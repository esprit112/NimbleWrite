// NVIDIA NIM client (OpenAI-compatible chat completions) with a concurrency limit, retries,
// reasoning ("thinking") switched off for interactive use, tolerant JSON and SSE streaming.
import { config } from './config';
import { parseLooseJson, stripThinking } from './json';
import type { KeyTestResult } from '../shared/types';

export interface ChatMessage {
  role: 'system' | 'user' | 'assistant';
  content: string;
}

export interface ChatOptions {
  model: string;
  messages: ChatMessage[];
  temperature?: number;
  topP?: number;
  maxTokens?: number;
  signal?: AbortSignal;
  timeoutMs?: number;
}

export class NimError extends Error {
  constructor(message: string, public status: number) {
    super(message);
  }
}

class Semaphore {
  private active = 0;
  private queue: Array<() => void> = [];
  constructor(private readonly max: number) {}
  async acquire(): Promise<() => void> {
    if (this.active >= this.max) await new Promise<void>((resolve) => this.queue.push(resolve));
    this.active++;
    let released = false;
    return () => {
      if (released) return;
      released = true;
      this.active--;
      this.queue.shift()?.();
    };
  }
}

const gate = new Semaphore(config.nim.maxConcurrency);
const rejectsThinkingToggle = new Set<string>();

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

function buildBody(o: ChatOptions, stream: boolean): Record<string, unknown> {
  const body: Record<string, unknown> = {
    model: o.model,
    messages: o.messages,
    temperature: o.temperature ?? 0.2,
    top_p: o.topP ?? 0.95,
    max_tokens: o.maxTokens ?? 1024,
    stream,
  };
  // Nemotron models think before answering unless told not to; interactive writing help needs speed.
  if (/^nvidia\/.*nemotron/i.test(o.model) && !rejectsThinkingToggle.has(o.model)) {
    body.chat_template_kwargs = { enable_thinking: false };
  }
  if (/gpt-oss/i.test(o.model)) body.reasoning_effort = 'low';
  return body;
}

/** Where the user fixes their key: the tray icon for the installed app, .env in development. */
const keyFix = () => (config.packaged ? 'paste it into NimbleWrite (tray icon → NVIDIA API key…)' : 'update NVIDIA_API_KEY in .env');

function friendlyError(status: number, model: string, detail: string): NimError {
  if (status === 401 || status === 403) {
    return new NimError(`NVIDIA rejected the API key. Generate a new one at build.nvidia.com and ${keyFix()}.`, status);
  }
  if (status === 404) return new NimError(`The model "${model}" isn't available on your NVIDIA account. Choose another in Settings.`, status);
  if (status === 429) return new NimError('NVIDIA rate limit reached. Give it a few seconds and try again.', status);
  return new NimError(`NVIDIA NIM error ${status}: ${detail.slice(0, 200)}`, status >= 500 ? 502 : status);
}

async function request(o: ChatOptions, stream: boolean): Promise<{ res: Response; release: () => void }> {
  if (!config.nim.apiKey) {
    throw new NimError(
      config.packaged
        ? 'AI features need your NVIDIA API key. Add it from the NimbleWrite tray icon (Connect to NVIDIA…).'
        : 'NVIDIA_API_KEY is not set. Add it to .env and restart NimbleWrite.',
      503,
    );
  }
  const release = await gate.acquire();
  try {
    for (let attempt = 0; ; attempt++) {
      const signals = [AbortSignal.timeout(o.timeoutMs ?? 90_000)];
      if (o.signal) signals.push(o.signal);
      let res: Response;
      try {
        res = await fetch(`${config.nim.baseUrl}/chat/completions`, {
          method: 'POST',
          signal: AbortSignal.any(signals),
          headers: {
            Authorization: `Bearer ${config.nim.apiKey}`,
            'Content-Type': 'application/json',
            Accept: stream ? 'text/event-stream' : 'application/json',
          },
          body: JSON.stringify(buildBody(o, stream)),
        });
      } catch (err) {
        if (o.signal?.aborted) throw new NimError('Request cancelled', 499);
        const timedOut = err instanceof Error && (err.name === 'TimeoutError' || err.name === 'AbortError');
        if (attempt < 2 && !timedOut) {
          await sleep(800 * 2 ** attempt);
          continue;
        }
        throw new NimError(timedOut ? `The model "${o.model}" took too long to answer. Try again or pick a faster model in Settings.` : `Could not reach NVIDIA NIM: ${String(err)}`, 504);
      }
      if (res.ok) return { res, release };
      const detail = await res.text().catch(() => '');
      if ((res.status === 400 || res.status === 422) && /chat_template_kwargs|enable_thinking|unexpected|extra/i.test(detail) && !rejectsThinkingToggle.has(o.model)) {
        rejectsThinkingToggle.add(o.model);
        continue;
      }
      if ([429, 500, 502, 503, 504].includes(res.status) && attempt < 2) {
        const retryAfter = Number(res.headers.get('retry-after'));
        await sleep(Number.isFinite(retryAfter) && retryAfter > 0 ? Math.min(retryAfter, 10) * 1000 : 1000 * 2 ** attempt + Math.random() * 400);
        continue;
      }
      throw friendlyError(res.status, o.model, detail);
    }
  } catch (err) {
    release();
    throw err;
  }
}

export async function chat(o: ChatOptions): Promise<{ text: string; ms: number; finish?: string }> {
  const t0 = Date.now();
  const { res, release } = await request(o, false);
  try {
    const data = (await res.json()) as { choices?: { message?: { content?: string }; finish_reason?: string }[] };
    const choice = data.choices?.[0];
    return { text: stripThinking(String(choice?.message?.content ?? '')), ms: Date.now() - t0, finish: choice?.finish_reason };
  } finally {
    release();
  }
}

