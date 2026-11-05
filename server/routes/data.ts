// Documents, settings, stats, import and research tools (plagiarism, citations).
import express, { Router } from 'express';
import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import * as cheerio from 'cheerio';
import mammoth from 'mammoth';
import { marked } from 'marked';
import { CATEGORIES } from '../../shared/types';
import { ISSUE_TYPES } from '../../shared/meta';
import { applyTuneChanges, MAX_INSTRUCTIONS } from '../../shared/tune';
import { computeStats, overallScore, readabilityLabel } from '../../shared/text';
import type { CitationMeta, CitationStyle, HealthResponse, InsightsResponse, KeyTestResult } from '../../shared/types';
import { config } from '../config';
import { chat, listChatModels, testApiKey } from '../nim';
import {
  allLiveDocs, createDoc, deleteDocForever, getDoc, getSettings, listDocs, recentStats, recordAccepted, restoreDoc, saveSettings, trashDoc, updateDoc,
} from '../store';
import { checkPlagiarism } from '../plagiarism';
import { formatCitation, lookupCitation } from '../citation';
import { webSearch } from '../web';

export const dataRouter = Router();

const goals = z.object({
  audience: z.enum(['general', 'knowledgeable', 'expert']),
  formality: z.enum(['informal', 'neutral', 'formal']),
  domain: z.enum(['academic', 'business', 'general', 'email', 'casual', 'creative']),
  intent: z.array(z.enum(['inform', 'describe', 'convince', 'tell_story'])).max(4),
});

const idParam = (v: unknown) => z.string().regex(/^[a-zA-Z0-9-]{6,64}$/).parse(v);

dataRouter.get('/health', async (_req, res) => {
  const s = await getSettings();
  const body: HealthResponse = {
    ok: true,
    nimConfigured: !!config.nim.apiKey,
    searchConfigured: !!config.braveKey,
    models: s.models,
    version: config.version,
    packaged: config.packaged,
  };
  res.json(body);
});

const KeyBody = z.object({ key: z.string().max(1000) });

/** Check an NVIDIA API key before the desktop app saves it. The key is used for one request and never kept here. */
dataRouter.post('/setup/test-key', async (req, res) => {
  const parsed = KeyBody.safeParse(req.body ?? {});
  const key = parsed.success ? parsed.data.key.trim() : '';
  if (key.length < 8 || key.length > 300 || /\s/.test(key)) {
    const body: KeyTestResult = { ok: false, reason: 'invalid', message: "That doesn't look like an NVIDIA API key. Keys from build.nvidia.com start with nvapi-." };
    res.json(body);
    return;
  }
  const settings = await getSettings();
  res.json(await testApiKey(key, settings.models.check));
});

// ---------- documents ----------
dataRouter.get('/docs', (req, res) => {
  res.json({ docs: listDocs(req.query.trash === '1') });
});

const DocCreate = z.object({ title: z.string().max(300).optional(), html: z.string().max(5_000_000).optional(), text: z.string().max(2_000_000).optional(), goals: goals.optional() });

dataRouter.post('/docs', async (req, res) => {
  res.status(201).json(await createDoc(DocCreate.parse(req.body ?? {})));
});

dataRouter.get('/docs/:id', (req, res) => {
  const doc = getDoc(idParam(req.params.id));
  if (!doc) {
    res.status(404).json({ error: 'Document not found' });
    return;
  }
  res.json(doc);
});

const DocPatch = z.object({
  title: z.string().max(300).optional(),
  html: z.string().max(5_000_000).optional(),
  text: z.string().max(2_000_000).optional(),
  goals: goals.optional(),
  dismissed: z.array(z.string().max(400)).max(5000).optional(),
  score: z.number().min(0).max(100).nullable().optional(),
});

dataRouter.put('/docs/:id', async (req, res) => {
  const doc = await updateDoc(idParam(req.params.id), DocPatch.parse(req.body));
  if (!doc) {
    res.status(404).json({ error: 'Document not found' });
    return;
  }
  res.json(doc);
});

dataRouter.delete('/docs/:id', async (req, res) => {
  const id = idParam(req.params.id);
  const ok = req.query.forever === '1' ? await deleteDocForever(id) : await trashDoc(id);
  res.status(ok ? 204 : 404).end();
});

