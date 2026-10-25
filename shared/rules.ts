// Instant, deterministic checks that run in the browser on every keystroke (no AI round-trip).
// They complement the NIM model: cheap mechanical fixes, English-variant spelling and the user's style guide.
import type { Dialect, Issue, StyleRule } from './types';
import { knownMistake } from './data/misspellings';

type Pair = [us: string, uk: string];

const IZE_STEMS = [
  'organ', 'real', 'recogn', 'apolog', 'priorit', 'special', 'emphas', 'summar', 'minim', 'maxim', 'optim', 'categor',
  'critic', 'final', 'util', 'standard', 'visual', 'author', 'character', 'memor', 'custom', 'personal', 'modern', 'normal',
  'sympath', 'familiar', 'general', 'stabil', 'mobil', 'popular', 'harmon', 'symbol', 'monet', 'digit', 'central',
  'capital', 'global', 'local', 'legal', 'neutral', 'natural', 'scrutin', 'agon', 'patron', 'jeopard', 'subsid',
  'synchron', 'hypothes', 'theor', 'econom', 'vandal', 'terror', 'fertil', 'steril', 'colon', 'civil', 'fantas', 'ideal',
  'commercial', 'industrial', 'internal', 'rational', 'initial', 'sanit', 'random', 'final', 'privat', 'decentral',
];
const IZE_SUFFIXES: Pair[] = [['ize', 'ise'], ['izes', 'ises'], ['ized', 'ised'], ['izing', 'ising'], ['ization', 'isation'], ['izations', 'isations'], ['izer', 'iser'], ['izers', 'isers']];

const OUR_STEMS = ['col', 'fav', 'flav', 'hon', 'hum', 'lab', 'neighb', 'rum', 'behavi', 'harb', 'vap', 'vig', 'endeav', 'arm', 'parl', 'od', 'val', 'cand', 'clam', 'tum', 'sav', 'savi'];
const OUR_SUFFIXES = ['', 's', 'ed', 'ing', 'ful', 'fully', 'ite', 'ites', 'able', 'ably', 'less', 'er', 'ers', 'al', 'ally', 'hood', 'hoods'];
// Generated forms that are real words spelled the same in both variants.
const OUR_SKIP = new Set(['humoral', 'tumoral', 'honoral', 'valoral']);

const RE_PAIRS: Pair[] = [
  ['center', 'centre'], ['centers', 'centres'], ['centered', 'centred'], ['centering', 'centring'],
  ['theater', 'theatre'], ['theaters', 'theatres'], ['liter', 'litre'], ['liters', 'litres'], ['fiber', 'fibre'], ['fibers', 'fibres'],
  ['caliber', 'calibre'], ['somber', 'sombre'], ['saber', 'sabre'], ['specter', 'spectre'], ['luster', 'lustre'], ['meager', 'meagre'],
  ['maneuver', 'manoeuvre'], ['maneuvers', 'manoeuvres'], ['maneuvered', 'manoeuvred'],
];

const OTHER_PAIRS: Pair[] = [
  ['analyze', 'analyse'], ['analyzed', 'analysed'], ['analyzing', 'analysing'], ['paralyze', 'paralyse'], ['paralyzed', 'paralysed'],
  ['catalyze', 'catalyse'], ['defense', 'defence'], ['offense', 'offence'], ['gray', 'grey'], ['grays', 'greys'],
  ['traveling', 'travelling'], ['traveled', 'travelled'], ['traveler', 'traveller'], ['travelers', 'travellers'],
  ['canceled', 'cancelled'], ['canceling', 'cancelling'], ['labeled', 'labelled'], ['labeling', 'labelling'],
  ['modeled', 'modelled'], ['modeling', 'modelling'], ['fueled', 'fuelled'], ['fueling', 'fuelling'], ['signaled', 'signalled'],
  ['totaled', 'totalled'], ['marvelous', 'marvellous'], ['jewelry', 'jewellery'], ['counselor', 'counsellor'], ['counselors', 'counsellors'],
  ['catalog', 'catalogue'], ['catalogs', 'catalogues'], ['aluminum', 'aluminium'], ['mold', 'mould'], ['moldy', 'mouldy'],
  ['smolder', 'smoulder'], ['plow', 'plough'], ['pajamas', 'pyjamas'], ['fulfill', 'fulfil'], ['fulfillment', 'fulfilment'],
  ['enroll', 'enrol'], ['enrollment', 'enrolment'], ['skillful', 'skilful'], ['willful', 'wilful'], ['installment', 'instalment'],
  ['aging', 'ageing'], ['artifact', 'artefact'], ['artifacts', 'artefacts'], ['cozy', 'cosy'], ['estrogen', 'oestrogen'],
  ['anemia', 'anaemia'], ['pediatric', 'paediatric'], ['mustache', 'moustache'], ['tumor', 'tumour'], ['tumors', 'tumours'],
  ['airplane', 'aeroplane'], ['airplanes', 'aeroplanes'], ['donut', 'doughnut'], ['donuts', 'doughnuts'],
];

