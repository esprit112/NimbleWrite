// Fetching web pages (for citations and plagiarism sources) and optional Brave web search.
import * as cheerio from 'cheerio';
import { config } from './config';

export interface FetchedPage {
  url: string;
  html: string;
  title: string;
  text: string;
}

const MAX_BYTES = 3 * 1024 * 1024;

export function isHttpUrl(s: string): boolean {
  try {
    const u = new URL(s);
    return u.protocol === 'http:' || u.protocol === 'https:';
  } catch {
    return false;
  }
}

export async function fetchPage(url: string, timeoutMs = 12_000): Promise<FetchedPage> {
  if (!isHttpUrl(url)) throw new Error('Only http(s) links can be fetched');
  const res = await fetch(url, {
    signal: AbortSignal.timeout(timeoutMs),
    redirect: 'follow',
    headers: {
      'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36 NimbleWrite/1.0',
      Accept: 'text/html,application/xhtml+xml;q=0.9,*/*;q=0.5',
      'Accept-Language': 'en-GB,en;q=0.8',
    },
  });
  if (!res.ok) throw new Error(`The page returned HTTP ${res.status}`);
  const reader = res.body?.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  if (reader) {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_BYTES) break;
      chunks.push(value);
    }
  }
  const html = Buffer.concat(chunks).toString('utf8');
  const $ = cheerio.load(html);
  const title = $('title').first().text().trim();
  $('script, style, noscript, svg, nav, footer, header, aside, form, iframe').remove();
  const text = $('body').text().replace(/\s+/g, ' ').trim();
  return { url: res.url || url, html, title, text };
}

export interface SearchHit {
  title: string;
  url: string;
  snippet: string;
}

export async function webSearch(query: string, count = 5): Promise<SearchHit[]> {
  if (!config.braveKey) throw new Error('Web search is not configured (set BRAVE_API_KEY in .env)');
  const u = new URL('https://api.search.brave.com/res/v1/web/search');
  u.searchParams.set('q', query);
  u.searchParams.set('count', String(count));
  const res = await fetch(u, {
    signal: AbortSignal.timeout(12_000),
    headers: { Accept: 'application/json', 'X-Subscription-Token': config.braveKey },
  });
  if (!res.ok) throw new Error(`Brave search failed (${res.status})`);
  const data = (await res.json()) as { web?: { results?: { title: string; url: string; description?: string }[] } };
  return (data.web?.results ?? []).map((r) => ({
    title: r.title,
    url: r.url,
    snippet: (r.description ?? '').replace(/<[^>]+>/g, ''),
  }));
}
