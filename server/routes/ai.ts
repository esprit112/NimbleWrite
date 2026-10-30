// AI endpoints: proofreading, tone, synonyms, streaming writing help and the agents.
import { Router, type Request, type Response } from 'express';
import { z } from 'zod';
import type { AiDetectResult, AssistResponse, CheckOptions, CheckParagraph, CheckResponse, ClaimToCite, GraderResult, Issue, ReaderReactions, SynonymSense, ToneResult, TuneResult } from '../../shared/types';
import { CATEGORIES } from '../../shared/types';
import { TONES } from '../../shared/meta';
import { countWords, sentences } from '../../shared/text';
import { chat, chatJson, chatStream, NimError } from '../nim';
import { config } from '../config';
import { parseLooseJson, salvageObjects } from '../json';
import { placeFindings, type RawFinding } from '../locate';
import { aiDetectPrompt, checkSystemPrompt, claimsPrompt, graderPrompt, readerReactionsPrompt, synonymsSystemPrompt, toneSystemPrompt, writeMessages } from '../prompts';
import { getSettings, recordCheck } from '../store';
import { heuristicSignals } from '../detect';
import { assistFingerprint, mergeAssistIssues } from '../assist';
import { dialectMap } from '../../shared/rules';
import { MAX_INSTRUCTIONS } from '../../shared/tune';
import { parseTuneResult, tuneSystemPrompt } from '../tune';

export const aiRouter = Router();

const dialect = z.enum(['en-GB', 'en-US', 'en-CA', 'en-AU', 'en-IN']);
const goals = z.object({
  audience: z.enum(['general', 'knowledgeable', 'expert']),
  formality: z.enum(['informal', 'neutral', 'formal']),
  domain: z.enum(['academic', 'business', 'general', 'email', 'casual', 'creative']),
  intent: z.array(z.enum(['inform', 'describe', 'convince', 'tell_story'])).max(4),
});
const category = z.enum(['correctness', 'clarity', 'engagement', 'delivery']);

const CheckBody = z.object({
  paragraphs: z.array(z.object({ id: z.string().max(64), text: z.string().max(6000) })).min(1).max(24),
  options: z.object({
    dialect,
    goals,
    dictionary: z.array(z.string().max(80)).max(2000).default([]),
    categories: z.array(category).min(1).default([...CATEGORIES]),
    instructions: z.string().max(MAX_INSTRUCTIONS).default(''),
  }),
});

aiRouter.post('/check', async (req: Request, res: Response) => {
  const { paragraphs, options } = CheckBody.parse(req.body);
  res.json(await checkParagraphs(paragraphs, options, abortOnClose(res).signal));
});

/** Proofread paragraphs with the check model. Offsets are placed by the server, never trusted from the model. */
export async function checkParagraphs(paragraphs: CheckParagraph[], options: CheckOptions, signal?: AbortSignal): Promise<CheckResponse> {
  const settings = await getSettings();
  const model = settings.models.check;
  const totalWords = paragraphs.reduce((s, p) => s + countWords(p.text), 0);
  const out = await chat({
    model,
    temperature: 0,
    maxTokens: Math.min(4096, Math.max(700, 450 + totalWords * 7)),
    signal,
    timeoutMs: 75_000,
    messages: [
      { role: 'system', content: checkSystemPrompt(options) },
      { role: 'user', content: JSON.stringify({ paragraphs }) },
    ],
  });
  const parsed = parseLooseJson(out.text) as { issues?: RawFinding[] } | RawFinding[] | null;
  let findings: RawFinding[] = Array.isArray(parsed) ? parsed : Array.isArray(parsed?.issues) ? parsed.issues : [];
  if (!findings.length && out.text.includes('"original"')) findings = salvageObjects(out.text, 'original') as RawFinding[];

  const allowed = new Set(options.categories);
  const wrongForDialect = dialectMap(options.dialect);
  const byPara = new Map<string, RawFinding[]>();
  for (const f of findings) {
    let pid = typeof f.p === 'string' || typeof f.p === 'number' ? String(f.p) : '';
    if (!paragraphs.some((p) => p.id === pid)) {
      // The model forgot or mangled the paragraph id: assign to the first paragraph containing the text.
      const orig = typeof f.original === 'string' ? f.original : '';
      pid = paragraphs.find((p) => orig && p.text.includes(orig))?.id ?? (paragraphs.length === 1 ? paragraphs[0].id : '');
    }
    if (!pid) continue;
    byPara.set(pid, [...(byPara.get(pid) ?? []), f]);
  }
  const results = paragraphs.map((p) => ({
    id: p.id,
    issues: placeFindings(p.text, byPara.get(p.id) ?? [], allowed)
      .map((i) => ({ ...i, replacements: i.replacements.filter((r) => !violatesDialect(i.original, r, wrongForDialect)) }))
      .filter((i) => i.replacements.length > 0) as Issue[],
  }));
  void recordCheck(totalWords);
  return { results, model, ms: out.ms };
}