dataRouter.post('/docs/:id/restore', async (req, res) => {
  res.status((await restoreDoc(idParam(req.params.id))) ? 204 : 404).end();
});

dataRouter.post('/docs/:id/duplicate', async (req, res) => {
  const src = getDoc(idParam(req.params.id));
  if (!src) {
    res.status(404).json({ error: 'Document not found' });
    return;
  }
  res.status(201).json(await createDoc({ title: `${src.title} (copy)`, html: src.html, text: src.text, goals: src.goals }));
});

dataRouter.post('/trash/empty', async (_req, res) => {
  for (const d of listDocs(true)) await deleteDocForever(d.id);
  res.status(204).end();
});

// ---------- import ----------
function sanitizeHtml(html: string): string {
  const $ = cheerio.load(html);
  $('script, style, iframe, object, embed, link, meta').remove();
  $('*').each((_, el) => {
    for (const attr of Object.keys((el as unknown as { attribs?: Record<string, string> }).attribs ?? {})) {
      if (/^on/i.test(attr) || (attr === 'href' && /^\s*javascript:/i.test($(el).attr('href') ?? ''))) $(el).removeAttr(attr);
    }
  });
  return $('body').html() ?? '';
}

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

dataRouter.post('/import', express.raw({ type: '*/*', limit: '25mb' }), async (req, res) => {
  const name = decodeURIComponent(String(req.headers['x-filename'] ?? 'document.txt'));
  const buf = req.body as Buffer;
  if (!Buffer.isBuffer(buf) || !buf.length) {
    res.status(400).json({ error: 'Empty file' });
    return;
  }
  const ext = name.toLowerCase().split('.').pop() ?? '';
  let html: string;
  if (ext === 'docx') {
    html = (await mammoth.convertToHtml({ buffer: buf })).value;
  } else if (ext === 'md' || ext === 'markdown') {
    html = await marked.parse(buf.toString('utf8'));
  } else if (ext === 'html' || ext === 'htm') {
    html = buf.toString('utf8');
  } else if (ext === 'txt' || ext === 'text' || !ext) {
    html = buf.toString('utf8').split(/\r?\n\s*\r?\n/).map((p) => `<p>${esc(p).replace(/\r?\n/g, '<br>')}</p>`).join('');
  } else {
    res.status(415).json({ error: 'Unsupported file type. Use .docx, .txt, .md or .html' });
    return;
  }
  html = sanitizeHtml(html);
  const text = cheerio.load(html).root().text().replace(/\s+/g, ' ').trim();
  res.json({ title: name.replace(/\.[^.]+$/, ''), html, text });
});

// ---------- settings & models ----------
const SettingsBody = z.object({
  dialect: z.enum(['en-GB', 'en-US', 'en-CA', 'en-AU', 'en-IN']),
  defaultGoals: goals,
  showGoalsOnNewDoc: z.boolean(),
  categories: z.object({ correctness: z.boolean(), clarity: z.boolean(), engagement: z.boolean(), delivery: z.boolean() }),
  localRules: z.boolean(),
  dictionary: z.array(z.string().trim().min(1).max(80)).max(5000),
  styleRules: z.array(z.object({ id: z.string().max(64), find: z.string().max(200), replace: z.string().max(200), note: z.string().max(300).optional(), caseSensitive: z.boolean().optional() })).max(500),
  snippets: z.array(z.object({ id: z.string().max(64), trigger: z.string().regex(/^[\w-]{1,30}$/), text: z.string().max(5000) })).max(200),
  models: z.object({ check: z.string().min(3).max(120), write: z.string().min(3).max(120), translate: z.string().min(3).max(120) }),
  theme: z.enum(['light', 'dark', 'system']),
  customInstructions: z.string().max(MAX_INSTRUCTIONS).default(''),
  mutedTypes: z.array(z.enum(ISSUE_TYPES as [string, ...string[]])).max(60).default([]),
});

dataRouter.get('/settings', async (_req, res) => {
  res.json(await getSettings());
});