/** Chat expecting JSON. Retries once with a firm reminder if the reply cannot be parsed or validated. */
export async function chatJson<T>(o: ChatOptions, validate: (v: unknown) => T | null): Promise<{ data: T; ms: number }> {
  const first = await chat(o);
  const v1 = validate(parseLooseJson(first.text));
  if (v1 !== null) return { data: v1, ms: first.ms };
  const second = await chat({
    ...o,
    temperature: 0,
    messages: [
      ...o.messages,
      { role: 'assistant', content: first.text.slice(0, 4000) },
      { role: 'user', content: 'That reply was not the requested JSON. Reply again with ONLY the JSON object and nothing else.' },
    ],
  });
  const v2 = validate(parseLooseJson(second.text));
  if (v2 === null) throw new NimError('The model did not return usable JSON. Try again, or choose a different model in Settings.', 502);
  return { data: v2, ms: first.ms + second.ms };
}

/** Visible text so far: hides <think> blocks and holds back a partially streamed "<think" tag. */
function visibleText(full: string, final: boolean): string {
  let s = full.replace(/<think>[\s\S]*?<\/think>/gi, '');
  const open = s.search(/<think>/i);
  if (open >= 0) s = s.slice(0, open);
  if (!final) {
    for (let k = 6; k >= 1; k--) {
      if (s.toLowerCase().endsWith('<think>'.slice(0, k))) {
        s = s.slice(0, -k);
        break;
      }
    }
  }
  return s.replace(/^\s+/, '');
}

export async function chatStream(o: ChatOptions, onDelta: (text: string) => void): Promise<{ text: string; ms: number }> {
  const t0 = Date.now();
  const { res, release } = await request(o, true);
  let full = '';
  let emitted = 0;
  const emit = (final: boolean) => {
    const vis = visibleText(full, final);
    if (vis.length > emitted) {
      onDelta(vis.slice(emitted));
      emitted = vis.length;
    }
  };
  try {
    if (!res.body) throw new NimError('Empty response from NVIDIA NIM', 502);
    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = '';
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });
      let nl: number;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, nl).trim();
        buf = buf.slice(nl + 1);
        if (!line.startsWith('data:')) continue;
        const payload = line.slice(5).trim();
        if (!payload || payload === '[DONE]') continue;
        try {
          const json = JSON.parse(payload) as { choices?: { delta?: { content?: string } }[] };
          const delta = json.choices?.[0]?.delta?.content;
          if (typeof delta === 'string' && delta) {
            full += delta;
            emit(false);
          }
        } catch {
          /* ignore keep-alive or partial lines */
        }
      }
    }
    emit(true);
    return { text: visibleText(full, true), ms: Date.now() - t0 };
  } finally {
    release();
  }
}

let modelCache: { at: number; ids: string[] } | null = null;
const NON_CHAT = /embed|reward|guard|safety|clip|parse|detector|calibration|vila|neva|kosmos|deplot|fuyu|vlm|topic-control|starcoder|codegemma|codellama|code-instruct|codestral|cosmos|diffusion|muse|recurrentgemma|gemma-2b|llama2|vision|omni/i;

export async function listChatModels(): Promise<string[]> {
  if (modelCache && Date.now() - modelCache.at < 10 * 60_000) return modelCache.ids;
  const res = await fetch(`${config.nim.baseUrl}/models`, {
    signal: AbortSignal.timeout(15_000),
    headers: config.nim.apiKey ? { Authorization: `Bearer ${config.nim.apiKey}` } : {},
  });
  if (!res.ok) throw new NimError(`Could not list NIM models (${res.status})`, 502);
  const data = (await res.json()) as { data?: { id: string }[] };
  const ids = (data.data ?? []).map((m) => m.id).filter((id) => !NON_CHAT.test(id)).sort();
  modelCache = { at: Date.now(), ids };
  return ids;
}

/**
 * Checks a key the user is about to save, with one tiny request (max_tokens 1) to the check model.
 * The key is used for this request only: it isn't stored, logged or sent back.
 */
export async function testApiKey(key: string, model: string): Promise<KeyTestResult> {
  let res: Response;
  try {
    res = await fetch(`${config.nim.baseUrl}/chat/completions`, {
      method: 'POST',
      signal: AbortSignal.timeout(30_000),
      headers: { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(buildBody({ model, messages: [{ role: 'user', content: 'Reply with OK.' }], maxTokens: 1, temperature: 0 }, false)),
    });
  } catch {
    return { ok: false, reason: 'network', message: "Couldn't reach NVIDIA to check the key. Check your internet connection and try again." };
  }
  await res.body?.cancel().catch(() => undefined);
  // 400/422: the key got past NVIDIA's sign-in check and only the tiny test request itself was refused.
  if (res.ok || res.status === 400 || res.status === 422) return { ok: true };
  if (res.status === 401 || res.status === 403) {
    return { ok: false, reason: 'rejected', message: "NVIDIA didn't accept that key. Check you copied all of it, or create a new one at build.nvidia.com." };
  }
  if (res.status === 404) {
    return { ok: true, reason: 'model', message: `NVIDIA accepted the key, but the model "${model}" isn't available to it. Pick another check model in the editor's Settings.` };
  }
  if (res.status === 429) {
    return { ok: true, reason: 'rate', message: 'NVIDIA accepted the key but is rate-limiting it right now. AI suggestions start once the limit resets.' };
  }
  return { ok: false, reason: 'error', message: `NVIDIA couldn't check the key just now (error ${res.status}). Try again in a minute.` };
}