function izePairs(): Pair[] {
  const out: Pair[] = [];
  for (const stem of new Set(IZE_STEMS)) for (const [us, uk] of IZE_SUFFIXES) out.push([stem + us, stem + uk]);
  return out;
}

function ourPairs(): Pair[] {
  const out: Pair[] = [];
  for (const stem of OUR_STEMS) {
    for (const suf of OUR_SUFFIXES) {
      const us = `${stem}or${suf}`;
      if (OUR_SKIP.has(us)) continue;
      out.push([us, `${stem}our${suf}`]);
    }
  }
  return out;
}

const dialectCache = new Map<Dialect, Map<string, string>>();

/** Map of lower-case "wrong for this variant" spellings to the preferred spelling. */
export function dialectMap(dialect: Dialect): Map<string, string> {
  const cached = dialectCache.get(dialect);
  if (cached) return cached;
  const map = new Map<string, string>();
  const add = (pairs: Pair[], wrongIsUs: boolean) => {
    for (const [us, uk] of pairs) {
      if (wrongIsUs) map.set(us, uk);
      else map.set(uk, us);
    }
  };
  const ize = izePairs();
  const our = ourPairs();
  // Canadian English: -our and -re like British, -ize like American, doubled L like British.
  const CA_OTHER = new Set(['traveling', 'traveled', 'traveler', 'travelers', 'canceled', 'canceling', 'labeled', 'labeling', 'modeled',
    'modeling', 'fueled', 'fueling', 'signaled', 'totaled', 'marvelous', 'jewelry', 'counselor', 'counselors', 'defense', 'offense',
    'pajamas', 'mold', 'moldy', 'smolder', 'cozy', 'mustache', 'tumor', 'tumors']);
  if (dialect === 'en-US') {
    add(ize, false); add(our, false); add(RE_PAIRS, false); add(OTHER_PAIRS.filter(([us]) => us !== 'gray' && us !== 'grays'), false);
  } else if (dialect === 'en-CA') {
    add(ize, false); add(our, true); add(RE_PAIRS, true); add(OTHER_PAIRS.filter(([us]) => CA_OTHER.has(us)), true);
  } else {
    add(ize, true); add(our, true); add(RE_PAIRS, true); add(OTHER_PAIRS, true);
  }
  dialectCache.set(dialect, map);
  return map;
}

const DIALECT_NAME: Record<Dialect, string> = {
  'en-GB': 'British English', 'en-US': 'American English', 'en-CA': 'Canadian English', 'en-AU': 'Australian English', 'en-IN': 'Indian English',
};

export function matchCase(source: string, target: string): string {
  if (source.length > 1 && source === source.toUpperCase()) return target.toUpperCase();
  if (source[0] && source[0] === source[0].toUpperCase() && source[0] !== source[0].toLowerCase()) return target[0].toUpperCase() + target.slice(1);
  return target;
}

