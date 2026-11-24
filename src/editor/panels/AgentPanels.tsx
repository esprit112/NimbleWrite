// AI assistant and agent panels: Assistant, Paraphraser, Humanizer, Translate, Reader Reactions,
// AI Grader, Citations (+ Citation Finder), AI Detector and Plagiarism.
import { useEffect, useRef, useState } from 'react';
import DOMPurify from 'dompurify';
import {
  Drama, ExternalLink, FileUp, GraduationCap, Languages, Quote, ScanSearch, Search,
  Send, ShieldCheck, Sparkles, UserRound, WandSparkles, BookOpenCheck,
} from 'lucide-react';
import type { AiDetectResult, CitationMeta, CitationStyle, ClaimToCite, FormattedCitation, GraderResult, ParaphraseMode, PlagiarismResult, ReaderReactions, WriteTask } from '../../../shared/types';
import { LANGUAGES, PARAPHRASE_MODES } from '../../../shared/meta';
import { api } from '../../api';
import { toast, useSettings } from '../../store';
import { Button, cx, Pill, Spinner } from '../../components/ui';
import { findText, getTarget, selectRange } from '../editorUtils';
import {
  ErrorNote, Field, inputClass, PanelShell, ResultCard, resolveTarget, TargetPicker, useAsync, useWriter, type PanelContext, type TargetChoice,
} from './common';

const QUICK: { task: WriteTask; label: string; kind: 'replace' | 'insert' }[] = [
  { task: 'improve', label: 'Improve it', kind: 'replace' },
  { task: 'shorten', label: 'Shorten it', kind: 'replace' },
  { task: 'expand', label: 'Make it more detailed', kind: 'replace' },
  { task: 'simplify', label: 'Simplify it', kind: 'replace' },
  { task: 'professional', label: 'Sound professional', kind: 'replace' },
  { task: 'friendly', label: 'Sound friendly', kind: 'replace' },
  { task: 'confident', label: 'Sound confident', kind: 'replace' },
  { task: 'persuasive', label: 'Make it persuasive', kind: 'replace' },
  { task: 'fix_grammar', label: 'Fix grammar only', kind: 'replace' },
  { task: 'summarize', label: 'Summarise it', kind: 'insert' },
  { task: 'continue', label: 'Continue writing', kind: 'insert' },
  { task: 'reply', label: 'Write a reply', kind: 'insert' },
  { task: 'outline', label: 'Create an outline', kind: 'insert' },
  { task: 'brainstorm', label: 'Brainstorm ideas', kind: 'insert' },
];

export function AssistantPanel({ ctx, initialPrompt }: { ctx: PanelContext; initialPrompt?: string }) {
  const { editor } = ctx;
  const settings = useSettings((s) => s.settings);
  const writer = useWriter();
  const [choice, setChoice] = useState<TargetChoice | 'auto'>('auto');
  const [prompt, setPrompt] = useState(initialPrompt ?? '');
  const [mode, setMode] = useState<'replace' | 'insert'>('replace');

  const run = (task: WriteTask, kind: 'replace' | 'insert', instruction?: string) => {
    const target = resolveTarget(editor, choice);
    if (!target) return toast('Select some text first.');
    if (!target.text.trim() && !['compose', 'custom', 'brainstorm', 'outline'].includes(task)) return toast('There is no text to work on yet.');
    const doc = getTarget(editor, 'document').text;
    setMode(target.text ? kind : 'insert');
    void writer.run({ task, text: target.text, context: target.kind === 'document' ? undefined : doc, instruction, dialect: settings?.dialect }, target);
  };

  const submitCustom = () => {
    if (!prompt.trim()) return;
    const target = resolveTarget(editor, choice);
    run(target?.text ? 'custom' : 'compose', target?.text ? 'replace' : 'insert', prompt.trim());
  };

  return (
    <PanelShell icon={<Sparkles className="size-4" />} title="AI Assistant" subtitle="Rewrite, shorten, expand or draft with NVIDIA NIM. Select text first to work on just that part.">
      <TargetPicker editor={editor} value={choice} onChange={setChoice} allowNew />
      <div>
        <div className="mb-1.5 text-xs font-medium text-slate-500 dark:text-slate-400">Quick actions</div>
        <div className="flex flex-wrap gap-1.5">
          {QUICK.map((q) => (
            <button
              key={q.task}
              disabled={writer.running}
              onClick={() => run(q.task, q.kind)}
              className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-[13px] text-slate-700 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-800 disabled:opacity-50 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:hover:border-brand-700 dark:hover:bg-brand-900/40 dark:hover:text-brand-100"
            >
              {q.label}
            </button>
          ))}
        </div>
      </div>
      <div>
        <div className="mb-1.5 text-xs font-medium text-slate-500 dark:text-slate-400">Or tell NimbleWrite what to do</div>
        <div className="relative">
          <textarea
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) submitCustom();
            }}
            rows={3}
            placeholder="e.g. Make this sound like a friendly product update for customers"
            className={cx(inputClass, 'resize-none pr-11')}
          />
          <button onClick={submitCustom} disabled={!prompt.trim() || writer.running} className="absolute bottom-2 right-2 flex size-8 items-center justify-center rounded-lg bg-brand-600 text-white hover:bg-brand-700 disabled:opacity-40" title="Send (Ctrl+Enter)">
            <Send className="size-4" />
          </button>
        </div>
      </div>
      <ResultCard editor={editor} writer={writer} mode={mode} />
    </PanelShell>
  );
}

