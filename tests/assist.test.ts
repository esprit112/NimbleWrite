import { describe, expect, it } from 'vitest';
import type { Issue, Settings } from '../shared/types';
import { DEFAULT_GOALS } from '../shared/types';
import { assistFingerprint, mergeAssistIssues } from '../server/assist';

const settings = (over: Partial<Settings> = {}): Settings => ({
  dialect: 'en-GB',
  defaultGoals: DEFAULT_GOALS,
  showGoalsOnNewDoc: true,
  categories: { correctness: true, clarity: true, engagement: true, delivery: true },
  localRules: true,
  dictionary: [],
  styleRules: [],
  snippets: [],
  models: { check: 'nvidia/check', write: 'nvidia/write', translate: 'nvidia/translate' },
  theme: 'system',
  ...over,
});

const ai = (text: string, original: string, replacement: string, extra: Partial<Issue> = {}): Issue => ({
  offset: text.indexOf(original),
  length: original.length,
  original,
  replacements: [replacement],
  category: 'correctness',
  type: 'spelling',
  title: 'Correct your spelling',
  explanation: 'This word is misspelled.',
  source: 'ai',
  ...extra,
});

describe('mergeAssistIssues', () => {
  const text = 'We saw the the libary and we met in order to talk.';

  it('merges AI findings with local rules, sorted, without overlaps', () => {
    const merged = mergeAssistIssues(text, [ai(text, 'libary', 'library'), ai(text, 'the the', 'the', { type: 'repeated_word' })], settings());
    expect(merged.map((i) => `${i.original}->${i.replacements[0]}:${i.source}`)).toEqual([
      'the the->the:ai', // AI wins over the identical local repeated-word rule
      'libary->library:ai',
      'in order to->to:rule', // local clarity rule fills in what the model missed
    ]);
  });

  it('respects disabled categories and the personal dictionary', () => {
    const s = settings({ categories: { correctness: true, clarity: false, engagement: true, delivery: true }, dictionary: ['Libary'] });
    expect(mergeAssistIssues(text, [ai(text, 'libary', 'library')], s).map((i) => i.original)).toEqual(['the the']);
  });

  it('drops findings whose offsets do not match the text', () => {
    const bad = { ...ai(text, 'libary', 'library'), offset: 3 };
    expect(mergeAssistIssues(text, [bad], settings({ localRules: false }))).toEqual([]);
  });

  it('drops fixes that are already applied, so accepting can never duplicate text', () => {
    const t = 'Also, the folder could not be created.';
    const stale = [ai(t, 'Also', 'Also,', { type: 'punctuation' }), ai(t, 'created', 'created.', { type: 'punctuation' })];
    expect(mergeAssistIssues(t, stale, settings({ localRules: false }))).toEqual([]);
  });
});

describe('assistFingerprint', () => {
  it('changes with settings that change suggestions, but not with the dictionary', () => {
    const base = assistFingerprint(settings());
    expect(assistFingerprint(settings({ dialect: 'en-US' }))).not.toBe(base);
    expect(assistFingerprint(settings({ localRules: false }))).not.toBe(base);
    expect(assistFingerprint(settings({ dictionary: ['Roxy'] }))).toBe(base);
  });
});
