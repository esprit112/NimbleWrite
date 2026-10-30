// Split a model's wide finding (often a whole sentence rewritten at once) into the minimal word-level edits it
// contains, so every mistake gets its own underline and explanation.
import { diffWords, isCommonWord } from '../shared/text';

export interface EditHunk {
  /** Offset inside the finding's original text. */
  offset: number;
  original: string;
  replacement: string;
  type: string;
  explanation: string;
}

interface RawHunk {
  a0: number;
  a1: number;
  b0: number;
  b1: number;
  type: string;
}

const CONFUSABLE: string[][] = [
  ['their', 'there', "they're", 'they’re'], ['your', "you're", 'you’re'], ['its', "it's", 'it’s'], ['then', 'than'], ['affect', 'effect'],
  ['loose', 'lose'], ['to', 'too', 'two'], ['whose', "who's", 'who’s'], ['accept', 'except'], ['lead', 'led'], ['quite', 'quiet'],
  ['weather', 'whether'], ['were', 'where', "we're", 'we’re'], ['of', 'have'], ['principal', 'principle'], ['complement', 'compliment'],
  ['advice', 'advise'], ['breath', 'breathe'], ['desert', 'dessert'], ['past', 'passed'], ['sight', 'site', 'cite'], ['stationary', 'stationery'],
  ['practice', 'practise'], ['licence', 'license'], ['bare', 'bear'], ['brake', 'break'], ['fewer', 'less'], ['lay', 'lie'], ['who', 'whom'],
];

function confusable(a: string, b: string): boolean {
  const x = a.toLowerCase();
  const y = b.toLowerCase();
  return CONFUSABLE.some((set) => set.includes(x) && set.includes(y));
}

/** Levenshtein distance with adjacent transpositions (optimal string alignment). */
export function editDistance(a: string, b: string): number {
  const m = a.length;
  const n = b.length;
  const d: number[][] = Array.from({ length: m + 1 }, (_, i) => [i, ...new Array<number>(n).fill(0)]);
  for (let j = 1; j <= n; j++) d[0][j] = j;
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      d[i][j] = Math.min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + cost);
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) d[i][j] = Math.min(d[i][j], d[i - 2][j - 2] + 1);
    }
  }
  return d[m][n];
}

const letters = (s: string) => s.replace(/[^\p{L}\p{N}]/gu, '');
const isWs = (c: string | undefined) => !!c && /\s/.test(c);

function classify(del: string, add: string): string {
  if (!del) return 'missing_word';
  if (!add) return /^(in order|really|very|just|actually|basically|literally|quite|totally|simply|definitely|certainly|absolutely|of course|kind of|sort of)$/i.test(del) ? 'wordiness' : 'grammar';
  const dWords = del.split(/\s+/);
  if (dWords.length === 1) {
    if (del.toLowerCase() === add.toLowerCase()) return 'capitalization';
    if (letters(del) && letters(del) === letters(add)) return 'punctuation';
    if (confusable(del.replace(/[^\p{L}'’]/gu, ''), add.replace(/[^\p{L}'’]/gu, ''))) return 'word_confusion';
    const flatAdd = add.replace(/\s+/g, '');
    const limit = del.length >= 8 ? 3 : 2;
    if (!isCommonWord(letters(del)) && editDistance(del.toLowerCase(), flatAdd.toLowerCase()) <= limit) return 'spelling';
  }
  return 'grammar';
}

/** The edit type implied by the text itself (shared with locate.ts to correct mislabelled model findings). */
export const classifyEdit = classify;

function explain(type: string, del: string, add: string): string {
  switch (type) {
    case 'spelling': return 'This word is misspelled.';
    case 'capitalization': return add[0] && add[0] === add[0].toUpperCase() ? 'This needs a capital letter.' : 'This doesn’t need a capital letter.';
    case 'punctuation': return 'Fix the punctuation here.';
    case 'word_confusion': return `“${add}” is the right word here, not “${del}”.`;
    case 'missing_word': return 'A word is missing here.';
    case 'repeated_word': return `“${del}” is repeated.`;
    case 'wordiness': return `“${del}” adds words without adding meaning.`;
    default: return add ? `Use “${add}” instead of “${del}” here.` : `Remove “${del}”.`;
  }
}

/**
 * Break a wide rewrite into word-level edits. Returns null when the finding is already narrow (under 6 words)
 * or the rewrite restructures too much (fewer than half the words survive), in which case it stays one suggestion.
 */