export function RewritePanel({ ctx, kind }: { ctx: PanelContext; kind: 'paraphrase' | 'humanize' | 'translate' }) {
  const { editor } = ctx;
  const settings = useSettings((s) => s.settings);
  const writer = useWriter();
  const [choice, setChoice] = useState<TargetChoice | 'auto'>('auto');
  const [mode, setMode] = useState<ParaphraseMode>('standard');
  const [voice, setVoice] = useState('');
  const [lang, setLang] = useState(() => localStorage.getItem('nw-lang') || 'French');
  const [source, setSource] = useState('English');

  const go = () => {
    const target = resolveTarget(editor, choice);
    if (!target?.text.trim()) return toast('Select or write some text first.');
    localStorage.setItem('nw-lang', lang);
    void writer.run({ task: kind, text: target.text, mode, instruction: mode === 'custom' ? voice : undefined, targetLanguage: lang, sourceLanguage: source, dialect: settings?.dialect }, target);
  };

  const meta = {
    paraphrase: { icon: <WandSparkles className="size-4" />, title: 'Paraphraser', subtitle: 'Reword text for a different tone, audience or style while keeping the meaning.', cta: 'Paraphrase' },
    humanize: { icon: <Drama className="size-4" />, title: 'Humanizer', subtitle: 'Make AI-assisted writing sound natural: strips stock AI phrasing, varies rhythm, keeps your meaning.', cta: 'Humanize' },
    translate: { icon: <Languages className="size-4" />, title: 'Translate', subtitle: 'Translate text into another language with NVIDIA NIM.', cta: 'Translate' },
  }[kind];

  return (
    <PanelShell icon={meta.icon} title={meta.title} subtitle={meta.subtitle}>
      <TargetPicker editor={editor} value={choice} onChange={setChoice} />
      {kind === 'paraphrase' && (
        <div>
          <div className="mb-1.5 text-xs font-medium text-slate-500 dark:text-slate-400">Mode</div>
          <div className="flex flex-wrap gap-1.5">
            {PARAPHRASE_MODES.map((m) => (
              <Pill key={m.id} selected={mode === m.id} onClick={() => setMode(m.id)} title={m.hint}>{m.label}</Pill>
            ))}
          </div>
          {mode === 'custom' && (
            <input value={voice} onChange={(e) => setVoice(e.target.value)} placeholder="Describe the voice, e.g. witty British tech journalist" className={cx(inputClass, 'mt-2')} />
          )}
        </div>
      )}
      {kind === 'translate' && (
        <div className="grid grid-cols-2 gap-2">
          <Field label="From">
            <select value={source} onChange={(e) => setSource(e.target.value)} className={inputClass}>
              {['English', ...LANGUAGES.filter((l) => !l.startsWith('English'))].map((l) => <option key={l}>{l}</option>)}
            </select>
          </Field>
          <Field label="To">
            <select value={lang} onChange={(e) => setLang(e.target.value)} className={inputClass}>
              {LANGUAGES.map((l) => <option key={l}>{l}</option>)}
            </select>
          </Field>
        </div>
      )}
      <Button variant="primary" className="w-full" onClick={go} loading={writer.running}>{meta.icon} {meta.cta}</Button>
      <ResultCard editor={editor} writer={writer} mode="replace" />
    </PanelShell>
  );
}

