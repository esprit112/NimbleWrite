import { describe, expect, it } from 'vitest';
import type { Issue } from '../shared/types';
import { dedupeIssues, dialectMap, matchCase, runLocalRules } from '../shared/rules';

const run = (text: string, dialect: 'en-GB' | 'en-US' | 'en-CA' = 'en-GB', mechanics = true) => runLocalRules(text, { dialect, styleRules: [], mechanics });
const fixes = (issues: Issue[], type: string) => issues.filter((i) => i.type === type).map((i) => `${i.original}→${i.replacements[0]}`);

describe('English variants', () => {
  it('British English flags American spellings', () => {
    expect(fixes(run('We need to organize the color scheme at the center.'), 'dialect')).toEqual(['organize→organise', 'color→colour', 'center→centre']);
  });
  it('American English flags British spellings', () => {
    expect(fixes(run('We need to organise the colour scheme at the centre.', 'en-US'), 'dialect')).toEqual(['organise→organize', 'colour→color', 'centre→center']);
  });
  it('Canadian English uses -ize but -our', () => {
    expect(fixes(run('I will organise the colour.', 'en-CA'), 'dialect')).toEqual(['organise→organize']);
    expect(fixes(run('I will organize the color.', 'en-CA'), 'dialect')).toEqual(['color→colour']);
  });
  it('preserves capitalisation', () => {
    expect(fixes(run('Organize it. COLOR matters.'), 'dialect')).toEqual(['Organize→Organise', 'COLOR→COLOUR']);
  });
  it('does not flag words that are correct in both variants', () => {
    expect(fixes(run('The size of the prize was a surprise to the colonel.'), 'dialect')).toEqual([]);
    expect(dialectMap('en-US').has('grey')).toBe(false);
    expect(dialectMap('en-GB').get('analyze')).toBe('analyse');
    expect(dialectMap('en-US').get('analyse')).toBe('analyze');
  });
});

describe('mechanics', () => {
  it('finds repeated words but allows "had had" and "that that"', () => {
    expect(fixes(run('This is the the best.'), 'repeated_word')).toEqual(['the the→the']);
    expect(fixes(run('She had had enough, and I know that that is true.'), 'repeated_word')).toEqual([]);
  });
  it('can be switched off without disabling dialect checks', () => {
    const issues = run('this is the the color.', 'en-GB', false);
    expect(issues.map((i) => i.type)).toEqual(['dialect']);
  });
  it('fixes spacing', () => {
    expect(fixes(run('Hello  world.'), 'spacing')).toEqual(['  → ']);
    expect(fixes(run('Hello , world.'), 'spacing')).toEqual([' ,→,']);
  });
  it('capitalises a lone "i" but not "i.e."', () => {
    expect(fixes(run('Yesterday i went home.'), 'capitalization')).toEqual(['i→I']);
    expect(fixes(run('Use a short form, i.e. an abbreviation.'), 'capitalization')).toEqual([]);
  });
  it('capitalises sentence starts, except after abbreviations', () => {
    expect(fixes(run('hello world. this is fine.'), 'capitalization')).toEqual(['hello→Hello', 'this→This']);
    expect(fixes(run('See e.g. this example.'), 'capitalization')).toEqual([]);
  });
});

describe('clarity rules', () => {
  it('suggests tighter phrasing for wordy phrases, keeping capitals', () => {
    const issues = run('In my opinion, I think that we met in order to talk. Due to the fact that it rained, we left.');
    expect(fixes(issues, 'wordiness')).toEqual(['In my opinion, I think→I think', 'in order to→to', 'Due to the fact that→Because']);
    expect(issues.filter((i) => i.type === 'wordiness').every((i) => i.category === 'clarity')).toBe(true);
  });
  it('removes redundant pairs', () => {
    expect(fixes(run('The end result was a very unique free gift.'), 'redundancy')).toEqual(['end result→result', 'very unique→unique', 'free gift→gift']);
  });
  it('matches whole phrases only and switches off with local checks', () => {
    expect(fixes(run('A recurring theme: borders and orders.'), 'wordiness')).toEqual([]);
    expect(run('We met in order to talk.', 'en-GB', false).filter((i) => i.type === 'wordiness')).toEqual([]);
  });
});

describe('style guide', () => {
  it('applies find/replace rules with case matching', () => {
    const issues = runLocalRules('Send an e-mail. E-mail me.', { dialect: 'en-GB', mechanics: false, styleRules: [{ id: '1', find: 'e-mail', replace: 'email' }] });
    expect(issues.map((i) => `${i.original}→${i.replacements[0]}`)).toEqual(['e-mail→email', 'E-mail→Email']);
    expect(issues.every((i) => i.category === 'delivery' && i.source === 'style' && i.type === 'style_guide')).toBe(true);
  });
  it('flags avoided terms when there is no replacement', () => {
    const [issue] = runLocalRules('We will leverage synergy.', { dialect: 'en-GB', mechanics: false, styleRules: [{ id: '1', find: 'leverage', replace: '', note: 'Say “use”.' }] });
    expect(issue.replacements).toEqual([]);
    expect(issue.explanation).toBe('Say “use”.');
  });
});

describe('helpers', () => {
  it('matchCase', () => {
    expect(matchCase('Color', 'colour')).toBe('Colour');
    expect(matchCase('COLOR', 'colour')).toBe('COLOUR');
    expect(matchCase('color', 'colour')).toBe('colour');
  });
  it('dedupeIssues keeps the first of overlapping issues and sorts by offset', () => {
    const mk = (offset: number, length: number, type: string): Issue => ({ offset, length, original: 'x'.repeat(length), replacements: ['y'], category: 'correctness', type, title: '', explanation: '', source: 'ai' });
    const out = dedupeIssues([mk(10, 5, 'a'), mk(12, 4, 'b'), mk(0, 3, 'c'), mk(15, 2, 'd')]);
    expect(out.map((i) => i.type)).toEqual(['c', 'a', 'd']);
  });
});
