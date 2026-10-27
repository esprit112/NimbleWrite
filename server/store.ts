// File-backed storage: one JSON file per document, plus settings and daily stats.
// Writes are atomic (temp file + rename) so a crash never leaves a half-written document.
import { randomUUID } from 'node:crypto';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { config } from './config';
import type { Category, DayStats, Doc, DocSummary, Goals, Settings } from '../shared/types';
import { DEFAULT_GOALS } from '../shared/types';
import { countWords } from '../shared/text';

const DOCS_DIR = path.join(config.dataDir, 'docs');
const SETTINGS_FILE = path.join(config.dataDir, 'settings.json');
const STATS_FILE = path.join(config.dataDir, 'stats.json');

export const DEFAULT_SETTINGS: Settings = {
  dialect: 'en-GB',
  defaultGoals: DEFAULT_GOALS,
  showGoalsOnNewDoc: true,
  categories: { correctness: true, clarity: true, engagement: true, delivery: true },
  localRules: true,
  dictionary: [],
  styleRules: [],
  snippets: [
    { id: 'sig', trigger: 'sig', text: 'Kind regards,' },
    { id: 'ty', trigger: 'ty', text: 'Thank you for your time and consideration.' },
  ],
  models: { check: config.nim.checkModel, write: config.nim.writeModel, translate: config.nim.translateModel },
  theme: 'system',
};

async function writeAtomic(file: string, data: unknown): Promise<void> {
  await fs.mkdir(path.dirname(file), { recursive: true });
  const tmp = `${file}.${process.pid}.${Date.now()}.tmp`;
  await fs.writeFile(tmp, JSON.stringify(data, null, 2), 'utf8');
  await fs.rename(tmp, file);
}

async function readJson<T>(file: string): Promise<T | null> {
  try {
    return JSON.parse(await fs.readFile(file, 'utf8')) as T;
  } catch {
    return null;
  }
}

const escapeHtml = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/**
 * HTML for a document that arrives as plain text only, such as the text the desktop companion opens in the editor.
 * The editor shows a document's HTML, never its text, so without this the document opens blank. As in the companion,
 * every line break starts a paragraph (Notepad and Word end lines with \r; Word also uses \a and \f), which keeps
 * paragraphs within the checker's size limit. Manual line breaks (\v, U+2028) stay line breaks, and blank lines are
 * dropped because paragraphs are spaced anyway.
 */
export function plainTextToHtml(text: string): string {
  return text
    .split(/\r\n|[\n\r\f\u0007\u2029]/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => `<p>${line.split(/[\v\u2028]/).map((part) => escapeHtml(part.trim())).join('<br>')}</p>`)
    .join('');
}

const docs = new Map<string, Doc>();
let loaded = false;

export function summarize(d: Doc): DocSummary {
  return {
    id: d.id, title: d.title, snippet: d.text.replace(/\s+/g, ' ').trim().slice(0, 180), wordCount: d.wordCount,
    score: d.score, createdAt: d.createdAt, updatedAt: d.updatedAt, deletedAt: d.deletedAt,
  };
}

const safeId = (id: string) => /^[a-zA-Z0-9-]{6,64}$/.test(id);

export async function loadStore(): Promise<void> {
  if (loaded) return;
  await fs.mkdir(DOCS_DIR, { recursive: true });
  for (const name of await fs.readdir(DOCS_DIR)) {
    if (!name.endsWith('.json')) continue;
    const d = await readJson<Doc>(path.join(DOCS_DIR, name));
    // `html ||`: a document saved from plain text alone (the companion's "Open in NimbleWrite") had no HTML and
    // opened blank. Its text was kept, so show that.
    if (d?.id) docs.set(d.id, { ...d, html: d.html || plainTextToHtml(d.text ?? ''), goals: { ...DEFAULT_GOALS, ...d.goals }, dismissed: d.dismissed ?? [] });
  }
  loaded = true;
  if (docs.size === 0) await createDoc(WELCOME);
}

export function listDocs(trash: boolean): DocSummary[] {
  return [...docs.values()]
    .filter((d) => (trash ? d.deletedAt !== null : d.deletedAt === null))
    .sort((a, b) => b.updatedAt - a.updatedAt)
    .map(summarize);
}

export function allLiveDocs(): Doc[] {
  return [...docs.values()].filter((d) => d.deletedAt === null);
}

export function getDoc(id: string): Doc | undefined {
  return docs.get(id);
}

export async function createDoc(input: { title?: string; html?: string; text?: string; goals?: Goals }): Promise<Doc> {
  const now = Date.now();
  const text = input.text ?? '';
  const doc: Doc = {
    id: randomUUID(),
    title: (input.title ?? '').trim() || 'Untitled document',
    html: input.html || plainTextToHtml(text),
    text,
    goals: input.goals ?? (await getSettings()).defaultGoals,
    dismissed: [],
    wordCount: countWords(text),
    score: null,
    createdAt: now,
    updatedAt: now,
    deletedAt: null,
  };
  docs.set(doc.id, doc);
  await writeAtomic(path.join(DOCS_DIR, `${doc.id}.json`), doc);
  return doc;
}