const READERS = ['My manager', 'A university professor', 'A potential client', 'A hiring manager', 'A busy executive', 'A general reader'];

export function ReaderPanel({ ctx }: { ctx: PanelContext }) {
  const [reader, setReader] = useState(READERS[0]);
  const [custom, setCustom] = useState('');
  const state = useAsync<ReaderReactions>();
  const run = () => {
    const text = getTarget(ctx.editor, 'document').text;
    if (text.trim().split(/\s+/).length < 15) return toast('Write a bit more first (at least a few sentences).');
    void state.run(() => api.reader(text, custom.trim() || reader));
  };
  const r = state.data;
  return (
    <PanelShell icon={<UserRound className="size-4" />} title="Reader Reactions" subtitle="Predict how a specific reader will react: what they'll take away, what they'll ask, and where they might get confused.">
      <div className="flex flex-wrap gap-1.5">
        {READERS.map((x) => <Pill key={x} selected={!custom && reader === x} onClick={() => { setReader(x); setCustom(''); }}>{x}</Pill>)}
      </div>
      <input value={custom} onChange={(e) => setCustom(e.target.value)} placeholder="Or describe your reader, e.g. a sceptical head teacher" className={inputClass} />
      <Button variant="primary" className="w-full" onClick={run} loading={state.loading}><UserRound className="size-4" /> Predict reactions</Button>
      {state.loading && <p className="text-center text-xs text-slate-500">Reading as {custom || reader.toLowerCase()}… (usually 10–20 s)</p>}
      <ErrorNote message={state.error} />
      {r && (
        <div className="space-y-4 text-sm">
          <div className="rounded-xl bg-slate-50 p-3.5 text-slate-700 dark:bg-slate-800/60 dark:text-slate-200">{r.overall}</div>
          <ListBlock title="Key takeaways" items={r.takeaways} />
          <ListBlock title="Questions they'll have" items={r.questions} />
          {r.confusions.length > 0 && (
            <div>
              <div className="mb-1.5 font-semibold text-slate-800 dark:text-slate-100">Possible confusion</div>
              <div className="space-y-2">
                {r.confusions.map((c, i) => (
                  <div key={i} className="rounded-lg border border-amber-200 bg-amber-50/60 p-3 dark:border-amber-900 dark:bg-amber-950/30">
                    {c.quote && (
                      <button onClick={() => jumpTo(ctx, c.quote)} className="mb-1 block text-left italic text-slate-700 underline decoration-dotted underline-offset-2 hover:text-brand-700 dark:text-slate-200">“{c.quote}”</button>
                    )}
                    <div className="text-slate-700 dark:text-slate-300">{c.issue}</div>
                    {c.suggestion && <div className="mt-1 text-brand-800 dark:text-brand-300">→ {c.suggestion}</div>}
                  </div>
                ))}
              </div>
            </div>
          )}
          <ListBlock title="How to land it better" items={r.suggestions} />
        </div>
      )}
    </PanelShell>
  );
}

function ListBlock({ title, items }: { title: string; items: string[] }) {
  if (!items.length) return null;
  return (
    <div>
      <div className="mb-1.5 font-semibold text-slate-800 dark:text-slate-100">{title}</div>
      <ul className="list-disc space-y-1 pl-5 text-slate-700 dark:text-slate-300">
        {items.map((x, i) => <li key={i}>{x}</li>)}
      </ul>
    </div>
  );
}

function jumpTo(ctx: PanelContext, quote: string) {
  const hit = findText(ctx.editor, quote);
  if (hit) selectRange(ctx.editor, hit.from, hit.to);
  else toast('Could not find that passage. It may have been edited.');
}

const LEVELS = ['Secondary school (GCSE)', 'Sixth form (A-level)', 'Undergraduate', 'Postgraduate', 'Professional'];

