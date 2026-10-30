// "Tune suggestions": the writer says in plain language how they want suggestions to change, and the model proposes
// settings changes. Nothing is applied here; the browser shows the list and the writer confirms it.
import type { Settings, TuneChange, TuneResult } from '../shared/types';
import { ISSUE_TYPES, categoryForType, normaliseType, titleForType } from '../shared/meta';

const typeTitle = (t: string) => titleForType(t, categoryForType(t));

export function tuneSystemPrompt(s: Pick<Settings, 'customInstructions' | 'mutedTypes'>): string {
  const types = ISSUE_TYPES.map((t) => `${t} (${typeTitle(t)})`).join('; ');
  return `You configure a writing assistant for its user. The user says, in plain language, how they want their writing suggestions to change. Turn that into settings changes.
Respond with ONLY a JSON object, no markdown:
{"reply":"<one or two friendly sentences: what you will change, or the answer to their question>","instructions":["<standing preference, imperative, at most 120 characters>"],"mute":["<issue type code>"],"unmute":["<issue type code>"],"dictionary":["<word that must never be flagged>"],"styleRules":[{"find":"<exact text to look for>","replace":"<replacement, or empty to just flag it>","note":"<why, optional>"}]}

How to choose:
- "mute": ONLY when the user wants a whole kind of suggestion gone. Use these codes: ${types}.
- "unmute": when they want a muted kind back. Currently muted: ${s.mutedTypes.length ? s.mutedTypes.join(', ') : 'nothing'}.
- "instructions": softer, conditional or stylistic preferences (e.g. "Allow sentence fragments in dialogue", "Keep my sentences long"). Existing preferences: ${s.customInstructions.trim() ? s.customInstructions.replace(/\s+/g, ' ').trim().slice(0, 600) : 'none'}. Do not repeat them.
- "dictionary": names, brands, jargon. "styleRules": exact term swaps (e.g. utilise -> use).
- Use the fewest changes that do the job. Never add an instruction that merely repeats a mute, a dictionary word or a style rule.
- Example: "stop flagging passive voice" -> {"reply":"Done: I'll stop suggesting active-voice changes.","instructions":[],"mute":["passive_voice"],"unmute":[],"dictionary":[],"styleRules":[]}
- Leave every list you do not need as []. If the request is not about suggestion settings (for example, it asks you to rewrite text), say so in "reply" and change nothing.`;
}

const strings = (v: unknown, max: number, maxLen: number): string[] =>
  (Array.isArray(v) ? v : [])
    .filter((x): x is string => typeof x === 'string')
    .map((x) => x.replace(/\s+/g, ' ').trim().slice(0, maxLen))
    .filter(Boolean)
    .slice(0, max);

/** Validates the model's answer into a reply plus change list. Returns null when it is not usable JSON of that shape. */
export function parseTuneResult(v: unknown, current: Pick<Settings, 'mutedTypes'>): TuneResult | null {
  const o = v as Record<string, unknown> | null;
  if (!o || typeof o !== 'object' || typeof o.reply !== 'string') return null;
  const changes: TuneChange[] = [];
  const known = new Set(ISSUE_TYPES);
  const types = (list: unknown) => [...new Set(strings(list, 12, 40).map(normaliseType).filter((t) => known.has(t)))];

  const mutes = types(o.mute).filter((t) => !current.mutedTypes.includes(t)).slice(0, 6);
  // Models like to add "Keep passive voice off" next to the passive_voice mute. Muting already does that job.
  const repeatsMute = (text: string) => mutes.some((t) => t.split('_').every((w) => new RegExp(`\\b${w}\\b`, 'i').test(text)));
  for (const text of strings(o.instructions, 3, 200).filter((x) => !repeatsMute(x))) changes.push({ kind: 'instruction', text });
  for (const type of mutes) changes.push({ kind: 'mute', type, title: typeTitle(type) });
  for (const type of types(o.unmute).filter((t) => current.mutedTypes.includes(t)).slice(0, 6)) changes.push({ kind: 'unmute', type, title: typeTitle(type) });
  for (const word of strings(o.dictionary, 10, 80)) changes.push({ kind: 'dictionary', word });
  for (const r of (Array.isArray(o.styleRules) ? o.styleRules : []).slice(0, 5) as Record<string, unknown>[]) {
    const find = typeof r?.find === 'string' ? r.find.trim().slice(0, 200) : '';
    if (!find) continue;
    const replace = typeof r.replace === 'string' ? r.replace.trim().slice(0, 200) : '';
    const note = typeof r.note === 'string' ? r.note.trim().slice(0, 300) : '';
    changes.push({ kind: 'style_rule', find, replace, ...(note ? { note } : {}) });
  }
  return { reply: o.reply.trim().slice(0, 500), changes };
}
