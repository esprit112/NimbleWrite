// Pure text utilities: hashing, readability statistics and the overall writing score.
import type { Category } from './types';
import { COMMON_WORDS_TEXT } from './data/common-words';

/** Fast, well-distributed 53-bit string hash (cyrb53), returned as base36. */
export function hashText(str: string, seed = 0): string {
  let h1 = 0xdeadbeef ^ seed;
  let h2 = 0x41c6ce57 ^ seed;
  for (let i = 0; i < str.length; i++) {
    const ch = str.charCodeAt(i);
    h1 = Math.imul(h1 ^ ch, 2654435761);
    h2 = Math.imul(h2 ^ ch, 1597334677);
  }
  h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507) ^ Math.imul(h2 ^ (h2 >>> 13), 3266489909);
  h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507) ^ Math.imul(h1 ^ (h1 >>> 13), 3266489909);
  return (4294967296 * (2097151 & h2) + (h1 >>> 0)).toString(36);
}

const WORD_RE = /[A-Za-z\u00C0-\u024F0-9]+(?:['’\-][A-Za-z\u00C0-\u024F0-9]+)*/g;

export function words(text: string): string[] {
  return text.match(WORD_RE) ?? [];
}

export function countWords(text: string): number {
  return words(text).length;
}

const ABBREVIATION_END = /\b(?:e\.g|i\.e|etc|vs|mr|mrs|ms|dr|prof|st|no|fig|approx|inc|ltd|jr|sr)\.$/i;
const INITIAL_END = /(?:^|\s)[A-Z]\.$/;

/** Split into sentences. Paragraph breaks always end a sentence; decimals and abbreviations do not. */
export function sentences(text: string): string[] {
  const out: string[] = [];
  for (const block of text.split(/\n+/)) {
    // Split where a terminator (plus closing quotes) is followed by space and a capital/number/opening quote.
    const parts = block.split(/(?<=[.!?…]["'”’)\]]*)\s+(?=["“'‘(\[]?[A-Z0-9])/);
    let carry = '';
    parts.forEach((part, idx) => {
      const piece = carry ? `${carry} ${part}` : part;
      const isLast = idx === parts.length - 1;
      if (!isLast && (ABBREVIATION_END.test(piece.trim()) || INITIAL_END.test(piece.trim()))) {
        carry = piece;
        return;
      }
      carry = '';
      if (words(piece).length) out.push(piece.trim());
    });
  }
  return out;
}

export function syllables(word: string): number {
  let w = word.toLowerCase().replace(/[^a-z]/g, '');
  if (!w) return 0;
  if (w.length <= 3) return 1;
  w = w.replace(/(?:[^laeiouy]es|ed|[^laeiouy]e)$/, '').replace(/^y/, '');
  const groups = w.match(/[aeiouy]{1,2}/g);
  return Math.max(1, groups ? groups.length : 1);
}

let commonSet: Set<string> | null = null;
function common(): Set<string> {
  if (!commonSet) commonSet = new Set(COMMON_WORDS_TEXT.split(' '));
  return commonSet;
}

/** True for words among the 5,000 most common English words. */
export function isCommonWord(word: string): boolean {
  return common().has(word.toLowerCase());
}

export interface TextStats {
  characters: number;
  charactersNoSpaces: number;
  words: number;
  sentences: number;
  paragraphs: number;
  readingTimeSec: number;
  speakingTimeSec: number;
  avgWordLength: number;
  avgSentenceLength: number;
  readability: number;
  gradeLevel: number;
  uniqueWordsPct: number;
  rareWordsPct: number;
  longSentences: number;
}

export function computeStats(text: string): TextStats {
  const ws = words(text);
  const sents = sentences(text);
  const wordCount = ws.length;
  const sentenceCount = Math.max(sents.length, wordCount ? 1 : 0);
  const letters = ws.reduce((s, w) => s + w.replace(/['’\-]/g, '').length, 0);
  const syl = ws.reduce((s, w) => s + syllables(w), 0);
  const lower = ws.map((w) => w.toLowerCase().replace(/['’]s$/, ''));
  const freq = new Map<string, number>();
  for (const w of lower) freq.set(w, (freq.get(w) ?? 0) + 1);
  const usedOnce = lower.filter((w) => freq.get(w) === 1).length;
  const isRare = (w: string) => /[a-z]/.test(w) && !common().has(w) && !common().has(w.replace(/(?:s|es|ed|ing|ly)$/, ''));
  const rare = lower.filter(isRare).length;
  const wps = sentenceCount ? wordCount / sentenceCount : 0;
  const spw = wordCount ? syl / wordCount : 0;
  const flesch = wordCount ? 206.835 - 1.015 * wps - 84.6 * spw : 0;
  const grade = wordCount ? 0.39 * wps + 11.8 * spw - 15.59 : 0;
  return {
    characters: text.length,
    charactersNoSpaces: text.replace(/\s/g, '').length,
    words: wordCount,
    sentences: sents.length,
    paragraphs: text.split(/\n+/).filter((p) => p.trim()).length,
    readingTimeSec: Math.round((wordCount / 238) * 60),
    speakingTimeSec: Math.round((wordCount / 150) * 60),
    avgWordLength: wordCount ? letters / wordCount : 0,
    avgSentenceLength: wps,
    readability: Math.round(Math.min(100, Math.max(0, flesch))),
    gradeLevel: Math.max(0, Math.round(grade)),
    uniqueWordsPct: wordCount ? Math.round((usedOnce / wordCount) * 100) : 0,
    rareWordsPct: wordCount ? Math.round((rare / wordCount) * 100) : 0,
    longSentences: sents.filter((s) => words(s).length > 30).length,
  };
}

export function readabilityLabel(score: number): { label: string; audience: string } {
  if (score >= 90) return { label: 'Very easy', audience: 'an 11-year-old (5th grade)' };
  if (score >= 80) return { label: 'Easy', audience: 'a 12-year-old (6th grade)' };
  if (score >= 70) return { label: 'Fairly easy', audience: 'a 13-year-old (7th grade)' };
  if (score >= 60) return { label: 'Plain English', audience: 'a 13–15-year-old (8th–9th grade)' };
  if (score >= 50) return { label: 'Fairly difficult', audience: 'a 15–18-year-old (10th–12th grade)' };
  if (score >= 30) return { label: 'Difficult', audience: 'a university student' };
  return { label: 'Very difficult', audience: 'a university graduate' };
}

export function formatDuration(sec: number): string {
  if (sec < 60) return `${sec} sec`;
  const m = Math.floor(sec / 60);
  const s = sec % 60;
  return s ? `${m} min ${s} sec` : `${m} min`;
}

export const CATEGORY_WEIGHT: Record<Category, number> = { correctness: 1, clarity: 0.55, engagement: 0.35, delivery: 0.45 };

/**
 * Overall score (0–100) in the spirit of Grammarly's: open issues weighted by severity,
 * normalised per 100 words, with a small readability adjustment. Null when there is too little text.
 */
export function overallScore(wordCount: number, counts: Record<Category, number>, readability: number): number | null {
  if (wordCount < 5) return null;
  const weighted = (Object.keys(counts) as Category[]).reduce((s, c) => s + counts[c] * CATEGORY_WEIGHT[c], 0);
  const density = (weighted / Math.max(wordCount, 60)) * 100;
  let score = 100 * Math.exp(-0.12 * density);
  if (readability < 30) score -= 3;
  if (readability < 10) score -= 4;
  return Math.max(0, Math.min(100, Math.round(score)));
}

export type DiffPart = { type: 'same' | 'add' | 'del'; text: string };

/**
 * True when the text around `offset` already reads like `replacement`: a fix that only adds to `original` ("create" ->
 * "create.", "Also" -> "Also,", "issue" -> "big issue") whose additions are already there. Applying it would
 * duplicate them ("create..", "Also,,"), so such a suggestion is stale or wrong and must not be offered.
 */
export function alreadyApplied(text: string, offset: number, original: string, replacement: string): boolean {
  if (!original || replacement === original) return false;
  const k = replacement.indexOf(original);
  if (k < 0) return false;
  const start = offset - k;
  return start >= 0 && text.startsWith(replacement, start);
}

/** Word-level diff (LCS). Falls back to a whole replacement for very large inputs. */
export function diffWords(a: string, b: string): DiffPart[] {
  const ta = a.match(/\s+|[^\s]+/g) ?? [];
  const tb = b.match(/\s+|[^\s]+/g) ?? [];
  const n = ta.length;
  const m = tb.length;
  if (n * m > 4_000_000) return [{ type: 'del', text: a }, { type: 'add', text: b }];
  const dp: Uint32Array[] = Array.from({ length: n + 1 }, () => new Uint32Array(m + 1));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = ta[i] === tb[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }
  const out: DiffPart[] = [];
  const push = (type: DiffPart['type'], text: string) => {
    const last = out[out.length - 1];
    if (last && last.type === type) last.text += text;
    else out.push({ type, text });
  };
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (ta[i] === tb[j]) { push('same', ta[i]); i++; j++; }
    else if (dp[i + 1][j] >= dp[i][j + 1]) { push('del', ta[i]); i++; }
    else { push('add', tb[j]); j++; }
  }
  while (i < n) push('del', ta[i++]);
  while (j < m) push('add', tb[j++]);
  return out;
}