export function GraderPanel({ ctx }: { ctx: PanelContext }) {
  const [level, setLevel] = useState('Undergraduate');
  const [rubric, setRubric] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);
  const state = useAsync<GraderResult>();
  const run = () => {
    const text = getTarget(ctx.editor, 'document').text;
    if (text.trim().split(/\s+/).length < 30) return toast('The AI Grader needs at least a paragraph or two.');
    void state.run(() => api.grader(text, rubric, level));
  };
  const upload = async (f: File) => {
    try {
      setRubric((await api.importFile(f)).text);
    } catch (e) {
      toast((e as Error).message, 'error');
    }
  };
  const g = state.data;
  const color = (n: number) => (n >= 75 ? '#18a058' : n >= 55 ? '#d97706' : '#e5484d');
  return (
    <PanelShell icon={<GraduationCap className="size-4" />} title="AI Grader" subtitle="Get rubric-aligned feedback and an estimated grade before you submit.">
      <Field label="Level">
        <select value={level} onChange={(e) => setLevel(e.target.value)} className={inputClass}>{LEVELS.map((l) => <option key={l}>{l}</option>)}</select>
      </Field>
      <Field label="Assignment brief or rubric (optional)">
        <textarea value={rubric} onChange={(e) => setRubric(e.target.value)} rows={5} placeholder="Paste the task, marking criteria or rubric…" className={cx(inputClass, 'resize-y')} />
      </Field>
      <div className="flex gap-2">
        <Button size="sm" variant="ghost" onClick={() => fileRef.current?.click()}><FileUp className="size-4" /> Upload rubric</Button>
        <input ref={fileRef} type="file" accept=".txt,.md,.docx,.html" hidden onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
      </div>
      <Button variant="primary" className="w-full" onClick={run} loading={state.loading}><GraduationCap className="size-4" /> Grade my document</Button>
      {state.loading && <p className="text-center text-xs text-slate-500">Marking against the rubric… (usually 15–25 s)</p>}
      <ErrorNote message={state.error} />
      {g && (
        <div className="space-y-4 text-sm">
          <div className="flex items-center gap-4 rounded-xl bg-slate-50 p-4 dark:bg-slate-800/60">
            <div className="flex size-16 shrink-0 items-center justify-center rounded-2xl text-2xl font-bold text-white" style={{ background: color(g.score) }}>{g.grade}</div>
            <div>
              <div className="font-semibold text-slate-900 dark:text-white">Estimated {g.score}/100</div>
              <p className="mt-0.5 text-slate-600 dark:text-slate-400">{g.summary}</p>
            </div>
          </div>
          <div className="space-y-3">
            {g.criteria.map((c) => (
              <div key={c.name}>
                <div className="mb-1 flex justify-between font-medium text-slate-800 dark:text-slate-100"><span>{c.name}</span><span>{c.score}</span></div>
                <div className="h-1.5 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-800"><div className="h-full rounded-full" style={{ width: `${c.score}%`, background: color(c.score) }} /></div>
                <p className="mt-1 text-slate-600 dark:text-slate-400">{c.feedback}</p>
              </div>
            ))}
          </div>
          <ListBlock title="Strengths" items={g.strengths} />
          <ListBlock title="Improve before submitting" items={g.improvements} />
          <p className="text-[11px] text-slate-400">An AI estimate to guide revision. Your marker's judgement may differ.</p>
        </div>
      )}
    </PanelShell>
  );
}

const STYLE_LABEL: Record<CitationStyle, string> = { apa: 'APA 7', mla: 'MLA 9', chicago: 'Chicago', harvard: 'Harvard' };