dataRouter.put('/settings', async (req, res) => {
  const next = SettingsBody.parse(req.body);
  next.dictionary = [...new Set(next.dictionary)];
  next.mutedTypes = [...new Set(next.mutedTypes)];
  res.json(await saveSettings(next));
});

const DictionaryWord = z.object({ word: z.string().trim().min(1).max(80) });

/** Add one word to the personal dictionary without replacing the whole settings object (used by the desktop companion). */
dataRouter.post('/settings/dictionary', async (req, res) => {
  const { word } = DictionaryWord.parse(req.body);
  const s = await getSettings();
  if (!s.dictionary.some((w) => w.toLowerCase() === word.toLowerCase())) await saveSettings({ ...s, dictionary: [...s.dictionary, word].slice(-5000) });
  res.status(204).end();
});

/**
 * Change a few settings without replacing the rest (used by the desktop companion's Settings window). PUT replaces
 * the whole object, which would undo anything changed in the editor meanwhile; this merges on the server instead.
 * Only these fields: the dictionary, style guide, snippets and models are edited in the editor.
 */
const SettingsPatch = z.object({
  dialect: SettingsBody.shape.dialect.optional(),
  categories: z.object({ correctness: z.boolean(), clarity: z.boolean(), engagement: z.boolean(), delivery: z.boolean() }).partial().optional(),
  localRules: z.boolean().optional(),
  defaultGoals: goals.optional(),
  showGoalsOnNewDoc: z.boolean().optional(),
  theme: SettingsBody.shape.theme.optional(),
}).strict();

dataRouter.patch('/settings', async (req, res) => {
  const p = SettingsPatch.parse(req.body ?? {});
  const cur = await getSettings();
  const next = { ...cur, ...p, categories: { ...cur.categories, ...p.categories } };
  if (!Object.values(next.categories).some(Boolean)) {
    res.status(400).json({ error: 'Keep at least one suggestion category on.' });
    return;
  }
  res.json(await saveSettings(next));
});

/** The changes the Tune suggestions chat proposes (same shapes as shared/types.ts TuneChange). */
const TuneChangeBody = z.discriminatedUnion('kind', [
  z.object({ kind: z.literal('instruction'), text: z.string().max(MAX_INSTRUCTIONS) }),
  z.object({ kind: z.literal('mute'), type: z.enum(ISSUE_TYPES as [string, ...string[]]), title: z.string().max(200) }),
  z.object({ kind: z.literal('unmute'), type: z.enum(ISSUE_TYPES as [string, ...string[]]), title: z.string().max(200) }),
  z.object({ kind: z.literal('dictionary'), word: z.string().max(80) }),
  z.object({ kind: z.literal('style_rule'), find: z.string().max(200), replace: z.string().max(200), note: z.string().max(300).optional() }),
]);

/** Apply confirmed Tune changes here, with the same code the editor uses, so thin clients don't need their own copy. */
dataRouter.post('/settings/tune', async (req, res) => {
  const { changes } = z.object({ changes: z.array(TuneChangeBody).min(1).max(20) }).parse(req.body ?? {});
  const cur = await getSettings();
  const { patch, applied, skipped } = applyTuneChanges(cur, changes, () => randomUUID().slice(0, 8));
  const settings = applied > 0 ? await saveSettings({ ...cur, ...patch }) : cur;
  res.json({ settings, applied, skipped });
});

// ---------- text statistics ----------
const StatsBody = z.object({
  text: z.string().min(1).max(200_000),
  counts: z.object({ correctness: z.number().int().min(0), clarity: z.number().int().min(0), engagement: z.number().int().min(0), delivery: z.number().int().min(0) }).optional(),
});

/** Word counts, reading time, readability and (given the open suggestion counts) the overall score, as the editor shows them. */
dataRouter.post('/text/stats', (req, res) => {
  const { text, counts } = StatsBody.parse(req.body ?? {});
  const stats = computeStats(text);
  res.json({ stats, readability: readabilityLabel(stats.readability), score: counts ? overallScore(stats.words, counts, stats.readability) : null });
});

