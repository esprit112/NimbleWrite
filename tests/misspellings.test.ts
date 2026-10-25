import { describe, expect, it } from 'vitest';
import { dialectMap, runLocalRules } from '../shared/rules';
import { allKnownMistakes } from '../shared/data/misspellings';
import { isCommonWord } from '../shared/text';
import type { Dialect } from '../shared/types';

const run = (text: string, dialect: Dialect = 'en-GB') => runLocalRules(text, { dialect, styleRules: [], mechanics: true });
const fixes = (text: string, dialect?: Dialect) =>
  run(text, dialect)
    .filter((i) => i.type === 'spelling' || i.title === 'Add the apostrophe')
    .map((i) => `${i.original}->${i.replacements[0]}`);

describe('everyday misspellings and contractions', () => {
  it('catches the classic mistakes every time, without the AI', () => {
    expect(fixes('Their going to the libary tomorow to return the books.')).toEqual(['libary->library', 'tomorow->tomorrow']);
    expect(fixes('I definately think we should go, but it dont matter.')).toEqual(['definately->definitely', "dont->don't"]);
    expect(fixes('We need alot of help.')).toEqual(['alot->a lot']);
  });

  it('keeps the writer’s case and apostrophe style', () => {
    expect(fixes('STOP, DONT DO THAT.')).toEqual(["DONT->DON'T"]);
    expect(fixes('Dont worry. Tomorow is fine.')).toEqual(["Dont->Don't", 'Tomorow->Tomorrow']);
    expect(fixes('It’s late and I dont know.')).toEqual(['dont->don’t']);
    expect(fixes('ive got it')).toEqual(["ive->I've"]);
  });

  it('gives one fix for a mistake at the start of a sentence, not a second capitalisation fix', () => {
    const issues = run('dont go. thats fine.');
    expect(issues.map((i) => `${i.original}->${i.replacements[0]}`)).toEqual(["dont->Don't", "thats->That's"]);
  });

  it('leaves names, links, code-like tokens and real words alone', () => {
    expect(fixes('I watched Kelsey Grammer last night.')).toEqual([]);
    expect(fixes('See example.com/libary or mail teh@example.com for details.')).toEqual([]);
    expect(fixes('The file is libary.ts, which we cant touch, and lets see what we were told.')).toEqual([]);
    expect(fixes('My colour, the programme and his judgement were travelling well.')).toEqual([]);
    expect(fixes('Tell me e.g. Grammer is fine.')).toEqual([]);
  });

  it('respects the mechanics switch', () => {
    expect(runLocalRules('the libary', { dialect: 'en-GB', styleRules: [], mechanics: false }).filter((i) => i.type === 'spelling')).toEqual([]);
  });

  it('only lists non-words that are wrong in every English variant', () => {
    const dialects: Dialect[] = ['en-GB', 'en-US', 'en-CA', 'en-AU', 'en-IN'];
    const variants = new Set<string>();
    for (const d of dialects) for (const [from, to] of dialectMap(d)) variants.add(from).add(to.toLowerCase());
    for (const [wrong, right, kind] of allKnownMistakes()) {
      // The common-words list was built from a corpus with apostrophes stripped ("dont"), so contractions skip this.
      if (kind === 'spelling') expect(isCommonWord(wrong), `${wrong} is a common word`).toBe(false);
      expect(variants.has(wrong), `${wrong} is a dialect spelling`).toBe(false);
      expect(wrong).not.toBe(right.toLowerCase());
      expect(wrong).toMatch(/^[a-z]+$/);
    }
  });
});