export function CitationsPanel({ ctx, searchConfigured, packaged = false }: { ctx: PanelContext; searchConfigured: boolean; packaged?: boolean }) {
  const [tab, setTab] = useState<'cite' | 'finder'>('cite');
  const [input, setInput] = useState('');
  const [style, setStyle] = useState<CitationStyle>(() => (localStorage.getItem('nw-cite-style') as CitationStyle) || 'harvard');
  const lookup = useAsync<{ meta: CitationMeta; formatted: FormattedCitation[] }>();
  const claims = useAsync<{ claims: ClaimToCite[] }>();
  const [hits, setHits] = useState<Record<number, { loading: boolean; items: { title: string; url: string; snippet: string }[] }>>({});
  const [meta, setMeta] = useState<CitationMeta | null>(null);
  const [formatted, setFormatted] = useState<FormattedCitation[]>([]);

  useEffect(() => localStorage.setItem('nw-cite-style', style), [style]);
  useEffect(() => {
    if (lookup.data) {
      setMeta(lookup.data.meta);
      setFormatted(lookup.data.formatted);
    }
  }, [lookup.data]);

  const cite = (value = input) => {
    if (!value.trim()) return;
    setInput(value);
    setTab('cite');
    void lookup.run(() => api.citationLookup(value.trim()));
  };
  const reformat = async (m: CitationMeta) => {
    setMeta(m);
    try {
      setFormatted((await api.citationFormat(m)).formatted);
    } catch (e) {
      toast((e as Error).message, 'error');
    }
  };
  const current = formatted.find((f) => f.style === style);
  const insert = () => {
    if (!current) return;
    const { editor } = ctx;
    const hasRefs = /(^|\n)\s*(references|bibliography|works cited|sources)\s*(\n|$)/i.test(editor.getText({ blockSeparator: '\n' }));
    const end = editor.state.doc.content.size;
    editor.chain().focus().insertContentAt(end, `${hasRefs ? '' : `<h2>${style === 'mla' ? 'Works Cited' : 'References'}</h2>`}<p>${DOMPurify.sanitize(current.html)}</p>`).run();
    toast('Citation added to your references', 'success');
  };
  const findSources = async (i: number, query: string) => {
    if (!searchConfigured) {
      window.open(`https://scholar.google.com/scholar?q=${encodeURIComponent(query)}`, '_blank', 'noopener');
      return;
    }
    setHits((h) => ({ ...h, [i]: { loading: true, items: [] } }));
    try {
      const r = await api.citationSearch(query);
      setHits((h) => ({ ...h, [i]: { loading: false, items: r.hits } }));
    } catch (e) {
      setHits((h) => ({ ...h, [i]: { loading: false, items: [] } }));
      toast((e as Error).message, 'error');
    }
  };

  return (
    <PanelShell icon={<Quote className="size-4" />} title="Citations" subtitle="Generate APA, MLA, Chicago and Harvard citations, and find claims that need a source.">
      <div className="flex rounded-lg bg-slate-100 p-0.5 dark:bg-slate-800">
        {(['cite', 'finder'] as const).map((t) => (
          <button key={t} onClick={() => setTab(t)} className={cx('flex-1 rounded-md py-1.5 text-xs', tab === t ? 'bg-white font-semibold shadow-sm dark:bg-slate-950' : 'text-slate-600 dark:text-slate-400')}>
            {t === 'cite' ? 'Cite a source' : 'Citation Finder'}
          </button>
        ))}
      </div>
      {tab === 'cite' ? (
        <>
          <div className="flex gap-2">
            <input value={input} onChange={(e) => setInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && cite()} placeholder="Paste a URL or DOI" className={inputClass} />
            <Button variant="primary" onClick={() => cite()} loading={lookup.loading}>Cite</Button>
          </div>
          <ErrorNote message={lookup.error} />
          {meta && (
            <div className="space-y-3">
              <div className="flex gap-1">
                {(Object.keys(STYLE_LABEL) as CitationStyle[]).map((s) => <Pill key={s} selected={style === s} onClick={() => setStyle(s)}>{STYLE_LABEL[s]}</Pill>)}
              </div>
              {current && (
                // Citation HTML is built server-side from escaped segments (only <i> tags are added),
                // and sanitised again here with DOMPurify as defence in depth.
                <div className="rounded-xl border border-slate-200 bg-white p-3.5 text-sm leading-relaxed dark:border-slate-700 dark:bg-slate-900" dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(current.html) }} />
              )}
              <div className="flex gap-2">
                <Button size="sm" variant="primary" onClick={insert}>Add to references</Button>
                <Button size="sm" variant="ghost" onClick={() => current && navigator.clipboard.writeText(current.text).then(() => toast('Copied'))}>Copy</Button>
              </div>
              <details className="rounded-lg border border-slate-200 p-3 text-sm dark:border-slate-700">
                <summary className="cursor-pointer text-xs font-medium text-slate-500">Edit details</summary>
                <div className="mt-3 space-y-2">
                  <Field label="Title"><input className={inputClass} value={meta.title} onChange={(e) => void reformat({ ...meta, title: e.target.value })} /></Field>
                  <Field label="Authors (one per line, “Family, Given”)">
                    <textarea
                      key={lookup.data?.meta.url ?? lookup.data?.meta.doi ?? 'manual'}
                      rows={3}
                      className={inputClass}
                      defaultValue={meta.authors.map((a) => a.literal ?? `${a.family ?? ''}, ${a.given ?? ''}`).join('\n')}
                      onBlur={(e) => void reformat({
                        ...meta,
                        authors: e.target.value.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => (l.includes(',') ? { family: l.split(',')[0].trim(), given: l.split(',')[1]?.trim() } : { literal: l })),
                      })}
                    />
                  </Field>
                  <div className="grid grid-cols-2 gap-2">
                    <Field label="Published (YYYY-MM-DD)"><input className={inputClass} value={meta.published ?? ''} onChange={(e) => void reformat({ ...meta, published: e.target.value })} /></Field>
                    <Field label="Site / publisher"><input className={inputClass} value={meta.siteName ?? meta.publisher ?? ''} onChange={(e) => void reformat({ ...meta, siteName: e.target.value })} /></Field>
                  </div>
                  <Field label="Type">
                    <select className={inputClass} value={meta.type} onChange={(e) => void reformat({ ...meta, type: e.target.value as CitationMeta['type'] })}>
                      <option value="webpage">Web page</option><option value="article">Journal article</option><option value="book">Book</option><option value="report">Report</option>
                    </select>
                  </Field>
                </div>
              </details>
            </div>
          )}
        </>
      ) : (
        <>
          <Button variant="primary" className="w-full" loading={claims.loading} onClick={() => void claims.run(() => api.claims(getTarget(ctx.editor, 'document').text))}>
            <Search className="size-4" /> Find claims that need sources
          </Button>
          {!searchConfigured && (
            <p className="text-[11px] text-slate-400">
              {packaged ? '“Search” opens Google Scholar in your browser.' : 'Tip: add BRAVE_API_KEY to .env to search for sources inside NimbleWrite. Without it, “Search” opens Google Scholar.'}
            </p>
          )}
          <ErrorNote message={claims.error} />
          {claims.data && claims.data.claims.length === 0 && <p className="text-sm text-slate-500">No claims needing citations were found.</p>}
          <div className="space-y-2.5">
            {claims.data?.claims.map((c, i) => (
              <div key={i} className="rounded-xl border border-slate-200 p-3 text-sm dark:border-slate-700">
                <div className="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide" style={{ color: c.strength === 'needs_citation' ? '#e5484d' : '#d97706' }}>
                  {c.strength === 'needs_citation' ? 'Needs a citation' : 'Citation recommended'}
                </div>
                <button onClick={() => jumpTo(ctx, c.claim)} className="text-left font-medium text-slate-800 hover:text-brand-700 dark:text-slate-100">“{c.claim}”</button>
                <p className="mt-1 text-slate-600 dark:text-slate-400">{c.reason}</p>
                <div className="mt-2 flex gap-2">
                  <Button size="sm" variant="secondary" onClick={() => void findSources(i, c.query)}>
                    {searchConfigured ? <Search className="size-3.5" /> : <ExternalLink className="size-3.5" />} {searchConfigured ? 'Find sources' : 'Search Scholar'}
                  </Button>
                </div>
                {hits[i]?.loading && <Spinner className="mt-2" />}
                {hits[i]?.items.map((h) => (
                  <div key={h.url} className="mt-2 rounded-lg bg-slate-50 p-2 dark:bg-slate-800/60">
                    <a href={h.url} target="_blank" rel="noreferrer" className="font-medium text-brand-700 hover:underline dark:text-brand-300">{h.title}</a>
                    <p className="line-clamp-2 text-xs text-slate-500">{h.snippet}</p>
                    <button onClick={() => cite(h.url)} className="mt-1 text-xs font-medium text-brand-700 hover:underline dark:text-brand-300">Cite this source →</button>
                  </div>
                ))}
              </div>
            ))}
          </div>
        </>
      )}
    </PanelShell>
  );
}