const AssistBody = z.object({
  paragraphs: z.array(z.object({ id: z.string().max(64), text: z.string().max(6000) })).min(1).max(24),
  ai: z.boolean().default(true),
});

/**
 * For thin clients (the Windows desktop companion): uses the saved settings, runs the AI check (optional) and the
 * local rules, and returns merged, filtered suggestions per paragraph. If the model fails, local results still return.
 */
aiRouter.post('/assist', async (req, res) => {
  const { paragraphs, ai } = AssistBody.parse(req.body);
  const settings = await getSettings();
  const categories = CATEGORIES.filter((c) => settings.categories[c]);
  const forAi = paragraphs.filter((p) => /[A-Za-z\u00C0-\u024F]/.test(p.text) && countWords(p.text) >= 1);
  let aiById = new Map<string, Issue[]>();
  let model: string | null = null;
  let ms = 0;
  let aiError: string | undefined;
  if (ai && forAi.length && categories.length) {
    try {
      const r = await checkParagraphs(
        forAi,
        { dialect: settings.dialect, goals: settings.defaultGoals, dictionary: settings.dictionary, categories, instructions: settings.customInstructions },
        abortOnClose(res).signal,
      );
      aiById = new Map(r.results.map((x) => [x.id, x.issues]));
      model = r.model;
      ms = r.ms;
    } catch (err) {
      if (err instanceof NimError && err.status === 499) throw err; // the client went away
      aiError = err instanceof Error ? err.message : String(err);
    }
  }
  const body: AssistResponse = {
    results: paragraphs.map((p) => ({ id: p.id, issues: mergeAssistIssues(p.text, aiById.get(p.id) ?? [], settings) })),
    model,
    ms,
    ai: ai && !aiError,
    aiError,
    aiConfigured: !!config.nim.apiKey,
    fingerprint: assistFingerprint(settings),
  };
  res.json(body);
});

const ToneBody = z.object({ text: z.string().min(1).max(20_000) });

aiRouter.post('/tone', async (req, res) => {
  const { text } = ToneBody.parse(req.body);
  const settings = await getSettings();
  const { data } = await chatJson<ToneResult>(
    {
      model: settings.models.check,
      temperature: 0,
      maxTokens: 300,
      timeoutMs: 45_000,
      messages: [{ role: 'system', content: toneSystemPrompt() }, { role: 'user', content: text.slice(0, 8000) }],
    },
    (v) => {
      const o = v as Partial<ToneResult> & { tones?: { name?: string; score?: number }[] } | null;
      if (!o || !Array.isArray(o.tones)) return null;
      const tones = o.tones
        .filter((t) => t && typeof t.name === 'string')
        .map((t) => {
          const name = String(t.name).trim().replace(/^\w/, (c) => c.toUpperCase());
          return { name, emoji: TONES[name] ?? '💬', score: Math.max(1, Math.min(100, Math.round(Number(t.score) || 50))) };
        })
        .slice(0, 3);
      if (!tones.length) return null;
      const sentiment = o.sentiment === 'positive' || o.sentiment === 'negative' ? o.sentiment : 'neutral';
      return { tones, summary: String(o.summary ?? ''), formality: Math.max(0, Math.min(100, Math.round(Number(o.formality) || 50))), sentiment };
    },
  );
  res.json(data);
});

const TuneBody = z.object({ message: z.string().trim().min(2).max(800) });

