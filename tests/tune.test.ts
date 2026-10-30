import { describe, expect, it } from 'vitest';
import type { Issue, Settings, TuneChange } from '../shared/types';
import { DEFAULT_GOALS } from '../shared/types';
import { applyTuneChanges, MAX_INSTRUCTIONS } from '../shared/tune';
import { parseTuneResult, tuneSystemPrompt } from '../server/tune';
import { checkSystemPrompt, preferencesLine, writeMessages } from '../server/prompts';
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
  customInstructions: '',
  mutedTypes: [],
  ...over,
});

let n = 0;
const id = () => `id${++n}`;

describe('applyTuneChanges', () => {
  it('applies each kind of change and returns only the fields that changed', () => {
    const changes: TuneChange[] = [
      { kind: 'instruction', text: 'Allow sentence fragments' },
      { kind: 'mute', type: 'passive_voice', title: 'Use the active voice' },
      { kind: 'dictionary', word: 'Nemotron' },
      { kind: 'style_rule', find: 'utilise', replace: 'use', note: 'Plain English' },
    ];
    const { patch, applied, skipped } = applyTuneChanges(settings(), changes, id);
    expect(applied).toBe(4);
    expect(skipped).toBe(0);
    expect(patch.customInstructions).toBe('- Allow sentence fragments');
    expect(patch.mutedTypes).toEqual(['passive_voice']);
    expect(patch.dictionary).toEqual(['Nemotron']);
    expect(patch.styleRules).toMatchObject([{ find: 'utilise', replace: 'use', note: 'Plain English' }]);
    expect(Object.keys(patch).sort()).toEqual(['customInstructions', 'dictionary', 'mutedTypes', 'styleRules']);
  });

  it('appends to existing preferences on a new line', () => {
    const { patch } = applyTuneChanges(settings({ customInstructions: 'Keep sentences long.\n' }), [{ kind: 'instruction', text: 'No American spellings' }], id);
    expect(patch.customInstructions).toBe('Keep sentences long.\n- No American spellings');
  });

  it('skips things that are already in place and leaves them out of the patch', () => {
    const s = settings({ customInstructions: '- Allow fragments', mutedTypes: ['wordiness'], dictionary: ['Nemotron'], styleRules: [{ id: 'a', find: 'Utilise', replace: 'use' }] });
    const { patch, applied, skipped } = applyTuneChanges(s, [
      { kind: 'instruction', text: 'allow fragments' },
      { kind: 'mute', type: 'wordiness', title: 'x' },
      { kind: 'unmute', type: 'passive_voice', title: 'x' },
      { kind: 'dictionary', word: 'nemotron' },
      { kind: 'style_rule', find: 'utilise', replace: 'use' },
    ], id);
    expect(applied).toBe(0);
    expect(skipped).toBe(5);
    expect(patch).toEqual({});
  });

  it('unmutes a muted type', () => {
    const { patch } = applyTuneChanges(settings({ mutedTypes: ['wordiness', 'tone'] }), [{ kind: 'unmute', type: 'wordiness', title: 'x' }], id);
    expect(patch.mutedTypes).toEqual(['tone']);
  });

  it('refuses a preference that would overflow the limit', () => {
    const { patch, skipped } = applyTuneChanges(settings({ customInstructions: 'x'.repeat(MAX_INSTRUCTIONS - 5) }), [{ kind: 'instruction', text: 'one more rule' }], id);
    expect(skipped).toBe(1);
    expect(patch).toEqual({});
  });
});

