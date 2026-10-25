// Citation metadata (from a URL or DOI) and formatting in APA 7, MLA 9, Chicago 17 and Harvard (Cite Them Right).
import * as cheerio from 'cheerio';
import type { CitationAuthor, CitationMeta, CitationStyle, FormattedCitation } from '../shared/types';
import { fetchPage } from './web';

const ORG_HINT = /\b(inc|ltd|llc|plc|university|association|organisation|organization|institute|society|council|agency|department|ministry|news|team|staff|editors?|press|bbc|nhs|who|un|government|office|foundation|trust|group|committee)\b/i;

export function parseAuthor(raw: string): CitationAuthor | null {
  const n = raw.replace(/\s+/g, ' ').replace(/^by\s+/i, '').trim();
  if (!n || /^https?:\/\//i.test(n)) return null;
  if (n.includes(',') && n.split(',').length === 2) {
    const [family, given] = n.split(',').map((s) => s.trim());
    return given ? { family, given } : { literal: family };
  }
  if (ORG_HINT.test(n) || n.split(' ').length > 4) return { literal: n };
  const parts = n.split(' ');
  if (parts.length === 1) return { literal: n };
  const family = parts.pop() as string;
  return { given: parts.join(' '), family };
}

function initials(given = ''): string {
  return given
    .split(/\s+/)
    .filter(Boolean)
    .map((g) => g.split('-').map((p) => (p ? `${p[0].toUpperCase()}.` : '')).join('-'))
    .join(' ');
}

interface DateParts {
  year?: number;
  month?: number;
  day?: number;
}

export function parseDate(s?: string): DateParts {
  if (!s) return {};
  const m = /^(\d{4})(?:-(\d{1,2})(?:-(\d{1,2}))?)?/.exec(s.trim());
  if (m) return { year: +m[1], month: m[2] ? +m[2] : undefined, day: m[3] ? +m[3] : undefined };
  const d = new Date(s);
  if (!Number.isNaN(d.getTime())) return { year: d.getUTCFullYear(), month: d.getUTCMonth() + 1, day: d.getUTCDate() };
  return {};
}

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const MLA_MONTHS = ['Jan.', 'Feb.', 'Mar.', 'Apr.', 'May', 'June', 'July', 'Aug.', 'Sept.', 'Oct.', 'Nov.', 'Dec.'];

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const endDot = (s: string) => (/[.?!]$/.test(s.trim()) ? s.trim() : `${s.trim()}.`);

/** A citation is built from segments so the same logic yields plain text and HTML (italics). */
type Seg = string | { i: string };
function render(segs: Seg[]): { text: string; html: string } {
  const text = segs.map((s) => (typeof s === 'string' ? s : s.i)).join('').replace(/\s+/g, ' ').replace(/\s+([.,])/g, '$1').trim();
  const html = segs.map((s) => (typeof s === 'string' ? esc(s) : `<i>${esc(s.i)}</i>`)).join('').replace(/\s+/g, ' ').replace(/\s+([.,])/g, '$1').trim();
  return { text, html };
}

function nameApa(a: CitationAuthor): string {
  return a.literal ?? `${a.family}, ${initials(a.given)}`.trim();
}
function nameHarvard(a: CitationAuthor): string {
  return a.literal ?? `${a.family}, ${initials(a.given)}`.trim();
}
function nameFamilyFirst(a: CitationAuthor): string {
  return a.literal ?? `${a.family}, ${a.given ?? ''}`.trim().replace(/,$/, '');
}
function nameGivenFirst(a: CitationAuthor): string {
  return a.literal ?? `${a.given ?? ''} ${a.family ?? ''}`.trim();
}

function apaAuthors(list: CitationAuthor[]): string {
  const n = list.map(nameApa);
  if (n.length === 1) return n[0];
  if (n.length === 2) return `${n[0]}, & ${n[1]}`;
  if (n.length <= 20) return `${n.slice(0, -1).join(', ')}, & ${n[n.length - 1]}`;
  return `${n.slice(0, 19).join(', ')}, . . . ${n[n.length - 1]}`;
}
function mlaAuthors(list: CitationAuthor[]): string {
  if (list.length === 1) return nameFamilyFirst(list[0]);
  if (list.length === 2) return `${nameFamilyFirst(list[0])}, and ${nameGivenFirst(list[1])}`;
  return `${nameFamilyFirst(list[0])}, et al`;
}
function chicagoAuthors(list: CitationAuthor[]): string {
  if (list.length === 1) return nameFamilyFirst(list[0]);
  const rest = list.slice(1, 10).map(nameGivenFirst);
  const all = [nameFamilyFirst(list[0]), ...rest];
  const joined = all.length === 2 ? `${all[0]}, and ${all[1]}` : `${all.slice(0, -1).join(', ')}, and ${all[all.length - 1]}`;
  return list.length > 10 ? `${all.slice(0, 7).join(', ')}, et al` : joined;
}
function harvardAuthors(list: CitationAuthor[]): string {
  const n = list.map(nameHarvard);
  if (n.length === 1) return n[0];
  if (n.length <= 3) return `${n.slice(0, -1).join(', ')} and ${n[n.length - 1]}`;
  return `${n[0]} et al.`;
}

const stripProtocol = (u: string) => u.replace(/^https?:\/\//, '').replace(/\/$/, '');
const doiUrl = (doi: string) => `https://doi.org/${doi}`;

export function formatCitation(meta: CitationMeta, style: CitationStyle, today = new Date()): FormattedCitation {
  // Page ranges take an en dash in APA, MLA, Chicago and Harvard (Crossref data often has a hyphen).
  const m: CitationMeta = { ...meta, pages: meta.pages?.replace(/(\d)\s*[-‐‑‒—]\s*(\d)/g, '$1–$2') };
  const d = parseDate(m.published);
  const acc = parseDate(m.accessed) ?? {};
  const accessed = acc.year ? acc : { year: today.getFullYear(), month: today.getMonth() + 1, day: today.getDate() };
  const hasAuthors = m.authors.length > 0;
  const link = m.doi ? doiUrl(m.doi) : m.url ?? '';
  const container = m.containerTitle || '';
  const site = m.siteName || '';
  let segs: Seg[] = [];

  if (style === 'apa') {
    const date = d.year ? (m.type === 'webpage' && d.month ? `(${d.year}, ${MONTHS[d.month - 1]}${d.day ? ` ${d.day}` : ''})` : `(${d.year})`) : '(n.d.)';
    const who = hasAuthors ? endDot(apaAuthors(m.authors)) : '';
    if (m.type === 'article') {
      const vol = m.volume ? [', ', { i: m.volume }, m.issue ? `(${m.issue})` : ''] : [];
      segs = [who, ' ', date, '. ', endDot(m.title), ' ', { i: container }, ...vol, m.pages ? `, ${m.pages}` : '', '. ', link];
      if (!hasAuthors) segs = [endDot(m.title), ' ', date, '. ', { i: container }, ...vol, m.pages ? `, ${m.pages}` : '', '. ', link];
    } else if (m.type === 'book' || m.type === 'report') {
      segs = hasAuthors ? [who, ' ', date, '. ', { i: m.title }, '. ', m.publisher ? endDot(m.publisher) : '', ' ', link] : [{ i: m.title }, '. ', date, '. ', m.publisher ? endDot(m.publisher) : '', ' ', link];
    } else {
      const siteSeg = site && !(m.authors.length === 1 && m.authors[0].literal === site) ? `${endDot(site)} ` : '';
      segs = hasAuthors ? [who, ' ', date, '. ', { i: m.title }, '. ', siteSeg, link] : [{ i: m.title }, '. ', date, '. ', siteSeg, link];
    }
  } else if (style === 'mla') {
    const who = hasAuthors ? `${endDot(mlaAuthors(m.authors))} ` : '';
    const date = d.year ? `${d.day ? `${d.day} ` : ''}${d.month ? `${MLA_MONTHS[d.month - 1]} ` : ''}${d.year}` : '';
    const accessedStr = `Accessed ${accessed.day} ${MLA_MONTHS[(accessed.month ?? 1) - 1]} ${accessed.year}.`;
    if (m.type === 'article') {
      segs = [who, `“${endDot(m.title)}” `, { i: container }, m.volume ? `, vol. ${m.volume}` : '', m.issue ? `, no. ${m.issue}` : '', d.year ? `, ${d.year}` : '', m.pages ? `, pp. ${m.pages}` : '', link ? `, ${m.doi ? link : stripProtocol(link)}` : '', '.'];
    } else if (m.type === 'book' || m.type === 'report') {
      segs = [who, { i: m.title }, '. ', m.publisher ? `${m.publisher}, ` : '', d.year ? `${d.year}.` : ''];
    } else {
      segs = [who, `“${endDot(m.title)}” `, site ? { i: site } : '', date ? `, ${date}` : '', link ? `, ${stripProtocol(link)}` : '', '. ', date ? '' : accessedStr];
    }
  } else if (style === 'chicago') {
    const who = hasAuthors ? `${endDot(chicagoAuthors(m.authors))} ` : '';
    const date = d.year ? `${d.month ? `${MONTHS[d.month - 1]} ` : ''}${d.day ? `${d.day}, ` : ''}${d.year}` : '';
    if (m.type === 'article') {
      segs = [who, `“${endDot(m.title)}” `, { i: container }, m.volume ? ` ${m.volume}` : '', m.issue ? `, no. ${m.issue}` : '', d.year ? ` (${d.year})` : '', m.pages ? `: ${m.pages}` : '', '. ', link ? endDot(link) : ''];
    } else if (m.type === 'book' || m.type === 'report') {
      segs = [who, { i: m.title }, '. ', m.publisher ? `${m.publisher}, ` : '', d.year ? `${d.year}.` : ''];
    } else {
      const accessedStr = `Accessed ${MONTHS[(accessed.month ?? 1) - 1]} ${accessed.day}, ${accessed.year}.`;
      segs = [who, `“${endDot(m.title)}” `, site ? `${endDot(site)} ` : '', date ? `${endDot(date)} ` : `${accessedStr} `, link ? endDot(link) : ''];
    }
  } else {
    const year = d.year ? `(${d.year})` : '(no date)';
    const author = hasAuthors ? harvardAuthors(m.authors) : site || '';
    const accessedStr = `(Accessed: ${accessed.day} ${MONTHS[(accessed.month ?? 1) - 1]} ${accessed.year})`;
    if (m.type === 'article') {
      segs = [author ? `${author} ` : '', `${year} `, `‘${m.title}’, `, { i: container }, m.volume ? `, ${m.volume}` : '', m.issue ? `(${m.issue})` : '', m.pages ? `, pp. ${m.pages}` : '', '.', link ? ` Available at: ${link}.` : ''];
    } else if (m.type === 'book' || m.type === 'report') {
      segs = [author ? `${author} ` : '', `${year} `, { i: m.title }, '. ', m.publisher ? endDot(m.publisher) : '', link ? ` Available at: ${link} ${accessedStr}.` : ''];
    } else {
      segs = author ? [`${author} `, `${year} `, { i: m.title }, '. ', link ? `Available at: ${link} ${accessedStr}.` : ''] : [{ i: m.title }, ` ${year}. `, link ? `Available at: ${link} ${accessedStr}.` : ''];
    }
  }
  return { style, ...render(segs) };
}

export function extractDoi(input: string): string | null {
  const m = /\b(10\.\d{4,9}\/[^\s"<>]+)/i.exec(decodeURIComponent(input));
  return m ? m[1].replace(/[.,;)\]]+$/, '') : null;
}

interface Csl {
  type?: string;
  title?: string | string[];
  author?: { given?: string; family?: string; literal?: string; name?: string }[];
  'container-title'?: string | string[];
  publisher?: string;
  issued?: { 'date-parts'?: number[][] };
  published?: { 'date-parts'?: number[][] };
  volume?: string | number;
  issue?: string | number;
  page?: string;
  DOI?: string;
  URL?: string;
}

const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) ?? '';

export async function metaFromDoi(doi: string): Promise<CitationMeta> {
  const res = await fetch(doiUrl(doi), {
    signal: AbortSignal.timeout(12_000),
    headers: { Accept: 'application/vnd.citationstyles.csl+json' },
    redirect: 'follow',
  });
  if (!res.ok) throw new Error(`DOI lookup failed (${res.status})`);
  const c = (await res.json()) as Csl;
  const parts = c.issued?.['date-parts']?.[0] ?? c.published?.['date-parts']?.[0];
  const type = c.type === 'book' || c.type === 'monograph' ? 'book' : c.type === 'report' ? 'report' : 'article';
  return {
    type,
    title: first(c.title).replace(/<[^>]+>/g, ''),
    authors: (c.author ?? []).map((a) => (a.literal || a.name ? { literal: a.literal ?? a.name } : { given: a.given, family: a.family })),
    containerTitle: first(c['container-title']),
    publisher: c.publisher,
    published: parts ? parts.map((p, i) => (i === 0 ? String(p) : String(p).padStart(2, '0'))).join('-') : undefined,
    volume: c.volume != null ? String(c.volume) : undefined,
    issue: c.issue != null ? String(c.issue) : undefined,
    pages: c.page,
    doi: c.DOI ?? doi,
    url: c.URL,
  };
}

export function metaFromHtml(html: string, url: string): CitationMeta {
  const $ = cheerio.load(html);
  const meta = (sel: string) => $(sel).map((_, el) => $(el).attr('content')?.trim() ?? '').get().filter(Boolean);
  let ld: Record<string, unknown> | null = null;
  $('script[type="application/ld+json"]').each((_, el) => {
    if (ld) return;
    try {
      const parsed = JSON.parse($(el).text()) as unknown;
      const items = (Array.isArray(parsed) ? parsed : [parsed]).flatMap((x) => (x && typeof x === 'object' && '@graph' in x ? (x as { '@graph': unknown[] })['@graph'] : [x]));
      const art = items.find((x) => x && typeof x === 'object' && /Article|BlogPosting|Report|WebPage/i.test(String((x as Record<string, unknown>)['@type'])));
      if (art) ld = art as Record<string, unknown>;
    } catch {
      /* ignore malformed JSON-LD */
    }
  });
  const ldAuthors = (() => {
    const a = (ld as Record<string, unknown> | null)?.author;
    const arr = Array.isArray(a) ? a : a ? [a] : [];
    return arr.map((x) => (typeof x === 'string' ? x : (x as { name?: string })?.name ?? '')).filter(Boolean);
  })();
  const host = (() => {
    try {
      return new URL(url).hostname.replace(/^www\./, '');
    } catch {
      return '';
    }
  })();
  const siteName = meta('meta[property="og:site_name"]')[0] || ((ld as Record<string, unknown> | null)?.publisher as { name?: string } | undefined)?.name || host;
  let title = meta('meta[name="citation_title"]')[0] || meta('meta[property="og:title"]')[0] || String((ld as Record<string, unknown> | null)?.headline ?? '') || meta('meta[name="twitter:title"]')[0] || $('title').first().text().trim();
  if (siteName) title = title.replace(new RegExp(`\\s*[|\\-–—:]\\s*${siteName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`, 'i'), '');
  const authorNames = meta('meta[name="citation_author"]').length ? meta('meta[name="citation_author"]') : ldAuthors.length ? ldAuthors : meta('meta[name="author"]');
  const published = meta('meta[name="citation_publication_date"]')[0] || meta('meta[name="citation_date"]')[0] || meta('meta[property="article:published_time"]')[0]
    || String((ld as Record<string, unknown> | null)?.datePublished ?? '') || meta('meta[name="date"]')[0] || meta('meta[name="dc.date"]')[0] || $('time[datetime]').first().attr('datetime') || '';
  const journal = meta('meta[name="citation_journal_title"]')[0];
  return {
    type: journal ? 'article' : 'webpage',
    title: title.replace(/\s+/g, ' ').trim() || host,
    authors: authorNames.flatMap((n) => n.split(/\s+(?:and|&)\s+/)).map(parseAuthor).filter((a): a is CitationAuthor => !!a).slice(0, 25),
    siteName,
    containerTitle: journal,
    published: published || undefined,
    volume: meta('meta[name="citation_volume"]')[0],
    issue: meta('meta[name="citation_issue"]')[0],
    pages: meta('meta[name="citation_firstpage"]')[0] ? `${meta('meta[name="citation_firstpage"]')[0]}${meta('meta[name="citation_lastpage"]')[0] ? `–${meta('meta[name="citation_lastpage"]')[0]}` : ''}` : undefined,
    doi: meta('meta[name="citation_doi"]')[0]?.replace(/^doi:/i, ''),
    url,
  };
}

export async function lookupCitation(input: string): Promise<CitationMeta> {
  const doi = extractDoi(input);
  if (doi && (!/^https?:\/\//i.test(input) || /doi\.org/i.test(input))) return metaFromDoi(doi);
  const page = await fetchPage(input.trim());
  const meta = metaFromHtml(page.html, page.url);
  if (meta.doi && meta.type === 'article') {
    try {
      return { ...(await metaFromDoi(meta.doi)), url: page.url };
    } catch {
      /* fall back to page metadata */
    }
  }
  return meta;
}
