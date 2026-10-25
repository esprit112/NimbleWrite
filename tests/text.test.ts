import { describe, expect, it } from 'vitest';
import { alreadyApplied, computeStats, countWords, diffWords, formatDuration, hashText, overallScore, readabilityLabel, sentences } from '../shared/text';

describe('countWords', () => {
  it('treats contractions and hyphenated words as single words', () => {
    expect(countWords("It's a well-known fact, isn't it?")).toBe(6);
  });
  it('returns 0 for empty or punctuation-only text', () => {
    expect(countWords('')).toBe(0);
    expect(countWords(' — ... !!')).toBe(0);
  });
});

describe('sentences', () => {
  it('does not split on abbreviations, decimals or lower-case continuations', () => {
    expect(sentences('Dr. Smith arrived at 3.30 p.m. on Monday. He left early! Did he? Yes.')).toEqual([
      'Dr. Smith arrived at 3.30 p.m. on Monday.',
      'He left early!',
      'Did he?',
      'Yes.',
    ]);
  });
  it('keeps initials with the name', () => {
    expect(sentences('J. R. R. Tolkien wrote books. They sold well.')).toEqual(['J. R. R. Tolkien wrote books.', 'They sold well.']);
  });
  it('always breaks at paragraph boundaries', () => {
    expect(sentences('Line one\nLine two')).toEqual(['Line one', 'Line two']);
  });
});

describe('computeStats', () => {
  it('handles empty text', () => {
    const s = computeStats('');
    expect(s.words).toBe(0);
    expect(s.readability).toBe(0);
    expect(s.uniqueWordsPct).toBe(0);
    expect(s.rareWordsPct).toBe(0);
  });
  it('computes counts, times and bounded scores', () => {
    const text = 'The cat sat on the mat. The dog sat on the log. Everyone was happy.';
    const s = computeStats(text);
    expect(s.words).toBe(15);
    expect(s.sentences).toBe(3);
    expect(s.readingTimeSec).toBe(Math.round((15 / 238) * 60));
    expect(s.speakingTimeSec).toBe(Math.round((15 / 150) * 60));
    expect(s.readability).toBeGreaterThanOrEqual(80); // very simple text
    expect(s.readability).toBeLessThanOrEqual(100);
    expect(s.avgSentenceLength).toBeCloseTo(5, 5);
  });
  it('counts words outside the common-word list as rare', () => {
    const plain = computeStats('the house is big and the car is red');
    const rare = computeStats('the perspicacious ultracrepidarian obfuscated sesquipedalian verbiage');
    expect(plain.rareWordsPct).toBe(0);
    expect(rare.rareWordsPct).toBeGreaterThan(50);
  });
  it('flags long sentences', () => {
    const long = `${Array.from({ length: 35 }, (_, i) => `word${i}`).join(' ')}.`;
    expect(computeStats(long).longSentences).toBe(1);
  });
});

describe('overallScore', () => {
  const none = { correctness: 0, clarity: 0, engagement: 0, delivery: 0 };
  it('is null for very short text', () => {
    expect(overallScore(3, none, 80)).toBeNull();
  });
  it('is 100 for clean, readable text and drops as issues increase', () => {
    expect(overallScore(200, none, 70)).toBe(100);
    const one = overallScore(200, { ...none, correctness: 1 }, 70)!;
    const five = overallScore(200, { ...none, correctness: 5 }, 70)!;
    expect(one).toBeLessThan(100);
    expect(five).toBeLessThan(one);
  });
  it('weights correctness more heavily than engagement', () => {
    expect(overallScore(200, { ...none, correctness: 3 }, 70)!).toBeLessThan(overallScore(200, { ...none, engagement: 3 }, 70)!);
  });
});

describe('diffWords', () => {
  it('reconstructs both inputs from the parts', () => {
    const a = 'Their going to the libary tomorow.';
    const b = "They're going to the library tomorrow.";
    const parts = diffWords(a, b);
    expect(parts.filter((p) => p.type !== 'add').map((p) => p.text).join('')).toBe(a);
    expect(parts.filter((p) => p.type !== 'del').map((p) => p.text).join('')).toBe(b);
    expect(parts.some((p) => p.type === 'same' && p.text.includes('going to the'))).toBe(true);
  });
});

describe('misc helpers', () => {
  it('hashText is deterministic and input-sensitive', () => {
    expect(hashText('hello')).toBe(hashText('hello'));
    expect(hashText('hello')).not.toBe(hashText('hello.'));
  });
  it('formatDuration', () => {
    expect(formatDuration(45)).toBe('45 sec');
    expect(formatDuration(60)).toBe('1 min');
    expect(formatDuration(75)).toBe('1 min 15 sec');
  });
  it('readabilityLabel', () => {
    expect(readabilityLabel(95).label).toBe('Very easy');
    expect(readabilityLabel(10).label).toBe('Very difficult');
  });
});

describe('alreadyApplied (fixes whose additions are already in the text)', () => {
  it('spots added punctuation that is already there', () => {
    const t = 'It could not create.';
    expect(alreadyApplied(t, t.indexOf('create'), 'create', 'create.')).toBe(true);
    expect(alreadyApplied('Also, if I open it', 0, 'Also', 'Also,')).toBe(true);
    expect(alreadyApplied('Also if I open it', 0, 'Also', 'Also,')).toBe(false);
  });
  it('spots an added word before or around the original', () => {
    const t = 'The big issue was the folder.';
    expect(alreadyApplied(t, t.indexOf('issue'), 'issue', 'big issue')).toBe(true);
    expect(alreadyApplied('The issue was the folder.', 4, 'issue', 'big issue')).toBe(false);
  });
  it('never fires for real changes', () => {
    expect(alreadyApplied('the the cat', 0, 'the the', 'the')).toBe(false); // deletion
    expect(alreadyApplied('the cat', 0, 'the', 'The')).toBe(false); // case change
    expect(alreadyApplied("its here and it's here", 0, 'its', "it's")).toBe(false);
    expect(alreadyApplied('teh cat', 0, 'teh', 'teh')).toBe(false);
    expect(alreadyApplied('x', 0, '', 'y')).toBe(false);
  });
});