/** Proposes settings changes from a plain-language request. The browser shows them and applies only what the writer confirms. */
aiRouter.post('/tune', async (req, res) => {
  const { message } = TuneBody.parse(req.body);
  const settings = await getSettings();
  const { data } = await chatJson<TuneResult>(
    {
      model: settings.models.check,
      temperature: 0.1,
      maxTokens: 700,
      timeoutMs: 45_000,
      signal: abortOnClose(res).signal,
      messages: [{ role: 'system', content: tuneSystemPrompt(settings) }, { role: 'user', content: message }],
    },
    (v) => parseTuneResult(v, settings),
  );
  res.json(data);
});

const SynBody = z.object({ word: z.string().min(1).max(60), sentence: z.string().max(1200), dialect: dialect.default('en-GB') });

aiRouter.post('/synonyms', async (req, res) => {
  const { word, sentence, dialect: d } = SynBody.parse(req.body);
  const settings = await getSettings();
  const { data } = await chatJson<{ senses: SynonymSense[] }>(
    {
      model: settings.models.check,
      temperature: 0.2,
      maxTokens: 350,
      timeoutMs: 30_000,
      messages: [{ role: 'system', content: synonymsSystemPrompt(d) }, { role: 'user', content: `Word: "${word}"\nSentence: "${sentence}"` }],
    },
    (v) => {
      const o = v as { senses?: { meaning?: string; synonyms?: unknown[] }[] } | null;
      if (!o || !Array.isArray(o.senses)) return null;
      const senses = o.senses
        .map((s) => ({
          meaning: String(s.meaning ?? ''),
          synonyms: (Array.isArray(s.synonyms) ? s.synonyms : []).map(String).filter((x) => x && x.toLowerCase() !== word.toLowerCase()).slice(0, 6),
        }))
        .filter((s) => s.synonyms.length)
        .slice(0, 3);
      return { senses };
    },
  );
  res.json(data);
});

const WriteBody = z.object({
  task: z.enum(['improve', 'shorten', 'expand', 'simplify', 'professional', 'friendly', 'confident', 'persuasive', 'fix_grammar', 'summarize',
    'continue', 'reply', 'outline', 'brainstorm', 'custom', 'compose', 'paraphrase', 'humanize', 'translate', 'chat']),
  text: z.string().max(60_000).default(''),
  context: z.string().max(60_000).optional(),
  instruction: z.string().max(4000).optional(),
  mode: z.enum(['standard', 'fluent', 'formal', 'academic', 'simple', 'creative', 'expand', 'shorten', 'custom']).optional(),
  targetLanguage: z.string().max(60).optional(),
  sourceLanguage: z.string().max(60).optional(),
  goals: goals.optional(),
  dialect: dialect.optional(),
  history: z.array(z.object({ role: z.enum(['user', 'assistant']), content: z.string().max(20_000) })).max(20).optional(),
});

/** Streams the model's answer as server-sent events: `delta` chunks, then `done` (or `error`). */
aiRouter.post('/write', async (req, res) => {
  const body = WriteBody.parse(req.body);
  const settings = await getSettings();
  const g = body.goals ?? settings.defaultGoals;
  const d = body.dialect ?? settings.dialect;
  const model = body.task === 'translate' ? settings.models.translate : settings.models.write;
  const ac = abortOnClose(res);
  res.writeHead(200, {
    'Content-Type': 'text/event-stream; charset=utf-8',
    'Cache-Control': 'no-cache, no-transform',
    Connection: 'keep-alive',
    'X-Accel-Buffering': 'no',
  });
  const send = (event: string, data: unknown) => res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
  send('meta', { model });
  const words = countWords(body.text);
  const maxTokens = ['expand', 'continue', 'compose', 'outline', 'brainstorm', 'chat', 'custom'].includes(body.task)
    ? 2048 : Math.min(4096, Math.max(600, Math.round(words * 2.6)));
  try {
    const result = await chatStream(
      {
        model,
        temperature: ['brainstorm', 'compose', 'expand', 'continue'].includes(body.task) ? 0.8 : body.task === 'paraphrase' ? 0.7 : 0.4,
        maxTokens,
        signal: ac.signal,
        timeoutMs: 120_000,
        messages: writeMessages(body, d, g, settings.customInstructions),
      },
      (t) => send('delta', { t }),
    );
    send('done', { ms: result.ms, model });
  } catch (err) {
    send('error', { message: err instanceof Error ? err.message : String(err) });
  }
  res.end();
});

const ReaderBody = z.object({ text: z.string().min(1).max(40_000), reader: z.string().max(300).default('') });

