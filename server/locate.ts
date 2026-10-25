// Turn raw model findings ("original" text + optional preceding context) into exact offsets.
// LLMs are unreliable at counting characters, so we never trust offsets from the model.
import type { Category, Issue } from '../shared/types';
import { categoryForType, normaliseType, titleForType } from '../shared/meta';
import { dedupeIssues } from '../shared/rules';
import { alreadyApplied } from '../shared/text';
import { classifyEdit, splitWide } from './split';

export interface RawFinding {
  p?: unknown;
  original?: unknown;
  before?: unknown;
  replacement?: unknown;
  replacements?: unknown;
  category?: unknown;
  type?: unknown;
  explanation?: unknown;
}

const isWordChar = (c: string | undefined) => !!c && /[\p{L}\p{N}_'’]/u.test(c);

// Same-length normalisation so indexes map 1:1 back to the original text.
function fold(s: string): string {
  return s.replace(/[‘’`´]/g, "'").replace(/[“”«»]/g, '"').replace(/[‐‑‒–—]/g, '-').replace(/\u00a0/g, ' ').toLowerCase();
}

function indexesOf(hay: string, needle: string): number[] {
  const out: number[] = [];
  if (!needle) return out;
  let i = hay.indexOf(needle);
  while (i >= 0) {
    out.push(i);
    i = hay.indexOf(needle, i + 1);
  }
  return out;
}

/** Find where `original` sits in `text`, avoiding ranges already taken and preferring whole-word matches. */
export function locate(text: string, original: string, before: string, taken: Array<[number, number]>): number {
  if (!original.trim()) return -1;
  let candidates = indexesOf(text, original);
  if (!candidates.length) candidates = indexesOf(fold(text), fold(original));
  if (!candidates.length) return -1;
  const free = candidates.filter((pos) => !taken.some(([a, b]) => pos < b && a < pos + original.length));
  if (!free.length) return -1;
  const wholeWord = free.filter((pos) => {
    const startOk = !isWordChar(original[0]) || !isWordChar(text[pos - 1]);
    const endOk = !isWordChar(original[original.length - 1]) || !isWordChar(text[pos + original.length]);
    return startOk && endOk;
  });
  const pool = wholeWord.length ? wholeWord : free;
  const ctx = fold(before).trim().replace(/\s+/g, ' ');
  if (ctx && pool.length > 1) {
    const hit = pool.find((pos) => fold(text.slice(Math.max(0, pos - ctx.length - 12), pos)).replace(/\s+/g, ' ').trimEnd().endsWith(ctx));
    if (hit !== undefined) return hit;
  }
  return pool[0];
}

const str = (v: unknown) => (typeof v === 'string' ? v : v == null ? '' : String(v));

export function placeFindings(text: string, findings: RawFinding[], allowed: Set<Category>): Issue[] {
  const taken: Array<[number, number]> = [];
  const placed: Issue[] = [];
  for (const f of findings) {
    const original = str(f.original);
    const reps = Array.isArray(f.replacements) ? f.replacements.map(str) : [str(f.replacement)];
    const replacements = [...new Set(reps.filter((r) => r !== original))].slice(0, 3);
    if (!original || !replacements.length) continue;
    const type = normaliseType(f.type);
    const category = categoryForType(type, f.category);
    if (!allowed.has(category)) continue;
    const pos = locate(text, original, str(f.before), taken);
    if (pos < 0) continue;
    const actual = text.slice(pos, pos + original.length);
    const fixed = replacements.map((r) => adaptCase(text, pos, actual, trimEcho(text, pos, actual, r)));
    if (fixed.every((r) => r === actual)) continue;
    // "create" -> "create." when the full stop is already there: accepting it would write "create..".
    if (fixed.some((r) => alreadyApplied(text, pos, actual, r))) continue;
    // A whole sentence rewritten as one finding: split it into its word-level edits.
    const pieces = fixed.length === 1 ? splitWide(actual, fixed[0]) : null;
    if (pieces) {
      for (const h of pieces) {
        const cat = categoryForType(h.type, category);
        if (!allowed.has(cat)) continue;
        const off = pos + h.offset;
        if (alreadyApplied(text, off, h.original, h.replacement)) continue;
        taken.push([off, off + h.original.length]);
        placed.push({ offset: off, length: h.original.length, original: h.original, replacements: [h.replacement], category: cat, type: h.type, title: titleForType(h.type, cat), explanation: h.explanation, source: 'ai' });
      }
      continue;
    }
    const finalType = fixed.length === 1 ? reconcileType(type || 'grammar', actual, fixed[0]) : type || 'grammar';
    const finalCategory = finalType === (type || 'grammar') ? category : categoryForType(finalType, category);
    if (!allowed.has(finalCategory)) continue;
    taken.push([pos, pos + actual.length]);
    placed.push({
      offset: pos,
      length: actual.length,
      original: actual,
      replacements: [...new Set(fixed.filter((r) => r !== actual))],
      category: finalCategory,
      type: finalType,
      title: titleForType(finalType, finalCategory),
      explanation: str(f.explanation).slice(0, 300),
      source: 'ai',
    });
  }
  const order: Category[] = ['correctness', 'clarity', 'delivery', 'engagement'];
  placed.sort((a, b) => order.indexOf(a.category) - order.indexOf(b.category));
  return dedupeIssues(placed);
}

const foldWs = (s: string) => s.toLowerCase().replace(/\s+/g, ' ');

/**
 * Models sometimes repeat the words around a mistake in the replacement ("of" -> "we should have"). Accepting that
 * would duplicate text, so drop leading words that already sit just before the original and trailing words that
 * already follow it.
 */
export function trimEcho(text: string, pos: number, actual: string, replacement: string): string {
  let rep = replacement;
  const before = foldWs(text.slice(Math.max(0, pos - 160), pos));
  for (let i = rep.length - 1; i > 0; i--) {
    if (!/\s/.test(rep[i - 1]) || /\s/.test(rep[i])) continue;
    const head = foldWs(rep.slice(0, i));
    const at = before.length - head.length;
    if (at >= 0 && before.endsWith(head) && (at === 0 || !isWordChar(before[at - 1]))) {
      rep = rep.slice(i);
      break;
    }
  }
  const after = foldWs(text.slice(pos + actual.length, pos + actual.length + 160));
  for (let j = 1; j < rep.length; j++) {
    if (!/\s/.test(rep[j]) || /\s/.test(rep[j - 1])) continue;
    const tail = foldWs(rep.slice(j));
    if (after.startsWith(tail) && (after.length === tail.length || !isWordChar(after[tail.length]))) {
      rep = rep.slice(0, j);
      break;
    }
  }
  return rep;
}

/**
 * Spelling, capitalisation and punctuation can be checked against the edit itself, and models mislabel them
 * ("of" -> "have" as spelling, "dont" -> "don't" as capitalisation). Trust the text for single-word edits.
 */
export function reconcileType(type: string, actual: string, fixed: string): string {
  if (!['spelling', 'capitalization', 'punctuation'].includes(type) || /\s/.test(actual.trim()) || /\s/.test(fixed.trim())) return type;
  const implied = classifyEdit(actual.trim(), fixed.trim());
  if (implied === type || (implied === 'grammar' && type === 'spelling')) return type;
  return implied;
}

/** Keep a sentence-initial capital when the model's replacement starts in lower case. */
function adaptCase(text: string, pos: number, original: string, replacement: string): string {
  const o = original[0];
  const r = replacement[0];
  if (!o || !r) return replacement;
  const prefix = text.slice(0, pos);
  const startsSentence = prefix.trim() === '' || /[.!?…]["”’)]?\s+$/.test(prefix.slice(-8));
  const origUpper = o === o.toUpperCase() && o !== o.toLowerCase();
  const repLower = r === r.toLowerCase() && r !== r.toUpperCase();
  return startsSentence && origUpper && repLower ? r.toUpperCase() + replacement.slice(1) : replacement;
}