export async function updateDoc(id: string, patch: Partial<Pick<Doc, 'title' | 'html' | 'text' | 'goals' | 'dismissed' | 'score'>>): Promise<Doc | null> {
  const cur = docs.get(id);
  if (!cur || !safeId(id)) return null;
  // Score / dismissal bookkeeping is not an edit, so it must not reorder the documents list.
  const edited = patch.title !== undefined || patch.html !== undefined || patch.text !== undefined || patch.goals !== undefined;
  const next: Doc = { ...cur, ...patch, updatedAt: edited ? Date.now() : cur.updatedAt };
  // The editor shows html, so new text without new html would never be seen, and the next autosave would undo it.
  // An empty html counts as none, as in createDoc.
  if (patch.text !== undefined && !patch.html) next.html = plainTextToHtml(patch.text);
  if (patch.text !== undefined) next.wordCount = countWords(patch.text);
  if (patch.title !== undefined) next.title = patch.title.trim() || 'Untitled document';
  docs.set(id, next);
  await writeAtomic(path.join(DOCS_DIR, `${id}.json`), next);
  return next;
}

export async function trashDoc(id: string): Promise<boolean> {
  const cur = docs.get(id);
  if (!cur) return false;
  const next = { ...cur, deletedAt: Date.now() };
  docs.set(id, next);
  await writeAtomic(path.join(DOCS_DIR, `${id}.json`), next);
  return true;
}

export async function restoreDoc(id: string): Promise<boolean> {
  const cur = docs.get(id);
  if (!cur) return false;
  const next = { ...cur, deletedAt: null, updatedAt: Date.now() };
  docs.set(id, next);
  await writeAtomic(path.join(DOCS_DIR, `${id}.json`), next);
  return true;
}

export async function deleteDocForever(id: string): Promise<boolean> {
  if (!docs.has(id) || !safeId(id)) return false;
  docs.delete(id);
  await fs.rm(path.join(DOCS_DIR, `${id}.json`), { force: true });
  return true;
}

let settingsCache: Settings | null = null;

export async function getSettings(): Promise<Settings> {
  if (settingsCache) return settingsCache;
  const saved = await readJson<Partial<Settings>>(SETTINGS_FILE);
  settingsCache = {
    ...DEFAULT_SETTINGS,
    ...saved,
    defaultGoals: { ...DEFAULT_SETTINGS.defaultGoals, ...saved?.defaultGoals },
    categories: { ...DEFAULT_SETTINGS.categories, ...saved?.categories },
    models: { ...DEFAULT_SETTINGS.models, ...saved?.models },
  };
  return settingsCache;
}

export async function saveSettings(next: Settings): Promise<Settings> {
  settingsCache = next;
  await writeAtomic(SETTINGS_FILE, next);
  return next;
}

type StatsFile = Record<string, DayStats>;
const today = () => new Date().toISOString().slice(0, 10);
const emptyDay = (): DayStats => ({ accepted: { correctness: 0, clarity: 0, engagement: 0, delivery: 0 }, checks: 0, wordsChecked: 0 });

let statsCache: StatsFile | null = null;
let statsTimer: NodeJS.Timeout | null = null;

async function stats(): Promise<StatsFile> {
  if (!statsCache) statsCache = (await readJson<StatsFile>(STATS_FILE)) ?? {};
  return statsCache;
}

function persistStatsSoon() {
  if (statsTimer) return;
  statsTimer = setTimeout(() => {
    statsTimer = null;
    if (statsCache) void writeAtomic(STATS_FILE, statsCache);
  }, 1500);
}

export async function recordCheck(words: number): Promise<void> {
  const s = await stats();
  const d = (s[today()] ??= emptyDay());
  d.checks++;
  d.wordsChecked += words;
  persistStatsSoon();
}

export async function recordAccepted(category: Category, n = 1): Promise<void> {
  const s = await stats();
  const d = (s[today()] ??= emptyDay());
  d.accepted[category] += n;
  persistStatsSoon();
}

export async function recentStats(days: number): Promise<{ date: string; stats: DayStats }[]> {
  const s = await stats();
  const out: { date: string; stats: DayStats }[] = [];
  for (let i = days - 1; i >= 0; i--) {
    const date = new Date(Date.now() - i * 86_400_000).toISOString().slice(0, 10);
    out.push({ date, stats: s[date] ?? emptyDay() });
  }
  return out;
}

const WELCOME = {
  title: 'Welcome to NimbleWrite',
  html: `<h1>Welcome to NimbleWrite 👋</h1>
<p>NimbleWrite checks your writing as you type, using NVIDIA NIM models. Underlines show suggestions: <strong>red</strong> for correctness, <strong>blue</strong> for clarity, <strong>green</strong> for engagement and <strong>purple</strong> for delivery. Click an underline, or a card on the right, to accept or dismiss it.</p>
<p>Try it on this paragraph. Their going to the libary tomorow, and me and him was planning to finish the report. In my opinion, I think that the the project is very good but it need alot of improvement in order to be successfull.</p>
<p>The report was written by the team in a hurry. We should of checked it more carefully, irregardless of the deadline. Send me the files asap because the client is really really angry and we has no time.</p>
<h2>Things to try</h2>
<ul><li><p>Double-click any word to see synonyms that fit the sentence.</p></li><li><p>Select a sentence and open <strong>AI Assistant</strong> to shorten it, make it more confident or rewrite it however you like.</p></li><li><p>Set your <strong>Goals</strong> (audience, formality, domain, intent) so suggestions match what you are writing.</p></li><li><p>Open the agents on the right: Paraphraser, Humanizer, Reader Reactions, AI Grader, Citations, AI Detector, Plagiarism and Translate.</p></li><li><p>Type <code>\\sig</code> and press space to expand a snippet.</p></li></ul>`,
  text: '',
};
WELCOME.text = WELCOME.html.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim();