export function DetectorPanel({ ctx, onHumanize }: { ctx: PanelContext; onHumanize: () => void }) {
  const state = useAsync<AiDetectResult>();
  const r = state.data;
  const color = (n: number) => (n >= 60 ? '#e5484d' : n >= 35 ? '#d97706' : '#18a058');
  return (
    <PanelShell icon={<ScanSearch className="size-4" />} title="AI Detector" subtitle="Estimate how much of your text reads as AI-generated, combining an NVIDIA NIM model's judgement with writing statistics.">
      <Button variant="primary" className="w-full" loading={state.loading} onClick={() => void state.run(() => api.detect(getTarget(ctx.editor, 'document').text))}>
        <ScanSearch className="size-4" /> Check for AI text
      </Button>
      <ErrorNote message={state.error} />
      {r && (
        <div className="space-y-4 text-sm">
          <div className="rounded-xl bg-slate-50 p-4 text-center dark:bg-slate-800/60">
            <div className="text-4xl font-bold" style={{ color: color(r.score) }}>{r.score}%</div>
            <div className="mt-1 text-slate-600 dark:text-slate-300">of your text appears AI-generated</div>
            <p className="mt-2 font-medium text-slate-800 dark:text-slate-100">{r.verdict}</p>
          </div>
          <dl className="space-y-1 text-xs">
            {r.signals.map((s) => <div key={s.label} className="flex justify-between gap-3"><dt className="text-slate-500">{s.label}</dt><dd className="text-right text-slate-700 dark:text-slate-300">{s.value}</dd></div>)}
          </dl>
          <div className="space-y-2">
            {r.passages.map((p, i) => (
              <div key={i} className="rounded-lg border p-3" style={{ borderColor: `${color(p.score)}55` }}>
                <div className="mb-1 flex items-center justify-between">
                  <span className="text-xs font-semibold" style={{ color: color(p.score) }}>{p.score}% AI-like</span>
                  <span className="flex gap-2">
                    <button onClick={() => jumpTo(ctx, p.text)} className="text-xs text-brand-700 hover:underline dark:text-brand-300">Show</button>
                    {p.score >= 50 && <button onClick={() => { jumpTo(ctx, p.text); const hit = findText(ctx.editor, p.text); if (hit) selectRange(ctx.editor, hit.from, Math.min(hit.from + p.text.length, ctx.editor.state.doc.content.size)); onHumanize(); }} className="text-xs text-brand-700 hover:underline dark:text-brand-300">Humanize</button>}
                  </span>
                </div>
                <p className="line-clamp-3 text-slate-600 dark:text-slate-400">{p.text}</p>
                {p.reasons.length > 0 && <ul className="mt-1.5 list-disc pl-4 text-xs text-slate-500">{p.reasons.map((x) => <li key={x}>{x}</li>)}</ul>}
              </div>
            ))}
          </div>
          <p className="text-[11px] leading-snug text-slate-400">No AI detector is reliable enough to prove who wrote something. Treat this as a signal for editing, never as evidence.</p>
        </div>
      )}
    </PanelShell>
  );
}