function escapeRe(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

const ABBREV_BEFORE = /\b(?:e\.g|i\.e|etc|vs|mr|mrs|ms|dr|prof|st|no|fig|approx|cf|al)\.\s*$/i;

/**
 * Whether text[index] starts a sentence: the start of the text or of a line, or after . ! ? — but not after an
 * abbreviation ("e.g."), an initial ("J.") or an ellipsis, where the next word usually continues the sentence.
 */
function sentenceStart(text: string, index: number): boolean {
  const from = Math.max(0, index - 60);
  const before = text.slice(from, index);
  const lineStart = from === 0 ? /(?:^|[\r\n\v\u2028\u2029])[ \t"“‘'(]*$/ : /[\r\n\v\u2028\u2029][ \t"“‘'(]*$/;
  if (lineStart.test(before)) return true;
  const m = /([.!?…])["”’)]*\s+["“‘(]*$/.exec(before);
  if (!m) return false;
  if (m[1] === '…') return false;
  if (m[1] !== '.') return true;
  const upToDot = before.slice(0, m.index + 1);
  return !/\.\.$/.test(upToDot) && !ABBREV_BEFORE.test(upToDot) && !/(?:^|[^\w])[a-z]\.$/i.test(upToDot);
}

// Clarity: wordy phrases with a tighter equivalent, and redundant pairs. Matched case-insensitively on word boundaries.
const WORDY: [phrase: string, replacement: string][] = [
  ['in order to', 'to'], ['due to the fact that', 'because'], ['owing to the fact that', 'because'], ['in light of the fact that', 'because'],
  ['despite the fact that', 'although'], ['in spite of the fact that', 'although'], ['at this point in time', 'now'], ['at the present time', 'now'],
  ['in the event that', 'if'], ['for the purpose of', 'for'], ['with regard to', 'about'], ['in regard to', 'about'], ['with reference to', 'about'],
  ['a large number of', 'many'], ['the majority of', 'most'], ['is able to', 'can'], ['are able to', 'can'], ['has the ability to', 'can'],
  ['have the ability to', 'can'], ['make a decision', 'decide'], ['in close proximity to', 'near'], ['in my opinion, i think', 'I think'],
  ['i personally think', 'I think'], ['each and every', 'every'], ['first and foremost', 'first'],
];
const REDUNDANT: [phrase: string, replacement: string][] = [
  ['absolutely essential', 'essential'], ['past history', 'history'], ['end result', 'result'], ['free gift', 'gift'], ['advance planning', 'planning'],
  ['basic fundamentals', 'fundamentals'], ['completely eliminate', 'eliminate'], ['unexpected surprise', 'surprise'], ['very unique', 'unique'],
  ['join together', 'join'], ['repeat again', 'repeat'], ['return back', 'return'], ['revert back', 'revert'], ['collaborate together', 'collaborate'],
  ['still remains', 'remains'], ['close proximity', 'proximity'],
];
const phraseRe = (list: [string, string][]) => new RegExp(`(?<![\\w'’-])(${list.map(([p]) => escapeRe(p)).join('|')})(?![\\w'’-])`, 'gi');
let wordyRe: RegExp | null = null;
let redundantRe: RegExp | null = null;
const lookup = (list: [string, string][], m: string) => list.find(([p]) => p === m.toLowerCase())?.[1];

export interface RuleOptions {
  dialect: Dialect;
  styleRules: StyleRule[];
  mechanics: boolean;
}

export function runLocalRules(text: string, opts: RuleOptions): Issue[] {
  const issues: Issue[] = [];
  const push = (offset: number, original: string, replacement: string | null, type: string, title: string, explanation: string, category: Issue['category'] = 'correctness', source: Issue['source'] = 'rule') => {
    issues.push({ offset, length: original.length, original, replacements: replacement === null ? [] : [replacement], category, type, title, explanation, source });
  };

  if (opts.mechanics) {
    // Everyday misspellings and apostrophe-less contractions. Runs first so it wins over the capitalisation rules
    // below for the same word ("dont" at the start of a sentence becomes one fix: "Don't").
    const apostrophe = text.includes('’') ? '’' : "'";
    for (const m of text.matchAll(/(?<![\w'’\-./@#\\])[A-Za-z]{2,}(?![\w'’\-@/\\]|\.[A-Za-z])/g)) {
      const fix = knownMistake(m[0]);
      if (!fix) continue;
      const atStart = sentenceStart(text, m.index!);
      if (/^[A-Z][a-z]/.test(m[0]) && !atStart) continue; // a capitalised word mid-sentence is probably a name
      let right = matchCase(m[0], fix.right.replace(/'/g, apostrophe));
      if (atStart && /^[a-z]/.test(right)) right = right[0].toUpperCase() + right.slice(1);
      if (fix.kind === 'spelling') push(m.index!, m[0], right, 'spelling', 'Correct your spelling', 'This word is misspelled.');
      else push(m.index!, m[0], right, 'punctuation', 'Add the apostrophe', `“${right}” needs an apostrophe.`);
    }
    for (const m of text.matchAll(/\b([A-Za-z]+)(\s+)(\1)\b/gi)) {
      const w = m[1].toLowerCase();
      if (w === 'had' || w === 'that' || (w.length === 1 && w !== 'a' && w !== 'i')) continue;
      push(m.index!, m[0], m[1], 'repeated_word', 'Remove the repeated word', `“${m[1]}” appears twice in a row.`);
    }
    for (const m of text.matchAll(/(?<=\S) {2,}(?=\S)/g)) {
      push(m.index!, m[0], ' ', 'spacing', 'Remove the extra space', 'Use a single space between words.');
    }
    for (const m of text.matchAll(/([A-Za-z0-9)"”’])( +)([,;:!?]|\.(?!\.))(?=\s|$)/g)) {
      const off = m.index! + m[1].length;
      push(off, m[2] + m[3], m[3], 'spacing', 'Remove the space before punctuation', 'Punctuation sits directly after the word.');
    }
    for (const m of text.matchAll(/([a-z])([,;])([A-Za-z])/g)) {
      push(m.index! + 1, m[2], `${m[2]} `, 'punctuation', 'Add a space after the punctuation', 'Put a space after commas and semicolons.');
    }
    for (const m of text.matchAll(/(?<![\w'’.\-@#/\\])i(?![\w'’.\-@#/\\])/g)) {
      push(m.index!, 'i', 'I', 'capitalization', 'Capitalise “I”', 'The pronoun “I” is always a capital letter.');
    }
    const first = /^(\s*)([a-z][a-z]*)(?=[\s,;:!?]|$)/.exec(text);
    if (first && first[2] !== 'i' && first[2].length > 1 && !knownMistake(first[2])) {
      push(first[1].length, first[2], first[2][0].toUpperCase() + first[2].slice(1), 'capitalization', 'Capitalise the first word', 'Start a sentence with a capital letter.');
    }
    for (const m of text.matchAll(/([.!?]["”’)]?\s+)([a-z][a-z]*)(?=[\s,;:!?]|$)/g)) {
      const before = text.slice(0, m.index! + m[1].length);
      if (ABBREV_BEFORE.test(before) || /\.\.\.\s*$|…\s*$/.test(before) || /\b[a-z]\.\s*$/i.test(before.slice(-4))) continue;
      if (m[2] === 'i' || knownMistake(m[2])) continue;
      push(m.index! + m[1].length, m[2], m[2][0].toUpperCase() + m[2].slice(1), 'capitalization', 'Capitalise the first word', 'Start a sentence with a capital letter.');
    }
    wordyRe ??= phraseRe(WORDY);
    redundantRe ??= phraseRe(REDUNDANT);
    for (const m of text.matchAll(wordyRe)) {
      const rep = lookup(WORDY, m[0]);
      if (rep) push(m.index!, m[0], matchCase(m[0], rep), 'wordiness', 'Remove wordiness', `“${matchCase(m[0], rep)}” says the same thing in fewer words.`, 'clarity');
    }
    for (const m of text.matchAll(redundantRe)) {
      const rep = lookup(REDUNDANT, m[0]);
      if (rep) push(m.index!, m[0], matchCase(m[0], rep), 'redundancy', 'Remove the redundancy', `“${m[0]}” repeats itself; “${rep}” is enough.`, 'clarity');
    }
  }

  const map = dialectMap(opts.dialect);
  for (const m of text.matchAll(/[A-Za-z]+/g)) {
    const right = map.get(m[0].toLowerCase());
    if (!right) continue;
    push(m.index!, m[0], matchCase(m[0], right), 'dialect', 'Match your English variant',
      `In ${DIALECT_NAME[opts.dialect]}, “${matchCase(m[0], right)}” is the usual spelling.`);
  }

  for (const rule of opts.styleRules) {
    const find = rule.find.trim();
    if (!find) continue;
    const re = new RegExp(`(?<![\\w])${escapeRe(find)}(?![\\w])`, rule.caseSensitive ? 'g' : 'gi');
    for (const m of text.matchAll(re)) {
      if (rule.replace && m[0] === rule.replace) continue;
      const replacement = rule.replace ? (rule.caseSensitive ? rule.replace : matchCase(m[0], rule.replace)) : null;
      push(m.index!, m[0], replacement, 'style_guide', 'Follow your style guide',
        rule.note || (rule.replace ? `Your style guide prefers “${rule.replace}”.` : `Your style guide says to avoid “${find}”.`), 'delivery', 'style');
    }
  }

  return issues.sort((a, b) => a.offset - b.offset);
}

/** Remove issues that overlap an earlier (higher-priority) one. Input order = priority. */
export function dedupeIssues(issues: Issue[]): Issue[] {
  const kept: Issue[] = [];
  for (const issue of issues) {
    const end = issue.offset + issue.length;
    const clash = kept.some((k) => (issue.offset < k.offset + k.length && k.offset < end) || (issue.length === 0 && k.offset === issue.offset));
    if (!clash) kept.push(issue);
  }
  return kept.sort((a, b) => a.offset - b.offset);
}