aiRouter.post('/agents/reader', async (req, res) => {
  const { text, reader } = ReaderBody.parse(req.body);
  const settings = await getSettings();
  const list = (v: unknown) => (Array.isArray(v) ? v.map(String).filter(Boolean).slice(0, 4) : []);
  const { data } = await chatJson<ReaderReactions>(
    {
      model: settings.models.write,
      temperature: 0.4,
      maxTokens: 1400,
      timeoutMs: 120_000,
      messages: [{ role: 'system', content: readerReactionsPrompt(reader) }, { role: 'user', content: text.slice(0, 30_000) }],
    },
    (v) => {
      const o = v as Partial<ReaderReactions> | null;
      if (!o || typeof o.overall !== 'string') return null;
      return {
        overall: o.overall,
        takeaways: list(o.takeaways),
        questions: list(o.questions),
        confusions: (Array.isArray(o.confusions) ? o.confusions : []).slice(0, 4).map((c) => ({ quote: String(c?.quote ?? ''), issue: String(c?.issue ?? ''), suggestion: String(c?.suggestion ?? '') })),
        suggestions: list(o.suggestions),
      };
    },
  );
  res.json(data);
});

const GraderBody = z.object({ text: z.string().min(1).max(60_000), rubric: z.string().max(20_000).default(''), level: z.string().max(80).default('undergraduate') });

aiRouter.post('/agents/grader', async (req, res) => {
  const { text, rubric, level } = GraderBody.parse(req.body);
  const settings = await getSettings();
  const list = (v: unknown) => (Array.isArray(v) ? v.map(String).filter(Boolean).slice(0, 6) : []);
  const { data } = await chatJson<GraderResult>(
    {
      model: settings.models.write,
      temperature: 0.2,
      maxTokens: 1800,
      timeoutMs: 150_000,
      messages: [
        { role: 'system', content: graderPrompt(level) },
        { role: 'user', content: `Assignment / rubric:\n${rubric.trim() || 'None provided.'}\n\nSubmission:\n"""\n${text.slice(0, 50_000)}\n"""` },
      ],
    },
    (v) => {
      const o = v as Partial<GraderResult> | null;
      if (!o || typeof o.grade !== 'string') return null;
      return {
        grade: o.grade,
        score: Math.max(0, Math.min(100, Math.round(Number(o.score) || 0))),
        summary: String(o.summary ?? ''),
        criteria: (Array.isArray(o.criteria) ? o.criteria : []).slice(0, 8).map((c) => ({ name: String(c?.name ?? ''), score: Math.max(0, Math.min(100, Math.round(Number(c?.score) || 0))), feedback: String(c?.feedback ?? '') })),
        strengths: list(o.strengths),
        improvements: list(o.improvements),
      };
    },
  );
  res.json(data);
});

const ClaimsBody = z.object({ text: z.string().min(1).max(40_000) });

aiRouter.post('/agents/claims', async (req, res) => {
  const { text } = ClaimsBody.parse(req.body);
  const settings = await getSettings();
  const { data } = await chatJson<{ claims: ClaimToCite[] }>(
    {
      model: settings.models.write,
      temperature: 0.1,
      maxTokens: 1400,
      timeoutMs: 120_000,
      messages: [{ role: 'system', content: claimsPrompt() }, { role: 'user', content: text.slice(0, 30_000) }],
    },
    (v) => {
      const o = v as { claims?: Partial<ClaimToCite>[] } | null;
      if (!o || !Array.isArray(o.claims)) return null;
      return {
        claims: o.claims.slice(0, 8).map((c) => ({
          claim: String(c.claim ?? ''),
          reason: String(c.reason ?? ''),
          query: String(c.query ?? c.claim ?? ''),
          strength: (c.strength === 'recommended' ? 'recommended' : 'needs_citation') as ClaimToCite['strength'],
        })).filter((c) => c.claim),
      };
    },
  );
  res.json(data);
});

const DetectBody = z.object({ text: z.string().min(1).max(60_000) });

