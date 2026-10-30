// Applying the changes proposed by the "Tune suggestions" chat. Pure, so the server and the browser agree and it is testable.
import type { Settings, StyleRule, TuneChange } from './types';

export const MAX_INSTRUCTIONS = 1500;

export interface TuneApplied {
  patch: Partial<Settings>;
  /** Changes that took effect. */
  applied: number;
  /** Changes skipped because they were already in place or no longer fit. */
  skipped: number;
}

/** Turns confirmed changes into a settings patch (only the fields that changed). `newId` makes ids for new style rules. */
export function applyTuneChanges(s: Settings, changes: TuneChange[], newId: () => string): TuneApplied {
  let instructions = s.customInstructions;
  let muted = [...s.mutedTypes];
  const dictionary = [...s.dictionary];
  const styleRules: StyleRule[] = [...s.styleRules];
  let applied = 0;
  let skipped = 0;
  const touched = new Set<keyof Settings>();

  for (const c of changes) {
    switch (c.kind) {
      case 'instruction': {
        const line = `- ${c.text.trim().replace(/\s+/g, ' ')}`;
        const next = instructions.trim() ? `${instructions.replace(/\s+$/, '')}\n${line}` : line;
        if (!c.text.trim() || instructions.split('\n').some((l) => l.trim().toLowerCase() === line.toLowerCase()) || next.length > MAX_INSTRUCTIONS) {
          skipped++;
          break;
        }
        instructions = next;
        touched.add('customInstructions');
        applied++;
        break;
      }
      case 'mute':
        if (muted.includes(c.type)) skipped++;
        else {
          muted.push(c.type);
          touched.add('mutedTypes');
          applied++;
        }
        break;
      case 'unmute':
        if (!muted.includes(c.type)) skipped++;
        else {
          muted = muted.filter((t) => t !== c.type);
          touched.add('mutedTypes');
          applied++;
        }
        break;
      case 'dictionary': {
        const w = c.word.trim();
        if (!w || dictionary.some((d) => d.toLowerCase() === w.toLowerCase())) skipped++;
        else {
          dictionary.push(w);
          touched.add('dictionary');
          applied++;
        }
        break;
      }
      case 'style_rule': {
        const find = c.find.trim();
        if (!find || styleRules.some((r) => r.find.toLowerCase() === find.toLowerCase()) || styleRules.length >= 500) skipped++;
        else {
          styleRules.push({ id: newId(), find, replace: c.replace.trim(), ...(c.note?.trim() ? { note: c.note.trim() } : {}) });
          touched.add('styleRules');
          applied++;
        }
        break;
      }
    }
  }

  const patch: Partial<Settings> = {};
  if (touched.has('customInstructions')) patch.customInstructions = instructions;
  if (touched.has('mutedTypes')) patch.mutedTypes = muted;
  if (touched.has('dictionary')) patch.dictionary = dictionary.sort((a, b) => a.localeCompare(b));
  if (touched.has('styleRules')) patch.styleRules = styleRules;
  return { patch, applied, skipped };
}
