// Remembers the AI check's findings per paragraph, so the same paragraph checked with the same settings never costs
// a second NVIDIA call: re-attaching the desktop app, restarting it, or the web editor and the desktop app both
// checking the same text. Only the model's findings are kept; local rules and the dictionary filter still run on
// every request. Held in memory only (nothing written to disk), and forgotten when the server stops.
import { createHash } from 'node:crypto';
import type { CheckOptions, CheckParagraph, CheckResponse, Issue } from '../shared/types';
import { Lru } from './lru';

type Entry = { issues: Issue[]; model: string };

export const checkCache = new Lru<Entry>(2000);

/** Everything that changes what the model is asked, plus the paragraph itself. Lists that are really sets are sorted. */
export function checkKey(model: string, o: CheckOptions, text: string): string {
  const shape = JSON.stringify([
    model,
    o.dialect,
    o.goals.audience,
    o.goals.formality,
    o.goals.domain,
    [...o.goals.intent].sort(),
    [...o.dictionary].sort(),
    [...o.categories].sort(),
    o.instructions ?? '',
  ]);
  return createHash('sha256').update(shape).update('\u0000').update(text).digest('base64url');
}

/**
 * Answers cached paragraphs from memory and sends only the rest to `run` (the model), then remembers what it found.
 * If `run` fails nothing is cached, so the next request tries again. Results are copies: callers may change them.
 */
export async function withCheckCache(
  paragraphs: CheckParagraph[],
  keyOf: (p: CheckParagraph) => string,
  run: (misses: CheckParagraph[]) => Promise<CheckResponse>,
  cache: Lru<Entry> = checkCache,
): Promise<CheckResponse & { cached: number }> {
  const found = new Map<string, Issue[]>();
  const misses: CheckParagraph[] = [];
  let cachedModel = '';
  for (const p of paragraphs) {
    const hit = cache.get(keyOf(p));
    if (hit) {
      found.set(p.id, structuredClone(hit.issues));
      cachedModel ||= hit.model;
    } else misses.push(p);
  }
  let model = cachedModel;
  let ms = 0;
  if (misses.length) {
    const r = await run(misses);
    model = r.model;
    ms = r.ms;
    const byId = new Map(misses.map((p) => [p.id, p]));
    for (const res of r.results) {
      const p = byId.get(res.id);
      if (!p) continue;
      cache.set(keyOf(p), { issues: structuredClone(res.issues), model: r.model });
      found.set(res.id, res.issues);
    }
  }
  return {
    results: paragraphs.map((p) => ({ id: p.id, issues: found.get(p.id) ?? [] })),
    model,
    ms,
    cached: paragraphs.length - misses.length,
  };
}
