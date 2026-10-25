import { describe, expect, it } from 'vitest';
import type { Category } from '../shared/types';
import { CATEGORIES } from '../shared/types';
import { locate, placeFindings } from '../server/locate';
import { balancedEnd, parseLooseJson, salvageObjects, stripThinking } from '../server/json';
import { violatesDialect } from '../server/routes/ai';
import { dialectMap } from '../shared/rules';

const ALL = new Set<Category>(CATEGORIES);

describe('placeFindings: exact offsets from model output', () => {
  const text = 'Their going to the libary tomorow, and me and him was planning to finish the report.';

  it('places a finding by its original text', () => {
    const [i] = placeFindings(text, [{ original: 'libary', replacement: 'library', type: 'spelling', explanation: 'Misspelt.' }], ALL);
    expect(text.slice(i.offset, i.offset + i.length)).toBe('libary');
    expect(i).toMatchObject({ category: 'correctness', replacements: ['library'], source: 'ai', title: 'Correct your spelling' });
  });

  it('uses the "before" context to pick the right occurrence', () => {
    const t = 'I saw the cat and the dog.';
    const [i] = placeFindings(t, [{ original: 'the', before: 'cat and', replacement: 'a', type: 'article' }], ALL);
    expect(i.offset).toBe(t.lastIndexOf('the'));
  });

  it('drops findings that are not in the text, or that change nothing', () => {
    expect(placeFindings(text, [{ original: 'banana', replacement: 'apple', type: 'spelling' }], ALL)).toEqual([]);
    expect(placeFindings(text, [{ original: 'report', replacement: 'report', type: 'spelling' }], ALL)).toEqual([]);
  });

  it('drops fixes that are already in the text, which would otherwise duplicate it when accepted', () => {
    // Seen live: the model asked for a full stop that was already there ("create" -> "create.").
    const t = 'The only problem was the folder it could not create.';
    expect(placeFindings(t, [{ original: 'create', replacement: 'create.', type: 'punctuation' }], ALL)).toEqual([]);
    expect(placeFindings('Also, if I open it, will it check?', [{ original: 'Also', replacement: 'Also,', type: 'punctuation' }], ALL)).toEqual([]);
    // ...but the same fix is still offered where it's needed.
    const [i] = placeFindings('Also if I open it, will it check?', [{ original: 'Also', replacement: 'Also,', type: 'punctuation' }], ALL);
    expect(i).toMatchObject({ offset: 0, original: 'Also', replacements: ['Also,'] });
  });

  it('matches across curly/straight quotes and returns the real text', () => {
    const t = 'It’s John’s book.';
    const [i] = placeFindings(t, [{ original: "John's", replacement: "Jon's", type: 'spelling' }], ALL);
    expect(i.original).toBe('John’s');
    expect(t.slice(i.offset, i.offset + i.length)).toBe('John’s');
  });

  it('keeps a sentence-initial capital', () => {
    const [i] = placeFindings('Their going home.', [{ original: 'Their', replacement: "they're", type: 'word_confusion' }], ALL);
    expect(i.replacements).toEqual(["They're"]);
  });

  it('respects the allowed categories', () => {
    const f = [{ original: 'in order to', replacement: 'to', type: 'wordiness' }];
    expect(placeFindings('We met in order to talk.', f, new Set<Category>(['correctness']))).toEqual([]);
    expect(placeFindings('We met in order to talk.', f, ALL)[0].category).toBe('clarity');
  });

  it('prefers whole-word matches and never books the same span twice', () => {
    expect(locate('category cat', 'cat', '', [])).toBe(9);
    const t = 'the cat and the dog';
    const out = placeFindings(t, [{ original: 'the', replacement: 'The', type: 'capitalization' }, { original: 'the', replacement: 'a', type: 'article' }], ALL);
    expect(new Set(out.map((i) => i.offset)).size).toBe(out.length);
  });

  it('accepts multiple replacements and de-duplicates them', () => {
    const [i] = placeFindings('This is very good.', [{ original: 'very good', replacements: ['excellent', 'great', 'excellent'], type: 'bland_word' }], ALL);
    expect(i.replacements).toEqual(['excellent', 'great']);
    expect(i.category).toBe('engagement');
  });
});

describe('tolerant JSON parsing', () => {
  it('strips think blocks and code fences, and repairs trailing commas', () => {
    expect(parseLooseJson('<think>let me see…</think>```json\n{"issues":[{"a":1},]}\n```')).toEqual({ issues: [{ a: 1 }] });
  });
  it('finds JSON surrounded by prose', () => {
    expect(parseLooseJson('Sure! Here it is: {"a": 2} Hope that helps.')).toEqual({ a: 2 });
  });
  it('hides everything after an unterminated think block', () => {
    expect(stripThinking('{"a":1}<think>still thinking')).toBe('{"a":1}');
  });
  it('salvages complete objects from truncated output', () => {
    const out = salvageObjects('{"issues":[{"original":"x","replacement":"y"},{"original":"z","repl', 'original');
    expect(out).toEqual([{ original: 'x', replacement: 'y' }]);
  });
  it('balancedEnd ignores brackets inside strings', () => {
    expect(balancedEnd('{"a":"}"}', 0)).toBe(8);
    expect(balancedEnd('{"a":1', 0)).toBe(-1);
  });
});

describe('violatesDialect (filters AI suggestions that break the chosen English variant)', () => {
  const gb = dialectMap('en-GB');
  it('rejects introducing a wrong-variant spelling', () => {
    expect(violatesDialect('organised', 'organized', gb)).toBe(true);
    expect(violatesDialect('The colours', 'The colors', gb)).toBe(true);
  });
  it('allows fixes that keep the variant, or words already in the original', () => {
    expect(violatesDialect('recieve', 'receive', gb)).toBe(false);
    expect(violatesDialect('the color scheme', 'the color palette', gb)).toBe(false);
  });
});
