// Statistical signals for the AI-text detector. Combined with a model's judgement in the route.
// Word lists follow Wikipedia's "Signs of AI writing" (via the MIT-licensed humanizer guide).
import { sentences, words } from '../shared/text';

const AI_WORDS = [
  'delve', 'delves', 'delving', 'tapestry', 'testament', 'pivotal', 'crucial', 'underscore', 'underscores', 'underscoring', 'showcase',
  'showcases', 'showcasing', 'vibrant', 'intricate', 'intricacies', 'interplay', 'landscape', 'foster', 'fosters', 'fostering', 'garner',
  'enhance', 'enhances', 'enhancing', 'seamless', 'seamlessly', 'realm', 'multifaceted', 'nuanced', 'holistic', 'paramount', 'leverage',
  'leveraging', 'robust', 'streamline', 'unwavering', 'meticulous', 'meticulously', 'noteworthy', 'commendable', 'elevate', 'embark',
  'navigate', 'navigating', 'resonate', 'resonates', 'boasts', 'nestled', 'transformative', 'groundbreaking', 'invaluable', 'endeavor', 'endeavour',
];
const AI_PHRASES = [
  /it is (?:important|worth|crucial) to note/i, /in today's (?:fast-paced|digital|ever-changing)/i, /plays? a (?:crucial|pivotal|vital|key) role/i,
  /not only\b[^.]{0,80}\bbut also/i, /in conclusion/i, /serves as a/i, /stands as a/i, /a testament to/i, /ever-evolving/i,
  /let's (?:dive|explore|delve)/i, /when it comes to/i, /at the end of the day/i, /whether you're/i, /in the realm of/i,
];
const AI_OPENERS = /^(?:moreover|furthermore|additionally|in conclusion|overall|ultimately|notably|importantly|consequently|thus|hence|in summary|in essence)\b/i;
const HUMAN_SLIPS = new RegExp(
  '\\b(?:teh|recieve[ds]?|seperate[ds]?|alot|definately|occured|untill|wich|becuase|tomm?orow|libary|succesful|successfull|goverment|enviroment|accomodate|wierd|beleive[ds]?|arguement|calender|existance|independant|neccessary|necesary|occassion|publically|reccomend|recomend|truely|thier|embarass|irregardless'
  + '|(?:should|could|would|must) of|we has|they was|you was|he don\'t|she don\'t|it don\'t|me and him|him and me|less people|could care less)\\b',
  'gi',
);

export interface Signals {
  score: number;
  reasons: string[];
  /** Spelling and grammar slips found. Several of them are strong evidence of a human writer. */
  slips: number;
}

export function heuristicSignals(text: string): Signals {
  const ws = words(text);
  const sents = sentences(text);
  const reasons: string[] = [];
  // Typos and grammar slips are strong human signals: AI output is almost always mechanically clean.
  const slips = (text.match(HUMAN_SLIPS) ?? []).length + (text.match(/\b(\w+)\s+\1\b/gi) ?? []).filter((m) => !/^(had|that)\s/i.test(m)).length;
  if (ws.length < 25) return { score: 50, reasons: ['Too short to judge reliably'], slips };

  const lengths = sents.map((s) => words(s).length);
  const mean = lengths.reduce((a, b) => a + b, 0) / Math.max(1, lengths.length);
  const sd = Math.sqrt(lengths.reduce((a, b) => a + (b - mean) ** 2, 0) / Math.max(1, lengths.length));
  const cv = mean ? sd / mean : 0;
  let burst = 50;
  if (lengths.length >= 3) {
    burst = cv < 0.25 ? 85 : cv < 0.4 ? 65 : cv < 0.6 ? 40 : 15;
    if (cv < 0.4) reasons.push('Very even sentence lengths');
    else if (cv >= 0.6) reasons.push('Varied sentence rhythm (human-like)');
  }

  const lower = ws.map((w) => w.toLowerCase());
  const vocabHits = lower.filter((w) => AI_WORDS.includes(w));
  const phraseHits = AI_PHRASES.filter((re) => re.test(text)).length;
  const per100 = ((vocabHits.length + phraseHits * 1.5) / ws.length) * 100;
  const vocab = per100 === 0 ? 20 : per100 < 1 ? 55 : per100 < 2.5 ? 75 : 92;
  if (vocabHits.length) reasons.push(`AI-typical words: ${[...new Set(vocabHits)].slice(0, 4).join(', ')}`);
  if (phraseHits) reasons.push('Formulaic phrases ("plays a crucial role", "it is important to note"…)');

  const openers = sents.filter((s) => AI_OPENERS.test(s.trim())).length;
  const openerScore = sents.length ? Math.min(100, (openers / sents.length) * 300) : 0;
  if (openers >= 2) reasons.push('Stock transitions (Moreover, Furthermore, Additionally)');

  const dashes = (text.match(/—/g) ?? []).length;
  const dashScore = Math.min(100, (dashes / ws.length) * 100 * 40);
  if (dashes >= 3) reasons.push('Frequent em dashes');

  const personal = /\b(I|my|me|we|our)\b/.test(text) ? -8 : 0;
  const typos = slips === 0 ? 0 : slips < 3 ? -15 : -30;
  if (slips) reasons.push(`Spelling or grammar slips (${slips}), typical of human writing`);

  const score = Math.max(0, Math.min(100, Math.round(0.42 * burst + 0.38 * vocab + 0.12 * openerScore + 0.08 * dashScore + personal + typos)));
  return { score, reasons, slips };
}
