import { describe, expect, it, vi } from 'vitest';
import type { CheckOptions, CheckParagraph, CheckResponse, Issue } from '../shared/types';
import { DEFAULT_GOALS } from '../shared/types';
import { Lru } from '../server/lru';
import { checkKey, withCheckCache } from '../server/checkCache';

describe('Lru', () => {
  it('keeps the most recently used entries and drops the oldest', () => {
    const lru = new Lru<number>(2);
    lru.set('a', 1);
    lru.set('b', 2);
    expect(lru.get('a')).toBe(1); // a is now the most recent
    lru.set('c', 3); // so b goes
    expect(lru.get('b')).toBeUndefined();
    expect(lru.get('a')).toBe(1);
    expect(lru.get('c')).toBe(3);
    expect(lru.size).toBe(2);
  });

  it('replacing a key does not grow it', () => {
    const lru = new Lru<number>(2);
    lru.set('a', 1);
    lru.set('a', 2);
    expect(lru.size).toBe(1);
    expect(lru.get('a')).toBe(2);
  });
});

const options = (over: Partial<CheckOptions> = {}): CheckOptions => ({
  dialect: 'en-GB',
  goals: DEFAULT_GOALS,
  dictionary: [],
  categories: ['correctness', 'clarity'],
  ...over,
});

const issue = (text: string, original: string): Issue => ({
  offset: text.indexOf(original),
  length: original.length,
  original,
  replacements: ['x'],
  category: 'correctness',
  type: 'spelling',
  title: 'Correct your spelling',
  explanation: '',
  source: 'ai',
});

/** Stands in for the NVIDIA call: one finding per paragraph, and a count of what it was asked. */
function fakeModel() {
  const asked: string[][] = [];
  const run = vi.fn(async (ps: CheckParagraph[]): Promise<CheckResponse> => {
    asked.push(ps.map((p) => p.text));
    return { results: ps.map((p) => ({ id: p.id, issues: [issue(p.text, p.text.split(' ')[0])] })), model: 'nvidia/check', ms: 1200 };
  });
  return { run, asked };
}

describe('check cache', () => {
  const para = (id: string, text: string): CheckParagraph => ({ id, text });

  it('asks the model only about paragraphs it has not seen with these settings', async () => {
    const cache = new Lru<{ issues: Issue[]; model: string }>(100);
    const m = fakeModel();
    const key = (p: CheckParagraph) => checkKey('nvidia/check', options(), p.text);
    await withCheckCache([para('1', 'Teh first'), para('2', 'Recieve second')], key, m.run, cache);
    const again = await withCheckCache([para('9', 'Recieve second'), para('3', 'Brand new')], key, m.run, cache);
    expect(m.asked).toEqual([['Teh first', 'Recieve second'], ['Brand new']]);
    expect(again.results.map((r) => [r.id, r.issues[0].original])).toEqual([['9', 'Recieve'], ['3', 'Brand']]);
    expect(again.cached).toBe(1);
  });

  it('makes no call at all when every paragraph is cached, and says which model found them', async () => {
    const cache = new Lru<{ issues: Issue[]; model: string }>(100);
    const m = fakeModel();
    const key = (p: CheckParagraph) => checkKey('nvidia/check', options(), p.text);
    await withCheckCache([para('1', 'Teh first')], key, m.run, cache);
    const r = await withCheckCache([para('1', 'Teh first')], key, m.run, cache);
    expect(m.run).toHaveBeenCalledTimes(1);
    expect(r.model).toBe('nvidia/check');
    expect(r.ms).toBe(0);
  });

  it('a change to the settings that shape the check misses the cache', () => {
    const base = checkKey('nvidia/check', options(), 'Same text');
    expect(checkKey('nvidia/check', options(), 'Same text')).toBe(base);
    expect(checkKey('nvidia/other', options(), 'Same text')).not.toBe(base);
    expect(checkKey('nvidia/check', options({ dialect: 'en-US' }), 'Same text')).not.toBe(base);
    expect(checkKey('nvidia/check', options({ dictionary: ['Nemotron'] }), 'Same text')).not.toBe(base);
    expect(checkKey('nvidia/check', options({ goals: { ...DEFAULT_GOALS, formality: 'formal' } }), 'Same text')).not.toBe(base);
    expect(checkKey('nvidia/check', options({ instructions: 'I write for engineers' }), 'Same text')).not.toBe(base);
    expect(checkKey('nvidia/check', options(), 'Same text.')).not.toBe(base);
    // Order doesn't matter for lists that are really sets.
    expect(checkKey('nvidia/check', options({ categories: ['clarity', 'correctness'] }), 'Same text')).toBe(base);
  });

  it('caches nothing when the model fails, so the next try asks again', async () => {
    const cache = new Lru<{ issues: Issue[]; model: string }>(100);
    const key = (p: CheckParagraph) => checkKey('nvidia/check', options(), p.text);
    const failing = vi.fn(async (): Promise<CheckResponse> => {
      throw new Error('NIM is down');
    });
    await expect(withCheckCache([para('1', 'Teh first')], key, failing, cache)).rejects.toThrow('NIM is down');
    expect(cache.size).toBe(0);
  });

  it('hands out copies, so a caller changing a result cannot change the cache', async () => {
    const cache = new Lru<{ issues: Issue[]; model: string }>(100);
    const m = fakeModel();
    const key = (p: CheckParagraph) => checkKey('nvidia/check', options(), p.text);
    await withCheckCache([para('1', 'Teh first')], key, m.run, cache);
    const a = await withCheckCache([para('1', 'Teh first')], key, m.run, cache);
    a.results[0].issues[0].replacements.push('mutated');
    const b = await withCheckCache([para('1', 'Teh first')], key, m.run, cache);
    expect(b.results[0].issues[0].replacements).toEqual(['x']);
  });
});