export function PlagiarismPanel({ ctx, searchConfigured, packaged = false }: { ctx: PanelContext; searchConfigured: boolean; packaged?: boolean }) {
  const [includeDocs, setIncludeDocs] = useState(true);
  const [web, setWeb] = useState(searchConfigured);
  const [pasted, setPasted] = useState('');
  const [urls, setUrls] = useState('');
  const state = useAsync<PlagiarismResult>();
  const run = () => {
    const text = getTarget(ctx.editor, 'document').text;
    if (!text.trim()) return toast('There is no text to check yet.');
    const sources = [
      ...(pasted.trim() ? [{ title: 'Pasted source', text: pasted }] : []),
      ...urls.split(/\s+/).filter((u) => /^https?:\/\//i.test(u)).slice(0, 8).map((url) => ({ url })),
    ];
    void state.run(() => api.plagiarism({ text, docId: ctx.docId, includeDocs, web, sources }));
  };
  const r = state.data;
  const color = (n: number) => (n >= 30 ? '#e5484d' : n >= 10 ? '#d97706' : '#18a058');
  return (
    <PanelShell icon={<ShieldCheck className="size-4" />} title="Plagiarism Checker" subtitle="Find passages that match other sources, so you can quote and cite them properly.">
      <div className="space-y-2 text-sm">
        <label className="flex items-center gap-2"><input type="checkbox" checked={includeDocs} onChange={(e) => setIncludeDocs(e.target.checked)} className="size-4 accent-brand-600" /> Compare with my other documents</label>
        <label className={cx('flex items-center gap-2', !searchConfigured && 'opacity-50')}>
          <input type="checkbox" disabled={!searchConfigured} checked={web && searchConfigured} onChange={(e) => setWeb(e.target.checked)} className="size-4 accent-brand-600" /> Search the web
        </label>
        {!searchConfigured && (
          <p className="text-[11px] text-slate-400">
            {packaged ? 'Web search isn’t set up in this version: add sources below to compare against.' : 'Web search needs a Brave Search API key (BRAVE_API_KEY in .env). Without it, add sources below.'}
          </p>
        )}
      </div>
      <Field label="Source URLs to compare against (optional)"><textarea rows={2} value={urls} onChange={(e) => setUrls(e.target.value)} placeholder="https://… one per line" className={inputClass} /></Field>
      <Field label="Or paste source text (optional)"><textarea rows={3} value={pasted} onChange={(e) => setPasted(e.target.value)} className={inputClass} /></Field>
      <Button variant="primary" className="w-full" onClick={run} loading={state.loading}><ShieldCheck className="size-4" /> Check originality</Button>
      <ErrorNote message={state.error} />
      {r && (
        <div className="space-y-3 text-sm">
          <div className="rounded-xl bg-slate-50 p-4 text-center dark:bg-slate-800/60">
            <div className="text-4xl font-bold" style={{ color: color(r.overall) }}>{r.overall}%</div>
            <div className="mt-1 text-slate-600 dark:text-slate-300">of your text matches the {r.checkedSources} source{r.checkedSources === 1 ? '' : 's'} checked</div>
            {r.webSearch === 'not_configured' && <p className="mt-1 text-xs text-slate-400">Web search not configured, so only your documents and supplied sources were checked.</p>}
          </div>
          {r.note && <p className="text-xs text-amber-700 dark:text-amber-400">{r.note}</p>}
          {r.sources.length === 0 && <p className="text-center text-slate-500">No matching passages found. 🎉</p>}
          {r.sources.map((s, i) => (
            <div key={i} className="rounded-lg border border-slate-200 p-3 dark:border-slate-700">
              <div className="flex items-center justify-between gap-2">
                <span className="truncate font-medium text-slate-800 dark:text-slate-100" title={s.title}>{s.kind === 'doc' ? '📄 ' : s.kind === 'web' ? '🌐 ' : '📎 '}{s.title}</span>
                <span className="shrink-0 text-xs font-semibold" style={{ color: color(s.similarity) }}>{s.similarity}%</span>
              </div>
              {s.url && <a href={s.url} target="_blank" rel="noreferrer" className="block truncate text-xs text-brand-700 hover:underline dark:text-brand-300">{s.url}</a>}
              <div className="mt-2 space-y-1.5">
                {s.passages.slice(0, 4).map((p, j) => (
                  <button key={j} onClick={() => jumpTo(ctx, p.text)} className="block w-full rounded bg-red-50 px-2 py-1 text-left text-xs text-red-900 hover:bg-red-100 dark:bg-red-950/40 dark:text-red-200">“{p.text.length > 160 ? `${p.text.slice(0, 160)}…` : p.text}”</button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </PanelShell>
  );
}

export const PANELS = [
  { id: 'suggestions', label: 'Review suggestions', icon: BookOpenCheck },
  { id: 'assistant', label: 'AI Assistant', icon: Sparkles },
  { id: 'paraphrase', label: 'Paraphraser', icon: WandSparkles },
  { id: 'humanize', label: 'Humanizer', icon: Drama },
  { id: 'reader', label: 'Reader Reactions', icon: UserRound },
  { id: 'grader', label: 'AI Grader', icon: GraduationCap },
  { id: 'citations', label: 'Citations', icon: Quote },
  { id: 'detector', label: 'AI Detector', icon: ScanSearch },
  { id: 'plagiarism', label: 'Plagiarism Checker', icon: ShieldCheck },
  { id: 'translate', label: 'Translate', icon: Languages },
] as const;

export type PanelId = (typeof PANELS)[number]['id'];
