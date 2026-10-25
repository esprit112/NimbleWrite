import { describe, expect, it } from 'vitest';
import { editDistance, splitWide } from '../server/split';
import { placeFindings, reconcileType, trimEcho } from '../server/locate';
import type { Category } from '../shared/types';

const show = (h: ReturnType<typeof splitWide>) => h?.map((x) => `${x.original}->${x.replacement}:${x.type}`);
const ALL = new Set<Category>(['correctness', 'clarity', 'engagement', 'delivery']);

describe('splitWide', () => {
  it('breaks a whole-sentence rewrite into word-level edits', () => {
    const h = splitWide('Their going to the libary tomorow, and we met in order to talk.', "They're going to the library tomorrow, and we met in order to talk.");
    expect(show(h)).toEqual(["Their->They're:word_confusion", 'libary->library:spelling', 'tomorow->tomorrow:spelling']);
    expect(h![1].explanation).toBe('This word is misspelled.');
  });

  it('keeps grammar edits separated by one short word together', () => {
    const h = splitWide('Yesterday me and him was planning to finish the report early.', 'Yesterday he and I were planning to finish the report early.');
    expect(show(h)).toEqual(['me and him was->he and I were:grammar']);
  });

  it('anchors a missing word to its neighbour, and flags a repeated word', () => {
    expect(show(splitWide('I going to the shops with my friends later.', 'I am going to the shops with my friends later.'))).toEqual(['I->I am:missing_word']);
    expect(show(splitWide('We saw the the film at the big cinema today.', 'We saw the film at the big cinema today.'))).toEqual([' the->:repeated_word']);
    expect(show(splitWide('We met at the library in order to talk about it.', 'We met at the library to talk about it.'))).toEqual([' in order->:wordiness']);
  });

  it('leaves narrow findings and heavy rewrites alone', () => {
    expect(splitWide('me and him was', 'he and I were')).toBeNull();
    expect(splitWide('The report was written by the team in a hurry.', 'Our team rushed through writing that report.')).toBeNull();
  });

  it('measures edit distance with transpositions', () => {
    expect(editDistance('recieve', 'receive')).toBe(1);
    expect(editDistance('libary', 'library')).toBe(1);
    expect(editDistance('kitten', 'sitting')).toBe(3);
  });
});

describe('placeFindings with a whole-sentence finding', () => {
  it('produces one underline per mistake', () => {
    const text = 'Their going to the libary tomorow, and we met in order to talk.';
    const issues = placeFindings(text, [{ original: text, replacement: "They're going to the library tomorrow, and we met in order to talk.", type: 'grammar', category: 'correctness' }], ALL);
    expect(issues.map((i) => text.slice(i.offset, i.offset + i.length))).toEqual(['Their', 'libary', 'tomorow']);
    expect(issues.map((i) => i.title)).toEqual(['Use the right word', 'Correct your spelling', 'Correct your spelling']);
  });
});

describe('cleaning up model findings', () => {
  const text = 'I think we should of left earlier, but it dont matter now.';

  it('drops context the model echoed around the replacement', () => {
    expect(trimEcho(text, text.indexOf('of'), 'of', 'we should have')).toBe('have');
    expect(trimEcho(text, text.indexOf('of'), 'of', 'have left')).toBe('have');
    expect(trimEcho(text, text.indexOf('of'), 'of', 'should have left earlier')).toBe('have');
    expect(trimEcho(text, text.indexOf('dont'), 'dont', "doesn't")).toBe("doesn't");
    // Only whole words count: "ould have" is not an echo of "should".
    expect(trimEcho(text, text.indexOf('of'), 'of', 'ould have')).toBe('ould have');
  });

  it('never hands out a replacement that would duplicate the surrounding words', () => {
    const issues = placeFindings(text, [{ original: 'of', replacement: 'we should have', type: 'spelling', category: 'correctness' }], ALL);
    expect(issues).toHaveLength(1);
    expect(issues[0].replacements).toEqual(['have']);
    const applied = text.slice(0, issues[0].offset) + issues[0].replacements[0] + text.slice(issues[0].offset + issues[0].length);
    expect(applied).toBe('I think we should have left earlier, but it dont matter now.');
  });

  it('corrects mechanical labels the text contradicts', () => {
    expect(reconcileType('spelling', 'of', 'have')).toBe('word_confusion');
    expect(reconcileType('capitalization', 'dont', "don't")).toBe('punctuation');
    expect(reconcileType('spelling', 'libary', 'library')).toBe('spelling');
    expect(reconcileType('capitalization', 'monday', 'Monday')).toBe('capitalization');
    expect(reconcileType('spelling', 'definately', 'definitely')).toBe('spelling');
    expect(reconcileType('grammar', 'dont', "doesn't")).toBe('grammar');
    const [dont] = placeFindings(text, [{ original: 'dont', replacement: "don't", type: 'capitalization', category: 'correctness' }], ALL);
    expect([dont.type, dont.category]).toEqual(['punctuation', 'correctness']);
  });
});