dataRouter.get('/models', async (_req, res) => {
  res.json({
    models: await listChatModels(),
    recommended: {
      check: ['nvidia/nemotron-3.5-lightning-30b-a3b', 'nvidia/nemotron-3.5-bolt-e4b', 'nvidia/nemotron-3-ultra-550b-a55b'],
      write: ['nvidia/nemotron-3-ultra-550b-a55b', 'nvidia/nemotron-3.5-lightning-30b-a3b'],
      translate: ['nvidia/nemotron-3-ultra-550b-a55b', 'nvidia/riva-translate-4b-instruct-v2'],
    },
  });
});

dataRouter.post('/models/test', async (req, res) => {
  const { model } = z.object({ model: z.string().min(3).max(120) }).parse(req.body);
  try {
    const r = await chat({ model, maxTokens: 20, temperature: 0, timeoutMs: 45_000, messages: [{ role: 'user', content: 'Reply with the single word: ready' }] });
    res.json({ ok: true, ms: r.ms, reply: r.text.slice(0, 60) });
  } catch (e) {
    res.json({ ok: false, error: (e as Error).message });
  }
});

// ---------- stats ----------
dataRouter.post('/stats/accepted', async (req, res) => {
  const { category, n } = z.object({ category: z.enum(CATEGORIES as [string, ...string[]]), n: z.number().int().min(1).max(500).default(1) }).parse(req.body);
  await recordAccepted(category as (typeof CATEGORIES)[number], n);
  res.status(204).end();
});

dataRouter.get('/insights', async (_req, res) => {
  const days = await recentStats(7);
  const live = allLiveDocs();
  const body: InsightsResponse = {
    days,
    totals: {
      docs: live.length,
      words: live.reduce((s, d) => s + d.wordCount, 0),
      accepted: days.reduce((s, d) => s + Object.values(d.stats.accepted).reduce((a, b) => a + b, 0), 0),
    },
  };
  res.json(body);
});

// ---------- plagiarism & citations ----------
const PlagBody = z.object({
  text: z.string().min(1).max(200_000),
  docId: z.string().max(64).optional(),
  includeDocs: z.boolean().default(true),
  web: z.boolean().default(true),
  sources: z.array(z.object({ title: z.string().max(300).optional(), text: z.string().max(400_000).optional(), url: z.string().max(2000).optional() })).max(10).default([]),
});

dataRouter.post('/plagiarism', async (req, res) => {
  res.json(await checkPlagiarism(PlagBody.parse(req.body)));
});

const STYLES: CitationStyle[] = ['apa', 'mla', 'chicago', 'harvard'];

dataRouter.post('/citation/lookup', async (req, res) => {
  const { input } = z.object({ input: z.string().min(4).max(2000) }).parse(req.body);
  try {
    const meta = await lookupCitation(input.trim());
    res.json({ meta, formatted: STYLES.map((s) => formatCitation(meta, s)) });
  } catch (e) {
    res.status(422).json({ error: `Couldn't read that source: ${(e as Error).message}. You can enter the details manually.` });
  }
});

const MetaBody = z.object({
  type: z.enum(['webpage', 'article', 'book', 'report']),
  title: z.string().max(500),
  authors: z.array(z.object({ given: z.string().max(100).optional(), family: z.string().max(100).optional(), literal: z.string().max(200).optional() })).max(50),
  siteName: z.string().max(200).optional(),
  containerTitle: z.string().max(300).optional(),
  publisher: z.string().max(200).optional(),
  published: z.string().max(40).optional(),
  accessed: z.string().max(40).optional(),
  volume: z.string().max(20).optional(),
  issue: z.string().max(20).optional(),
  pages: z.string().max(40).optional(),
  doi: z.string().max(200).optional(),
  url: z.string().max(2000).optional(),
});

dataRouter.post('/citation/format', (req, res) => {
  const meta = MetaBody.parse(req.body) as CitationMeta;
  res.json({ formatted: STYLES.map((s) => formatCitation(meta, s)) });
});

dataRouter.post('/citation/search', async (req, res) => {
  const { query } = z.object({ query: z.string().min(3).max(400) }).parse(req.body);
  if (!config.braveKey) {
    res.json({ configured: false, hits: [] });
    return;
  }
  res.json({ configured: true, hits: await webSearch(query, 5) });
});
