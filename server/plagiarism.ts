// Originality checking by word-shingle overlap against your other documents, pasted sources,
// fetched URLs and (optionally) web search results.
import type { PlagiarismResult, PlagiarismSource } from '../shared/types';
import { sentences, words } from '../shared/text';
import { allLiveDocs } from './store';
import { config } from './config';
import { fetchPage, isHttpUrl, webSearch } from './web';

const K = 6;

interface Token {
  w: string;
  start: number;
  end: number;
}

function tokenize(text: string): Token[] {
  const out: Token[] = [];
  for (const m of text.matchAll(/[\p{L}\p{N}]+(?:['’][\p{L}]+)?/gu)) {
    out.push({ w: m[0].toLowerCase().replace(/’/g, "'"), start: m.index!, end: m.index! + m[0].length });
  }
  return out;
}

function shingleSet(text: string): Set<string> {
  const t = tokenize(text);
  const s = new Set<string>();
  for (let i = 0; i + K <= t.length; i++) s.add(t.slice(i, i + K).map((x) => x.w).join(' '));
  return s;
}

/** Character ranges of `text` covered by shingles that also occur in the source. */
function matchedRanges(tokens: Token[], source: Set<string>): Array<[number, number]> {
  const ranges: Array<[number, number]> = [];
  for (let i = 0; i + K <= tokens.length; i++) {
    const key = tokens.slice(i, i + K).map((x) => x.w).join(' ');
    if (!source.has(key)) continue;
    const start = tokens[i].start;
    const end = tokens[i + K - 1].end;
    const last = ranges[ranges.length - 1];
    if (last && start <= last[1] + 1) last[1] = Math.max(last[1], end);
    else ranges.push([start, end]);
  }
  return ranges;
}

function coverage(tokens: Token[], ranges: Array<[number, number]>): number {
  if (!tokens.length) return 0;
  const covered = tokens.filter((t) => ranges.some(([a, b]) => t.start >= a && t.end <= b)).length;
  return covered / tokens.length;
}

function distinctiveSentences(text: string, n: number): string[] {
  return sentences(text)
    .map((s) => ({ s, score: words(s).filter((w) => w.length > 6).length + Math.min(words(s).length, 30) / 3 }))
    .filter((x) => words(x.s).length >= 8)
    .sort((a, b) => b.score - a.score)
    .slice(0, n)
    .map((x) => x.s.split(/\s+/).slice(0, 14).join(' '));
}

export interface PlagiarismInput {
  text: string;
  docId?: string;
  includeDocs: boolean;
  sources: { title?: string; text?: string; url?: string }[];
  web: boolean;
}

export async function checkPlagiarism(input: PlagiarismInput): Promise<PlagiarismResult> {
  const tokens = tokenize(input.text);
  if (tokens.length < K) {
    return { overall: 0, sources: [], webSearch: 'skipped', checkedSources: 0, note: 'Write at least a couple of sentences to check originality.' };
  }

  const candidates: { title: string; url?: string; kind: PlagiarismSource['kind']; docId?: string; text: string }[] = [];
  if (input.includeDocs) {
    for (const d of allLiveDocs()) {
      if (d.id === input.docId || !d.text.trim()) continue;
      candidates.push({ title: d.title, kind: 'doc', docId: d.id, text: d.text });
    }
  }

  const errors: string[] = [];
  for (const s of input.sources.slice(0, 10)) {
    if (s.text?.trim()) candidates.push({ title: s.title || 'Pasted source', kind: 'pasted', text: s.text });
    else if (s.url && isHttpUrl(s.url)) {
      try {
        const page = await fetchPage(s.url);
        candidates.push({ title: s.title || page.title || s.url, url: page.url, kind: 'pasted', text: page.text });
      } catch (e) {
        errors.push(`${s.url}: ${(e as Error).message}`);
      }
    }
  }

  let webSearchState: PlagiarismResult['webSearch'] = 'skipped';
  if (input.web) {
    if (!config.braveKey) webSearchState = 'not_configured';
    else {
      try {
        const seen = new Set<string>();
        for (const q of distinctiveSentences(input.text, 3)) {
          for (const hit of await webSearch(`"${q}"`, 4)) {
            if (seen.has(hit.url) || seen.size >= 6) continue;
            seen.add(hit.url);
            try {
              const page = await fetchPage(hit.url, 8000);
              candidates.push({ title: hit.title || page.title, url: page.url, kind: 'web', text: page.text });
            } catch {
              candidates.push({ title: hit.title, url: hit.url, kind: 'web', text: hit.snippet });
            }
          }
        }
        webSearchState = 'used';
      } catch (e) {
        webSearchState = 'failed';
        errors.push((e as Error).message);
      }
    }
  }

  const results: PlagiarismSource[] = [];
  const allRanges: Array<[number, number]> = [];
  for (const c of candidates) {
    const ranges = matchedRanges(tokens, shingleSet(c.text));
    if (!ranges.length) continue;
    allRanges.push(...ranges);
    results.push({
      title: c.title,
      url: c.url,
      kind: c.kind,
      docId: c.docId,
      similarity: Math.round(coverage(tokens, ranges) * 100),
      passages: ranges.slice(0, 20).map(([a, b]) => ({ offset: a, length: b - a, text: input.text.slice(a, b) })),
    });
  }
  results.sort((a, b) => b.similarity - a.similarity);
  return {
    overall: Math.round(coverage(tokens, allRanges) * 100),
    sources: results.slice(0, 15),
    webSearch: webSearchState,
    checkedSources: candidates.length,
    note: errors.length ? `Some sources could not be checked: ${errors.slice(0, 3).join('; ')}` : undefined,
  };
}
