// Server-side merging for clients that don't run the rule engine themselves (the desktop companion).
// Mirrors what the web editor's checker does in the browser: AI findings first, then local rules,
// overlaps removed, disabled categories and personal-dictionary words filtered out.
import type { Issue, Settings } from '../shared/types';
import { CATEGORIES } from '../shared/types';
import { dedupeIssues, runLocalRules } from '../shared/rules';
import { alreadyApplied, hashText } from '../shared/text';

/** Fingerprint of every setting that changes which suggestions a paragraph gets (dictionary excluded: it only filters). */
export function assistFingerprint(s: Settings): string {
  return hashText(JSON.stringify([s.dialect, s.defaultGoals, CATEGORIES.filter((c) => s.categories[c]), s.localRules, s.styleRules, s.models.check]));
}

/** AI findings plus local rules for one paragraph, filtered the same way the web editor filters them. */
export function mergeAssistIssues(text: string, ai: Issue[], s: Settings): Issue[] {
  const rules = runLocalRules(text, { dialect: s.dialect, styleRules: s.styleRules, mechanics: s.localRules });
  const dict = new Set(s.dictionary.map((w) => w.toLowerCase()));
  return dedupeIssues([...ai, ...rules])
    .filter((i) => s.categories[i.category])
    .filter((i) => !((i.type === 'spelling' || i.type === 'dialect') && dict.has(i.original.toLowerCase().trim())))
    .filter((i) => text.slice(i.offset, i.offset + i.length) === i.original)
    .filter((i) => !i.replacements.some((r) => alreadyApplied(text, i.offset, i.original, r)))
    .sort((a, b) => a.offset - b.offset || a.length - b.length);
}