aiRouter.post('/agents/detect', async (req, res) => {
  const { text } = DetectBody.parse(req.body);
  const settings = await getSettings();
  // Passages: paragraphs, with very long ones split into ~120-word chunks of whole sentences.
  const passages: { id: string; text: string }[] = [];
  for (const para of text.split(/\n+/).map((p) => p.trim()).filter((p) => countWords(p) >= 12)) {
    let chunk = '';
    for (const s of sentences(para)) {
      if (countWords(chunk) + countWords(s) > 130 && chunk) {
        passages.push({ id: `p${passages.length + 1}`, text: chunk.trim() });
        chunk = '';
      }
      chunk += `${s} `;
    }
    if (chunk.trim()) passages.push({ id: `p${passages.length + 1}`, text: chunk.trim() });
  }
  if (!passages.length) {
    res.json({ score: 0, verdict: 'Not enough text to analyse. Write at least a few sentences.', passages: [], signals: [] } satisfies AiDetectResult);
    return;
  }
  const limited = passages.slice(0, 24);
  let llm = new Map<string, { ai: number; reasons: string[] }>();
  try {
    const { data } = await chatJson<Map<string, { ai: number; reasons: string[] }>>(
      {
        model: settings.models.check,
        temperature: 0,
        maxTokens: 300 + limited.length * 140,
        timeoutMs: 90_000,
        messages: [{ role: 'system', content: aiDetectPrompt() }, { role: 'user', content: JSON.stringify({ passages: limited }) }],
      },
      (v) => {
        const o = v as { passages?: { id?: string; ai?: number; reasons?: unknown[] }[] } | null;
        if (!o || !Array.isArray(o.passages)) return null;
        // A passage cut off before its score falls back to the statistics rather than reading as 0% AI.
        return new Map(
          o.passages
            .filter((p) => p.ai != null && Number.isFinite(Number(p.ai)))
            .map((p) => [String(p.id), { ai: Math.max(0, Math.min(100, Number(p.ai))), reasons: (Array.isArray(p.reasons) ? p.reasons : []).map(String).slice(0, 3) }]),
        );
      },
    );
    llm = data;
  } catch (err) {
    if (err instanceof NimError && err.status !== 502) throw err;
  }
  let weighted = 0;
  let totalWords = 0;
  const scored = limited.map((p) => {
    const h = heuristicSignals(p.text);
    const m = llm.get(p.id);
    let score = Math.round(m ? 0.6 * m.ai + 0.4 * h.score : h.score);
    // Several spelling or grammar slips are near-conclusive evidence of a person (current models almost never make
    // them), so a miscalibrated model score must not outvote that evidence.
    if (h.slips >= 3) score = Math.min(score, 30);
    const w = countWords(p.text);
    weighted += score * w;
    totalWords += w;
    return { text: p.text, score, reasons: [...new Set([...(m?.reasons ?? []), ...h.reasons])].slice(0, 4) };
  });
  const flaggedWords = scored.filter((p) => p.score >= 60).reduce((s, p) => s + countWords(p.text), 0);
  const overall = Math.round(weighted / Math.max(1, totalWords));
  const share = Math.round((flaggedWords / Math.max(1, totalWords)) * 100);
  const whole = heuristicSignals(text);
  const result: AiDetectResult = {
    score: share,
    verdict: share >= 60 ? 'Most of this text reads like AI-generated writing.' : share >= 25 ? 'Parts of this text read like AI-generated writing.' : 'This text reads as mostly human-written.',
    passages: scored.sort((a, b) => b.score - a.score),
    signals: [
      { label: 'Average AI likelihood', value: `${overall}%` },
      { label: 'Passages analysed', value: String(scored.length) },
      { label: 'Statistical signals', value: whole.reasons.slice(0, 3).join('; ') || 'None notable' },
      { label: 'Model', value: llm.size ? settings.models.check : 'statistics only (model unavailable)' },
    ],
  };
  res.json(result);
});

function abortOnClose(res: Response): AbortController {
  const ac = new AbortController();
  res.on('close', () => {
    if (!res.writableEnded) ac.abort();
  });
  return ac;
}

/** True when a suggestion would introduce a spelling that is wrong for the user's English variant (e.g. organised -> organized in en-GB). */
export function violatesDialect(original: string, replacement: string, wrong: Map<string, string>): boolean {
  const orig = new Set((original.match(/[A-Za-z]+/g) ?? []).map((w) => w.toLowerCase()));
  return (replacement.match(/[A-Za-z]+/g) ?? []).some((w) => wrong.has(w.toLowerCase()) && !orig.has(w.toLowerCase()));
}