describe('parseTuneResult', () => {
  const none = { mutedTypes: [] as string[] };

  it('keeps valid changes and drops unknown issue types', () => {
    const r = parseTuneResult({ reply: 'Done.', instructions: ['Allow fragments'], mute: ['Passive Voice', 'not_a_type'], dictionary: ['Roxy'], styleRules: [{ find: 'e-mail', replace: 'email' }] }, none);
    expect(r?.reply).toBe('Done.');
    expect(r?.changes).toEqual([
      { kind: 'instruction', text: 'Allow fragments' },
      { kind: 'mute', type: 'passive_voice', title: 'Use the active voice' },
      { kind: 'dictionary', word: 'Roxy' },
      { kind: 'style_rule', find: 'e-mail', replace: 'email' },
    ]);
  });

  it('ignores muting what is already muted and unmuting what is not', () => {
    const r = parseTuneResult({ reply: 'ok', mute: ['wordiness'], unmute: ['wordiness', 'tone'] }, { mutedTypes: ['wordiness'] });
    expect(r?.changes).toEqual([{ kind: 'unmute', type: 'wordiness', title: 'Remove wordiness' }]);
  });

  it('drops an instruction that only repeats a mute, but keeps one that adds something', () => {
    const r = parseTuneResult({ reply: 'ok', instructions: ['Keep passive voice suggestions off', 'Keep my sentences long and flowing'], mute: ['passive_voice', 'run_on'] }, none);
    expect(r?.changes.filter((c) => c.kind === 'instruction')).toEqual([{ kind: 'instruction', text: 'Keep my sentences long and flowing' }]);
    expect(r?.changes.filter((c) => c.kind === 'mute').map((c) => (c as { type: string }).type)).toEqual(['passive_voice', 'run_on']);
  });

  it('allows a reply with no changes, and rejects output without a reply', () => {
    expect(parseTuneResult({ reply: 'That is a rewrite request, not a setting.' }, none)?.changes).toEqual([]);
    expect(parseTuneResult({ instructions: ['x'] }, none)).toBeNull();
    expect(parseTuneResult(null, none)).toBeNull();
    expect(parseTuneResult('text', none)).toBeNull();
  });

  it('tolerates malformed lists', () => {
    const r = parseTuneResult({ reply: 'ok', instructions: 'nope', mute: [1, null], styleRules: [null, { find: '' }, 5] }, none);
    expect(r).toEqual({ reply: 'ok', changes: [] });
  });

  it('lists the current preferences and the issue types in the prompt', () => {
    const p = tuneSystemPrompt({ customInstructions: '- Keep it long', mutedTypes: ['tone'] });
    expect(p).toContain('passive_voice (Use the active voice)');
    expect(p).toContain('Keep it long');
    expect(p).toContain('Currently muted: tone');
  });
});

describe('preferences reach the prompts', () => {
  const opts = { dialect: 'en-GB' as const, goals: DEFAULT_GOALS, dictionary: [], categories: ['correctness' as const] };

  it('adds them to the proofreading prompt only when set', () => {
    expect(checkSystemPrompt({ ...opts, instructions: '- Allow fragments' })).toContain("the writer's own rules");
    expect(checkSystemPrompt({ ...opts, instructions: '- Allow fragments' })).toContain('Allow fragments');
    expect(checkSystemPrompt(opts)).not.toContain("writer's own preferences");
    expect(preferencesLine('   ', 'check')).toBe('');
  });

  it('adds them to the writing prompts, including chat', () => {
    const base = { task: 'improve' as const, text: 'Hello there.' };
    expect(writeMessages(base, 'en-GB', DEFAULT_GOALS, '- No exclamation marks')[0].content).toContain('No exclamation marks');
    expect(writeMessages(base, 'en-GB', DEFAULT_GOALS)[0].content).not.toContain('standing preferences');
    expect(writeMessages({ ...base, task: 'chat' }, 'en-GB', DEFAULT_GOALS, '- No exclamation marks')[0].content).toContain('No exclamation marks');
  });
});

describe('muted types in the desktop companion path', () => {
  const text = 'It was decided by the team that we went in order to win.';
  const issue = (original: string, type: string, category: Issue['category']): Issue => ({
    offset: text.indexOf(original), length: original.length, original, replacements: ['x'], category, type, title: 't', explanation: 'e', source: 'ai',
  });

  it('drops muted types, keeps the rest', () => {
    const ai = [issue('It was decided by the team', 'passive_voice', 'clarity'), issue('in order to', 'wordiness', 'clarity')];
    const all = mergeAssistIssues(text, ai, settings()).map((i) => i.type);
    expect(all).toContain('passive_voice');
    expect(mergeAssistIssues(text, ai, settings({ mutedTypes: ['passive_voice'] })).map((i) => i.type)).not.toContain('passive_voice');
  });

  it('changes the fingerprint when preferences or muted types change, but not when only their order does', () => {
    const base = assistFingerprint(settings());
    expect(assistFingerprint(settings({ customInstructions: '- x' }))).not.toBe(base);
    expect(assistFingerprint(settings({ mutedTypes: ['tone'] }))).not.toBe(base);
    expect(assistFingerprint(settings({ mutedTypes: ['tone', 'wordiness'] }))).toBe(assistFingerprint(settings({ mutedTypes: ['wordiness', 'tone'] })));
  });
});