export function splitWide(original: string, replacement: string): EditHunk[] | null {
  const words = original.trim().split(/\s+/).filter(Boolean);
  if (words.length < 6) return null;
  const parts = diffWords(original, replacement);
  let sameWords = 0;
  const raw: RawHunk[] = [];
  let ai = 0;
  let bi = 0;
  let cur: RawHunk | null = null;
  for (const p of parts) {
    if (p.type === 'same') {
      if (cur) raw.push(cur);
      cur = null;
      sameWords += p.text.trim() ? p.text.trim().split(/\s+/).length : 0;
      ai += p.text.length;
      bi += p.text.length;
      continue;
    }
    cur ??= { a0: ai, a1: ai, b0: bi, b1: bi, type: '' };
    if (p.type === 'del') cur.a1 = ai += p.text.length;
    else cur.b1 = bi += p.text.length;
  }
  if (cur) raw.push(cur);
  if (!raw.length || sameWords / words.length < 0.5) return null;

  // Trim whitespace at the edges of each change and classify it.
  const hunks: RawHunk[] = [];
  for (const h of raw) {
    while (h.a0 < h.a1 && isWs(original[h.a0])) h.a0++;
    while (h.a1 > h.a0 && isWs(original[h.a1 - 1])) h.a1--;
    while (h.b0 < h.b1 && isWs(replacement[h.b0])) h.b0++;
    while (h.b1 > h.b0 && isWs(replacement[h.b1 - 1])) h.b1--;
    // Punctuation both sides share ("tomorow," -> "tomorrow,") isn't part of the mistake: underline only the word.
    const isPunct = (c: string | undefined) => !!c && /[^\p{L}\p{N}\s]/u.test(c);
    while (h.a1 - h.a0 > 1 && h.b1 - h.b0 > 1 && isPunct(original[h.a1 - 1]) && original[h.a1 - 1] === replacement[h.b1 - 1]) { h.a1--; h.b1--; }
    while (h.a1 - h.a0 > 1 && h.b1 - h.b0 > 1 && isPunct(original[h.a0]) && original[h.a0] === replacement[h.b0]) { h.a0++; h.b0++; }
    const del = original.slice(h.a0, h.a1);
    const add = replacement.slice(h.b0, h.b1);
    if (!del && !add) continue; // whitespace-only change: the local spacing rules cover it
    h.type = classify(del, add);
    if (h.type === 'grammar' && !add) {
      const prev = /(\S+)\s+$/.exec(original.slice(0, h.a0))?.[1];
      if (prev && prev.toLowerCase() === del.toLowerCase()) h.type = 'repeated_word';
    }
    hunks.push(h);
  }

  // Join grammar edits separated by at most one short word ("me and him was" -> "he and I were") into one suggestion.
  const merged: RawHunk[] = [];
  for (const h of hunks) {
    const last = merged[merged.length - 1];
    const joinable = (t: string) => t === 'grammar' || t === 'word_confusion' || t === 'missing_word';
    if (last && joinable(last.type) && joinable(h.type) && last.a1 <= h.a0 && /^\s*[\p{L}'’]{0,4}\s*$/u.test(original.slice(last.a1, h.a0))) {
      last.a1 = h.a1;
      last.b1 = h.b1;
      last.type = 'grammar';
      continue;
    }
    merged.push({ ...h });
  }

  const out: EditHunk[] = [];
  for (const h of merged) {
    let a0 = h.a0;
    let a1 = h.a1;
    let add = replacement.slice(h.b0, h.b1);
    if (a0 === a1) {
      // Pure insertion: anchor it to the neighbouring word so there is something to underline.
      const before = /(\S+)(\s*)$/.exec(original.slice(0, a0));
      if (before) {
        a0 -= before[0].length;
        a1 = a0 + before[1].length;
        add = `${before[1]} ${add}`;
      } else {
        const after = /^(\s*)(\S+)/.exec(original.slice(a1));
        if (!after) continue;
        a0 += after[1].length;
        a1 = a0 + after[2].length;
        add = `${add} ${after[2]}`;
      }
    }
    const del = original.slice(a0, a1);
    if (del === add) continue;
    if (!add) {
      // A pure deletion takes one neighbouring space with it, so accepting it doesn't leave a double space.
      if (a0 > 0 && original[a0 - 1] === ' ') a0--;
      else if (original[a1] === ' ') a1++;
    }
    const cut = original.slice(a0, a1);
    out.push({ offset: a0, original: cut, replacement: add, type: h.type, explanation: explain(h.type, del, add) });
  }
  if (!out.length || (out.length === 1 && out[0].original.trim() === original.trim())) return null;
  return out;
}
