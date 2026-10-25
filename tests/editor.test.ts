// Editor engine tests against a real ProseMirror schema (the same StarterKit schema the app uses).
import { describe, expect, it } from 'vitest';
import { getSchema } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { Node as PMNode } from '@tiptap/pm/model';
import { EditorState } from '@tiptap/pm/state';
import type { EditorView } from '@tiptap/pm/view';
import type { Issue } from '../shared/types';
import { extractBlocks } from '../src/editor/blocks';
import { applySuggestion, applySuggestionsBatch, type PlacedSuggestion } from '../src/editor/suggestions';
import { signature } from '../src/editor/checker';

const schema = getSchema([StarterKit]);
const p = (...content: object[]) => ({ type: 'paragraph', content });
const t = (text: string) => ({ type: 'text', text });

function stateOf(...blocks: object[]) {
  return EditorState.create({ schema, doc: PMNode.fromJSON(schema, { type: 'doc', content: blocks }) });
}

/** Minimal stand-in for EditorView: enough for dispatch-based helpers. */
function fakeView(state: EditorState) {
  const v = { state, dispatched: 0, dispatch(tr: Parameters<EditorState['apply']>[0]) { v.state = v.state.apply(tr); v.dispatched++; } };
  return v as typeof v & EditorView;
}

/** Turn a paragraph-relative issue into a placed suggestion at the paragraph's document position. */
function place(state: EditorState, blockIndex: number, original: string, replacement: string, at?: number): PlacedSuggestion {
  const b = extractBlocks(state.doc)[blockIndex];
  const offset = at ?? b.text.indexOf(original);
  if (offset < 0 || b.text.slice(offset, offset + original.length) !== original) throw new Error(`"${original}" not at ${offset} in block ${blockIndex}`);
  const issue: Issue = { offset, length: original.length, original, replacements: [replacement], category: 'correctness', type: 'spelling', title: '', explanation: '', source: 'ai' };
  return { ...issue, id: `${b.key}:${offset}`, from: b.from + offset, to: b.from + offset + original.length, blockKey: b.key, sig: signature(b.text, issue) };
}

const text = (s: EditorState) => s.doc.textBetween(0, s.doc.content.size, '\n', '\n');

describe('extractBlocks', () => {
  it('maps paragraph characters 1:1 to document positions, across marks, hard breaks and lists', () => {
    const state = stateOf(
      { type: 'heading', attrs: { level: 1 }, content: [t('Title here')] },
      p(t('Hello '), { type: 'text', text: 'bold', marks: [{ type: 'bold' }] }, t(' world')),
      p(t('Line one'), { type: 'hardBreak' }, t('Line two')),
      { type: 'codeBlock', content: [t('const x = 1;')] },
      { type: 'bulletList', content: [{ type: 'listItem', content: [p(t('Item one'))] }] },
    );
    const blocks = extractBlocks(state.doc);
    expect(blocks.map((b) => b.text)).toEqual(['Title here', 'Hello bold world', 'Line one\nLine two', 'Item one']); // code skipped
    for (const b of blocks) {
      expect(state.doc.textBetween(b.from, b.from + b.text.length, '\n', '\n')).toBe(b.text);
    }
    const hello = blocks[1];
    expect(state.doc.textBetween(hello.from + 11, hello.from + 16)).toBe('world');
    const lines = blocks[2];
    expect(state.doc.textBetween(lines.from + 9, lines.from + 17)).toBe('Line two');
  });

  it('gives identical paragraphs the same cache key', () => {
    const blocks = extractBlocks(stateOf(p(t('Same text.')), p(t('Same text.')), p(t('Other.'))).doc);
    expect(blocks[0].key).toBe(blocks[1].key);
    expect(blocks[0].key).not.toBe(blocks[2].key);
  });
});

describe('applying suggestions', () => {
  it('replaces one suggestion and refuses stale ones', () => {
    const view = fakeView(stateOf(p(t('I went to the libary.'))));
    const s = place(view.state, 0, 'libary', 'library');
    expect(applySuggestion(view, s, 'library')).toBe(true);
    expect(text(view.state)).toBe('I went to the library.');
    expect(applySuggestion(view, s, 'library')).toBe(false); // text no longer matches
  });

  it('accepts many suggestions in a single transaction (one undo step)', () => {
    const view = fakeView(stateOf(p(t('Their going to the libary tomorow.')), p(t('We has no time.'))));
    const list = [
      place(view.state, 0, 'Their', "They're"),
      place(view.state, 0, 'libary', 'library'),
      place(view.state, 0, 'tomorow', 'tomorrow'),
      place(view.state, 1, 'has', 'have'),
    ];
    const applied = applySuggestionsBatch(view, list);
    expect(applied).toHaveLength(4);
    expect(view.dispatched).toBe(1);
    expect(text(view.state)).toBe("They're going to the library tomorrow.\nWe have no time.");
  });

  it('removes a deleted word together with one neighbouring space', () => {
    const mid = fakeView(stateOf(p(t('It is very good.'))));
    applySuggestion(mid, place(mid.state, 0, 'very', ''), '');
    expect(text(mid.state)).toBe('It is good.');

    const end = fakeView(stateOf(p(t('It is good really'))));
    applySuggestion(end, place(end.state, 0, 'really', ''), '');
    expect(text(end.state)).toBe('It is good');

    const start = fakeView(stateOf(p(t('Basically it works.'))));
    applySuggestion(start, place(start.state, 0, 'Basically', ''), '');
    expect(text(start.state)).toBe('it works.');
  });

  it('skips overlapping and stale suggestions in a batch', () => {
    const view = fakeView(stateOf(p(t('We saw saw the film.'))));
    const a = place(view.state, 0, 'saw saw', 'saw', 3); // 3..10
    const b = { ...place(view.state, 0, 'saw the', 'watched the', 7), id: 'b' }; // 7..14, overlaps a
    const stale = { ...place(view.state, 0, 'film', 'movie', 15), original: 'flim', id: 'stale' };
    const applied = applySuggestionsBatch(view, [a, b, stale]);
    expect(applied).toHaveLength(1);
    expect(view.dispatched).toBe(1);
    expect(['We saw the film.', 'We saw watched the film.']).toContain(text(view.state));
  });
});

describe('dismissal signatures', () => {
  it('stay stable when text more than 24 characters earlier in the paragraph changes', () => {
    const issue = (offset: number): Issue => ({ offset, length: 6, original: 'libary', replacements: ['library'], category: 'correctness', type: 'spelling', title: '', explanation: '', source: 'ai' });
    const a = 'Some long introduction that goes on for a while. We then walked over to the libary.';
    const b = 'Short. We then walked over to the libary.';
    expect(signature(a, issue(a.indexOf('libary')))).toBe(signature(b, issue(b.indexOf('libary'))));
    const c = 'Short. We then walked over to a libary.';
    expect(signature(c, issue(c.indexOf('libary')))).not.toBe(signature(b, issue(b.indexOf('libary'))));
  });
});
