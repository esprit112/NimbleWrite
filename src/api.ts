// Typed client for the NimbleWrite API (all AI calls go through the server; the key never reaches the browser).
import type {
  AiDetectResult, CheckOptions, CheckParagraph, CheckResponse, CitationMeta, ClaimToCite, Doc, DocSummary, FormattedCitation,
  GraderResult, HealthResponse, InsightsResponse, PlagiarismResult, ReaderReactions, Settings, SynonymSense, ToneResult, WriteRequest,
} from '../shared/types';

export class ApiError extends Error {
  constructor(message: string, public status: number) {
    super(message);
  }
}

async function req<T>(path: string, init: RequestInit = {}): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`/api${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) } });
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e;
    throw new ApiError('NimbleWrite server is not reachable. Is it running?', 0);
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  let body: unknown = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    /* not JSON */
  }
  if (!res.ok) throw new ApiError((body as { error?: string } | null)?.error ?? `Request failed (${res.status})`, res.status);
  return body as T;
}

const post = <T>(path: string, body: unknown, signal?: AbortSignal) => req<T>(path, { method: 'POST', body: JSON.stringify(body), signal });

export const api = {
  health: () => req<HealthResponse>('/health'),
  listDocs: (trash = false) => req<{ docs: DocSummary[] }>(`/docs${trash ? '?trash=1' : ''}`).then((r) => r.docs),
  getDoc: (id: string) => req<Doc>(`/docs/${id}`),
  createDoc: (body: Partial<Pick<Doc, 'title' | 'html' | 'text' | 'goals'>> = {}) => post<Doc>('/docs', body),
  updateDoc: (id: string, patch: Partial<Pick<Doc, 'title' | 'html' | 'text' | 'goals' | 'dismissed' | 'score'>>) =>
    req<Doc>(`/docs/${id}`, { method: 'PUT', body: JSON.stringify(patch) }),
  deleteDoc: (id: string, forever = false) => req<void>(`/docs/${id}${forever ? '?forever=1' : ''}`, { method: 'DELETE' }),
  restoreDoc: (id: string) => req<void>(`/docs/${id}/restore`, { method: 'POST' }),
  duplicateDoc: (id: string) => req<Doc>(`/docs/${id}/duplicate`, { method: 'POST' }),
  emptyTrash: () => req<void>('/trash/empty', { method: 'POST' }),
  importFile: async (file: File) => {
    const res = await fetch('/api/import', { method: 'POST', headers: { 'X-Filename': encodeURIComponent(file.name), 'Content-Type': 'application/octet-stream' }, body: file });
    const body = (await res.json()) as { title: string; html: string; text: string; error?: string };
    if (!res.ok) throw new ApiError(body.error ?? 'Import failed', res.status);
    return body;
  },
  getSettings: () => req<Settings>('/settings'),
  saveSettings: (s: Settings) => req<Settings>('/settings', { method: 'PUT', body: JSON.stringify(s) }),
  models: () => req<{ models: string[]; recommended: Record<'check' | 'write' | 'translate', string[]> }>('/models'),
  testModel: (model: string) => post<{ ok: boolean; ms?: number; reply?: string; error?: string }>('/models/test', { model }),
  // Limits below mirror the server's Zod schemas so long documents degrade gracefully instead of failing.
  check: (paragraphs: CheckParagraph[], options: CheckOptions, signal?: AbortSignal) =>
    post<CheckResponse>('/ai/check', {
      paragraphs: paragraphs.slice(0, 24).map((p) => ({ id: p.id, text: p.text.slice(0, 6000) })),
      options: { ...options, dictionary: options.dictionary.slice(0, 2000) },
    }, signal),
  tone: (text: string, signal?: AbortSignal) => post<ToneResult>('/ai/tone', { text: text.slice(0, 20_000) }, signal),
  synonyms: (word: string, sentence: string, dialect: string, signal?: AbortSignal) =>
    post<{ senses: SynonymSense[] }>('/ai/synonyms', { word: word.slice(0, 60), sentence: sentence.slice(0, 1200), dialect }, signal),
  reader: (text: string, reader: string) => post<ReaderReactions>('/ai/agents/reader', { text: text.slice(0, 40_000), reader: reader.slice(0, 300) }),
  grader: (text: string, rubric: string, level: string) =>
    post<GraderResult>('/ai/agents/grader', { text: text.slice(0, 60_000), rubric: rubric.slice(0, 20_000), level: level.slice(0, 80) }),
  claims: (text: string) => post<{ claims: ClaimToCite[] }>('/ai/agents/claims', { text: text.slice(0, 40_000) }),
  detect: (text: string) => post<AiDetectResult>('/ai/agents/detect', { text: text.slice(0, 60_000) }),
  plagiarism: (body: { text: string; docId?: string; includeDocs: boolean; web: boolean; sources: { title?: string; text?: string; url?: string }[] }) =>
    post<PlagiarismResult>('/plagiarism', body),
  citationLookup: (input: string) => post<{ meta: CitationMeta; formatted: FormattedCitation[] }>('/citation/lookup', { input }),
  citationFormat: (meta: CitationMeta) => post<{ formatted: FormattedCitation[] }>('/citation/format', meta),
  citationSearch: (query: string) => post<{ configured: boolean; hits: { title: string; url: string; snippet: string }[] }>('/citation/search', { query }),
  accepted: (category: string, n = 1) => post<void>('/stats/accepted', { category, n }).catch(() => undefined),
  insights: () => req<InsightsResponse>('/insights'),
};

/** POST /api/ai/write and read the server-sent event stream. Resolves with the full text. */
export async function streamWrite(body: WriteRequest, onDelta: (chunk: string) => void, signal?: AbortSignal): Promise<{ text: string; model: string; ms: number }> {
  const res = await fetch('/api/ai/write', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal });
  if (!res.ok || !res.body) {
    const err = (await res.json().catch(() => ({}))) as { error?: string };
    throw new ApiError(err.error ?? `Request failed (${res.status})`, res.status);
  }
  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buf = '';
  let text = '';
  let model = '';
  let ms = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buf += decoder.decode(value, { stream: true });
    let idx: number;
    while ((idx = buf.indexOf('\n\n')) >= 0) {
      const block = buf.slice(0, idx);
      buf = buf.slice(idx + 2);
      const event = /^event: (.+)$/m.exec(block)?.[1];
      const dataLine = /^data: (.*)$/m.exec(block)?.[1];
      if (!event || dataLine === undefined) continue;
      const data = JSON.parse(dataLine) as { t?: string; model?: string; ms?: number; message?: string };
      if (event === 'delta' && data.t) {
        text += data.t;
        onDelta(data.t);
      } else if (event === 'meta' && data.model) model = data.model;
      else if (event === 'done') ms = data.ms ?? 0;
      else if (event === 'error') throw new ApiError(data.message ?? 'The model failed to respond', 502);
    }
  }
  return { text, model, ms };
}
