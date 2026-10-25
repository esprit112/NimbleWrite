// TipTap extension that renders suggestions as underline decorations and keeps them attached
// to the right words while the user types (positions are mapped through every transaction).
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey, type EditorState } from '@tiptap/pm/state';
import { Decoration, DecorationSet, type EditorView } from '@tiptap/pm/view';
import type { Issue } from '../../shared/types';

export interface PlacedSuggestion extends Issue {
  id: string;
  from: number;
  to: number;
  blockKey: string;
  sig: string;
}

interface PluginState {
  items: PlacedSuggestion[];
  activeId: string | null;
  deco: DecorationSet;
}

type Meta = { type: 'set'; items: PlacedSuggestion[] } | { type: 'active'; id: string | null } | { type: 'remove'; id: string } | { type: 'removeMany'; ids: string[] };

export const suggestionsKey = new PluginKey<PluginState>('nw-suggestions');

export interface SuggestionsOptions {
  onClick: (id: string, rect: DOMRect) => void;
  onHover: (id: string, rect: DOMRect) => void;
  onHoverEnd: () => void;
}

function decorate(state: EditorState, items: PlacedSuggestion[], activeId: string | null): DecorationSet {
  const max = state.doc.content.size;
  return DecorationSet.create(
    state.doc,
    items
      .filter((s) => s.from < s.to && s.to <= max)
      .map((s) => Decoration.inline(s.from, s.to, { class: `nw-sg nw-${s.category}${s.id === activeId ? ' nw-active' : ''}`, 'data-sid': s.id }, { sid: s.id })),
  );
}

export const Suggestions = Extension.create<SuggestionsOptions>({
  name: 'nwSuggestions',
  addOptions() {
    return { onClick: () => undefined, onHover: () => undefined, onHoverEnd: () => undefined };
  },
  addProseMirrorPlugins() {
    const options = this.options;
    return [
      new Plugin<PluginState>({
        key: suggestionsKey,
        state: {
          init: () => ({ items: [], activeId: null, deco: DecorationSet.empty }),
          apply(tr, prev, _old, next) {
            const meta = tr.getMeta(suggestionsKey) as Meta | undefined;
            let { items, activeId } = prev;
            let changed = false;
            if (tr.docChanged) {
              items = items.flatMap((s) => {
                const a = tr.mapping.mapResult(s.from, 1);
                const b = tr.mapping.mapResult(s.to, -1);
                if (b.pos <= a.pos) return [];
                // Drop the suggestion as soon as the text it refers to has been edited.
                if (next.doc.textBetween(a.pos, b.pos, '\n', '\n') !== s.original) return [];
                return [{ ...s, from: a.pos, to: b.pos }];
              });
              changed = true;
            }
            if (meta?.type === 'set') {
              items = meta.items;
              changed = true;
            } else if (meta?.type === 'remove') {
              items = items.filter((s) => s.id !== meta.id);
              changed = true;
            } else if (meta?.type === 'removeMany') {
              const gone = new Set(meta.ids);
              items = items.filter((s) => !gone.has(s.id));
              changed = true;
            } else if (meta?.type === 'active') {
              activeId = meta.id;
              changed = true;
            }
            if (activeId && !items.some((s) => s.id === activeId)) activeId = null;
            return { items, activeId, deco: changed ? decorate(next, items, activeId) : prev.deco };
          },
        },
        props: {
          decorations: (state) => suggestionsKey.getState(state)?.deco,
          handleClick: (_view, _pos, event) => {
            const el = (event.target as HTMLElement | null)?.closest?.('.nw-sg');
            const sid = el?.getAttribute('data-sid');
            if (el && sid) options.onClick(sid, el.getBoundingClientRect());
            return false;
          },
          handleDOMEvents: {
            mouseover: (_view, event) => {
              const el = (event.target as HTMLElement | null)?.closest?.('.nw-sg');
              const sid = el?.getAttribute('data-sid');
              if (el && sid) options.onHover(sid, el.getBoundingClientRect());
              return false;
            },
            mouseout: (_view, event) => {
              const el = (event.target as HTMLElement | null)?.closest?.('.nw-sg');
              const to = event.relatedTarget as Node | null;
              if (el && !(to && el.contains(to))) options.onHoverEnd();
              return false;
            },
          },
        },
      }),
    ];
  },
});

export function getSuggestions(state: EditorState): { items: PlacedSuggestion[]; activeId: string | null } {
  const s = suggestionsKey.getState(state);
  return { items: s?.items ?? [], activeId: s?.activeId ?? null };
}

export function setSuggestions(view: EditorView, items: PlacedSuggestion[]): void {
  view.dispatch(view.state.tr.setMeta(suggestionsKey, { type: 'set', items } satisfies Meta).setMeta('addToHistory', false));
}

export function setActiveSuggestion(view: EditorView, id: string | null): void {
  if (getSuggestions(view.state).activeId === id) return;
  view.dispatch(view.state.tr.setMeta(suggestionsKey, { type: 'active', id } satisfies Meta).setMeta('addToHistory', false));
}

export function removeSuggestion(view: EditorView, id: string): void {
  view.dispatch(view.state.tr.setMeta(suggestionsKey, { type: 'remove', id } satisfies Meta).setMeta('addToHistory', false));
}

/** Document range to replace for a suggestion. Deleting a word also takes one neighbouring space with it. */
function replaceRangeFor(state: EditorState, s: PlacedSuggestion, replacement: string): { from: number; to: number } {
  let { from, to } = s;
  if (replacement === '') {
    const before = state.doc.textBetween(Math.max(0, from - 1), from, '\n', '\n');
    const after = state.doc.textBetween(to, Math.min(state.doc.content.size, to + 1), '\n', '\n');
    if (before === ' ' && (after === ' ' || after === '' || /[.,;:!?]/.test(after))) from -= 1;
    else if (after === ' ') to += 1;
  }
  return { from, to };
}

/** Replace the suggestion's text. Returns false if the text changed underneath it. */
export function applySuggestion(view: EditorView, s: PlacedSuggestion, replacement: string): boolean {
  const { state } = view;
  if (state.doc.textBetween(s.from, s.to, '\n', '\n') !== s.original) return false;
  const { from, to } = replaceRangeFor(state, s, replacement);
  const tr = replacement ? state.tr.insertText(replacement, from, to) : state.tr.delete(from, to);
  tr.setMeta(suggestionsKey, { type: 'remove', id: s.id } satisfies Meta);
  view.dispatch(tr);
  return true;
}

/**
 * Apply the first replacement of many suggestions in ONE transaction (a single undo step).
 * Works from the end of the document backwards so earlier positions stay valid. Returns what was applied.
 */
export function applySuggestionsBatch(view: EditorView, list: PlacedSuggestion[]): PlacedSuggestion[] {
  const { state } = view;
  const tr = state.tr;
  const applied: PlacedSuggestion[] = [];
  let floor = Infinity; // start of the lowest change made so far
  for (const s of [...list].sort((a, b) => b.from - a.from)) {
    const replacement = s.replacements[0];
    if (replacement === undefined || state.doc.textBetween(s.from, s.to, '\n', '\n') !== s.original) continue;
    const { from, to } = replaceRangeFor(state, s, replacement);
    if (to > floor) continue; // overlaps a change already made
    if (replacement) tr.insertText(replacement, from, to);
    else tr.delete(from, to);
    floor = from;
    applied.push(s);
  }
  if (applied.length) {
    tr.setMeta(suggestionsKey, { type: 'removeMany', ids: applied.map((s) => s.id) } satisfies Meta);
    view.dispatch(tr);
  }
  return applied;
}
