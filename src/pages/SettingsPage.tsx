// Settings: language, suggestions, default goals, personal dictionary, style guide, snippets,
// NVIDIA NIM model choice (with a live test) and appearance.
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { ArrowLeft, BookA, Cpu, Keyboard, Monitor, Moon, Palette, Plus, Ruler, Search, SpellCheck, Sun, Target, Trash2, X } from 'lucide-react';
import type { Category, ModelSettings, Settings, Snippet, StyleRule } from '../../shared/types';
import { CATEGORIES, DIALECTS } from '../../shared/types';
import { CATEGORY_META } from '../../shared/meta';
import { api } from '../api';
import { navigate } from '../router';
import { toast, useSettings } from '../store';
import { Button, cx, IconButton, Logo, Spinner, Toggle } from '../components/ui';
import { GoalsPicker } from '../editor/Dialogs';
import { inputClass } from '../editor/panels/common';

const SECTIONS = [
  { id: 'language', label: 'Language', icon: SpellCheck },
  { id: 'suggestions', label: 'Suggestions', icon: Target },
  { id: 'dictionary', label: 'Personal dictionary', icon: BookA },
  { id: 'style', label: 'Style guide', icon: Ruler },
  { id: 'snippets', label: 'Snippets', icon: Keyboard },
  { id: 'models', label: 'AI models', icon: Cpu },
  { id: 'appearance', label: 'Appearance', icon: Palette },
] as const;

const uid = () => Math.random().toString(36).slice(2, 10);

export function SettingsPage() {
  const settings = useSettings((s) => s.settings);
  const update = useSettings((s) => s.update);
  const [section, setSection] = useState<(typeof SECTIONS)[number]['id']>('language');
  if (!settings) return <div className="flex h-full items-center justify-center"><Spinner className="size-6" /></div>;
  return (
    <div className="flex h-full flex-col">
      <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 px-4 dark:border-slate-800">
        <IconButton label="Back to documents" onClick={() => navigate('/')}><ArrowLeft className="size-5" /></IconButton>
        <Logo compact />
        <h1 className="text-lg font-semibold text-slate-900 dark:text-white">Settings</h1>
      </header>
      <div className="flex min-h-0 flex-1">
        <nav className="hidden w-56 shrink-0 space-y-1 border-r border-slate-200 p-3 sm:block dark:border-slate-800">
          {SECTIONS.map((s) => (
            <button
              key={s.id}
              onClick={() => setSection(s.id)}
              className={cx('flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-left text-sm', section === s.id ? 'bg-brand-50 font-semibold text-brand-800 dark:bg-brand-900/40 dark:text-brand-200' : 'text-slate-600 hover:bg-slate-50 dark:text-slate-400 dark:hover:bg-slate-900')}
            >
              <s.icon className="size-4" /> {s.label}
            </button>
          ))}
        </nav>
        <main className="nw-scroll min-w-0 flex-1 overflow-y-auto">
          <div className="mx-auto max-w-3xl px-5 py-6 sm:px-8">
            <select value={section} onChange={(e) => setSection(e.target.value as typeof section)} className={cx(inputClass, 'mb-5 sm:hidden')}>
              {SECTIONS.map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
            </select>
            {section === 'language' && <LanguageSection settings={settings} update={update} />}
            {section === 'suggestions' && <SuggestionsSection settings={settings} update={update} />}
            {section === 'dictionary' && <DictionarySection settings={settings} update={update} />}
            {section === 'style' && <StyleSection settings={settings} update={update} />}
            {section === 'snippets' && <SnippetsSection settings={settings} update={update} />}
            {section === 'models' && <ModelsSection settings={settings} update={update} />}
            {section === 'appearance' && <AppearanceSection settings={settings} update={update} />}
          </div>
        </main>
      </div>
    </div>
  );
}

type SectionProps = { settings: Settings; update: (p: Partial<Settings>) => Promise<void> };

function Card({ title, description, children }: { title: string; description?: ReactNode; children: ReactNode }) {
  return (
    <section className="mb-6 rounded-2xl border border-slate-200 bg-white p-5 dark:border-slate-800 dark:bg-slate-900">
      <h2 className="font-semibold text-slate-900 dark:text-white">{title}</h2>
      {description && <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">{description}</p>}
      <div className="mt-4">{children}</div>
    </section>
  );
}

function Row({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 py-2.5">
      <div>
        <div className="text-sm font-medium text-slate-800 dark:text-slate-100">{label}</div>
        {hint && <div className="text-xs text-slate-500 dark:text-slate-400">{hint}</div>}
      </div>
      {children}
    </div>
  );
}

function LanguageSection({ settings, update }: SectionProps) {
  return (
    <Card title="English variant" description="NimbleWrite checks spelling and suggests vocabulary for the variant you choose, and never ‘corrects’ you into another one.">
      <div className="grid gap-2 sm:grid-cols-2">
        {DIALECTS.map((d) => (
          <label key={d.id} className={cx('flex cursor-pointer items-center gap-3 rounded-xl border px-4 py-3 text-sm', settings.dialect === d.id ? 'border-brand-500 bg-brand-50 dark:bg-brand-950/40' : 'border-slate-200 hover:bg-slate-50 dark:border-slate-700 dark:hover:bg-slate-800')}>
            <input type="radio" name="dialect" checked={settings.dialect === d.id} onChange={() => void update({ dialect: d.id })} className="accent-brand-600" />
            <span className="font-medium text-slate-800 dark:text-slate-100">{d.label}</span>
            <span className="ml-auto text-xs text-slate-400">{d.id}</span>
          </label>
        ))}
      </div>
    </Card>
  );
}

function SuggestionsSection({ settings, update }: SectionProps) {
  const setCat = (c: Category, v: boolean) => {
    const next = { ...settings.categories, [c]: v };
    if (!Object.values(next).some(Boolean)) return toast('Keep at least one suggestion category on.');
    void update({ categories: next });
  };
  return (
    <>
      <Card title="Suggestion categories" description="Choose which kinds of suggestions NimbleWrite shows.">
        <div className="divide-y divide-slate-100 dark:divide-slate-800">
          {CATEGORIES.map((c) => (
            <Row key={c} label={CATEGORY_META[c].label} hint={CATEGORY_META[c].description}>
              <span className="flex items-center gap-3">
                <span className="size-2.5 rounded-full" style={{ background: CATEGORY_META[c].color }} />
                <Toggle label={CATEGORY_META[c].label} checked={settings.categories[c]} onChange={(v) => setCat(c, v)} />
              </span>
            </Row>
          ))}
          <Row label="Instant local checks" hint="Repeated words, spacing and your English variant’s spellings, checked instantly in the browser alongside the AI.">
            <Toggle label="Instant local checks" checked={settings.localRules} onChange={(v) => void update({ localRules: v })} />
          </Row>
        </div>
      </Card>
      <Card title="Default goals for new documents" description="Goals tailor suggestions to your audience, formality, domain and intent. You can change them per document.">
        <GoalsPicker goals={settings.defaultGoals} onChange={(g) => void update({ defaultGoals: g })} />
        <div className="mt-5 border-t border-slate-100 pt-3 dark:border-slate-800">
          <Row label="Show Set Goals for new documents">
            <Toggle label="Show Set Goals for new documents" checked={settings.showGoalsOnNewDoc} onChange={(v) => void update({ showGoalsOnNewDoc: v })} />
          </Row>
        </div>
      </Card>
    </>
  );
}

function DictionarySection({ settings, update }: SectionProps) {
  const [word, setWord] = useState('');
  const [filter, setFilter] = useState('');
  const add = () => {
    const w = word.trim();
    if (!w) return;
    if (w.length > 80) return toast('Words must be 80 characters or fewer.');
    if (settings.dictionary.some((x) => x.toLowerCase() === w.toLowerCase())) return toast('That word is already in your dictionary.');
    void update({ dictionary: [...settings.dictionary, w].sort((a, b) => a.localeCompare(b)) });
    setWord('');
  };
  const list = settings.dictionary.filter((w) => w.toLowerCase().includes(filter.toLowerCase()));
  return (
    <Card title="Personal dictionary" description="Words here are never flagged as misspellings: names, brands, jargon. Words you add from a suggestion card land here too.">
      <div className="flex gap-2">
        <input value={word} onChange={(e) => setWord(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && add()} placeholder="Add a word" className={inputClass} />
        <Button variant="primary" onClick={add}><Plus className="size-4" /> Add</Button>
      </div>
      {settings.dictionary.length > 8 && (
        <div className="relative mt-3">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
          <input value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Filter" className={cx(inputClass, 'pl-9')} />
        </div>
      )}
      <div className="mt-4 flex flex-wrap gap-2">
        {list.length === 0 && <p className="text-sm text-slate-400">{settings.dictionary.length ? 'No matches.' : 'Your dictionary is empty.'}</p>}
        {list.map((w) => (
          <span key={w} className="inline-flex items-center gap-1 rounded-full bg-slate-100 py-1 pl-3 pr-1 text-sm text-slate-700 dark:bg-slate-800 dark:text-slate-200">
            {w}
            <button onClick={() => void update({ dictionary: settings.dictionary.filter((x) => x !== w) })} className="rounded-full p-0.5 text-slate-400 hover:bg-slate-200 hover:text-slate-700 dark:hover:bg-slate-700" aria-label={`Remove ${w}`}>
              <X className="size-3.5" />
            </button>
          </span>
        ))}
      </div>
    </Card>
  );
}

function StyleSection({ settings, update }: SectionProps) {
  const [rules, setRules] = useState<StyleRule[]>(settings.styleRules);
  const dirty = JSON.stringify(rules) !== JSON.stringify(settings.styleRules);
  const set = (i: number, patch: Partial<StyleRule>) => setRules((r) => r.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  const save = () => {
    const clean = rules.filter((r) => r.find.trim()).map((r) => ({ ...r, find: r.find.trim(), replace: r.replace.trim() }));
    void update({ styleRules: clean }).then(() => toast('Style guide saved', 'success'));
    setRules(clean);
  };
  return (
    <Card title="Style guide" description="House rules NimbleWrite enforces as you type, e.g. ‘e-mail’ → ‘email’, or ‘utilise’ → ‘use’. Leave ‘Replace with’ empty to flag a term you want to avoid.">
      <div className="space-y-2">
        {rules.length === 0 && <p className="text-sm text-slate-400">No rules yet.</p>}
        {rules.map((r, i) => (
          <div key={r.id} className="grid grid-cols-[1fr_1fr_auto] gap-2 rounded-xl border border-slate-200 p-2.5 sm:grid-cols-[1fr_1fr_1.3fr_auto_auto] dark:border-slate-700">
            <input value={r.find} onChange={(e) => set(i, { find: e.target.value })} placeholder="Find" maxLength={200} className={inputClass} />
            <input value={r.replace} onChange={(e) => set(i, { replace: e.target.value })} placeholder="Replace with" maxLength={200} className={inputClass} />
            <input value={r.note ?? ''} onChange={(e) => set(i, { note: e.target.value })} placeholder="Why (shown on the card)" maxLength={300} className={cx(inputClass, 'col-span-2 sm:col-span-1')} />
            <label className="flex items-center gap-1.5 whitespace-nowrap text-xs text-slate-500" title="Match case">
              <input type="checkbox" checked={!!r.caseSensitive} onChange={(e) => set(i, { caseSensitive: e.target.checked })} className="accent-brand-600" /> Aa
            </label>
            <IconButton label="Delete rule" onClick={() => setRules((x) => x.filter((_, j) => j !== i))}><Trash2 className="size-4" /></IconButton>
          </div>
        ))}
      </div>
      <div className="mt-3 flex gap-2">
        <Button onClick={() => setRules((r) => [...r, { id: uid(), find: '', replace: '' }])}><Plus className="size-4" /> Add rule</Button>
        <Button variant="primary" disabled={!dirty} onClick={save}>Save style guide</Button>
      </div>
    </Card>
  );
}

function SnippetsSection({ settings, update }: SectionProps) {
  const [items, setItems] = useState<Snippet[]>(settings.snippets);
  const dirty = JSON.stringify(items) !== JSON.stringify(settings.snippets);
  const set = (i: number, patch: Partial<Snippet>) => setItems((r) => r.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  const save = () => {
    const clean = items.filter((s) => s.trigger.trim() && s.text.trim()).map((s) => ({ ...s, trigger: s.trigger.trim().replace(/^\\/, '') }));
    const bad = clean.find((s) => !/^[\w-]{1,30}$/.test(s.trigger));
    if (bad) return toast(`“${bad.trigger}” isn't a valid shortcut. Use letters, numbers, - or _ (max 30).`, 'error');
    const dup = clean.find((s, i) => clean.findIndex((x) => x.trigger.toLowerCase() === s.trigger.toLowerCase()) !== i);
    if (dup) return toast(`The shortcut “${dup.trigger}” is used twice.`, 'error');
    void update({ snippets: clean }).then(() => toast('Snippets saved', 'success'));
    setItems(clean);
  };
  return (
    <Card title="Snippets" description={<>Type a backslash and a shortcut, then press Space, Tab or Enter to insert the text. For example <code className="rounded bg-slate-100 px-1 dark:bg-slate-800">\sig</code>.</>}>
      <div className="space-y-2">
        {items.map((s, i) => (
          <div key={s.id} className="grid gap-2 rounded-xl border border-slate-200 p-2.5 sm:grid-cols-[180px_1fr_auto] dark:border-slate-700">
            <div className="relative">
              <span className="pointer-events-none absolute left-3 top-2 text-sm text-slate-400">\</span>
              <input value={s.trigger} onChange={(e) => set(i, { trigger: e.target.value })} placeholder="shortcut" maxLength={31} className={cx(inputClass, 'pl-6 font-mono')} />
            </div>
            <textarea value={s.text} onChange={(e) => set(i, { text: e.target.value })} rows={2} maxLength={5000} placeholder="Text to insert" className={cx(inputClass, 'resize-y')} />
            <IconButton label="Delete snippet" onClick={() => setItems((x) => x.filter((_, j) => j !== i))}><Trash2 className="size-4" /></IconButton>
          </div>
        ))}
      </div>
      <div className="mt-3 flex gap-2">
        <Button onClick={() => setItems((r) => [...r, { id: uid(), trigger: '', text: '' }])}><Plus className="size-4" /> Add snippet</Button>
        <Button variant="primary" disabled={!dirty} onClick={save}>Save snippets</Button>
      </div>
    </Card>
  );
}

const MODEL_ROLES: { key: keyof ModelSettings; label: string; hint: string }[] = [
  { key: 'check', label: 'Proofreading, tone, synonyms and AI detection', hint: 'Runs constantly as you type, so speed matters most. Thinking is switched off.' },
  { key: 'write', label: 'Generative writing and agents', hint: 'Rewrites, paraphrasing, humanizing, Reader Reactions, AI Grader and Citation Finder.' },
  { key: 'translate', label: 'Translation', hint: 'Riva Translate is faster; Nemotron Ultra is more fluent.' },
];

function ModelsSection({ settings, update }: SectionProps) {
  const [list, setList] = useState<{ models: string[]; recommended: Record<keyof ModelSettings, string[]> } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tests, setTests] = useState<Record<string, { loading: boolean; result?: string; ok?: boolean }>>({});
  useEffect(() => {
    api.models().then(setList).catch((e: Error) => setError(e.message));
  }, []);
  const test = async (key: keyof ModelSettings) => {
    const model = settings.models[key];
    setTests((t) => ({ ...t, [key]: { loading: true } }));
    try {
      const r = await api.testModel(model);
      setTests((t) => ({ ...t, [key]: { loading: false, ok: r.ok, result: r.ok ? `Responded in ${((r.ms ?? 0) / 1000).toFixed(1)} s` : r.error } }));
    } catch (e) {
      setTests((t) => ({ ...t, [key]: { loading: false, ok: false, result: (e as Error).message } }));
    }
  };
  return (
    <Card title="NVIDIA NIM models" description="All AI runs through NVIDIA NIM (integrate.api.nvidia.com). Your API key stays on the NimbleWrite server and is never sent to the browser.">
      {error && <p className="mb-3 text-sm text-red-600">{error}</p>}
      <div className="space-y-5">
        {MODEL_ROLES.map((role) => (
          <ModelPicker
            key={role.key}
            label={role.label}
            hint={role.hint}
            value={settings.models[role.key]}
            recommended={list?.recommended[role.key] ?? []}
            all={list?.models ?? []}
            onChange={(v) => void update({ models: { ...settings.models, [role.key]: v } })}
            onTest={() => void test(role.key)}
            test={tests[role.key]}
          />
        ))}
      </div>
    </Card>
  );
}

function ModelPicker({ label, hint, value, recommended, all, onChange, onTest, test }: {
  label: string; hint: string; value: string; recommended: string[]; all: string[];
  onChange: (v: string) => void; onTest: () => void; test?: { loading: boolean; result?: string; ok?: boolean };
}) {
  const others = useMemo(() => all.filter((m) => !recommended.includes(m)).sort(), [all, recommended]);
  const options = [...new Set([value, ...recommended])];
  return (
    <div>
      <div className="text-sm font-medium text-slate-800 dark:text-slate-100">{label}</div>
      <div className="mb-2 text-xs text-slate-500 dark:text-slate-400">{hint}</div>
      <div className="flex flex-wrap gap-2">
        <select value={value} onChange={(e) => onChange(e.target.value)} className={cx(inputClass, 'max-w-md flex-1 font-mono text-[13px]')}>
          <optgroup label="Recommended">
            {options.map((m) => <option key={m} value={m}>{m}</option>)}
          </optgroup>
          {others.length > 0 && (
            <optgroup label="All NIM chat models">
              {others.map((m) => <option key={m} value={m}>{m}</option>)}
            </optgroup>
          )}
        </select>
        <Button onClick={onTest} loading={test?.loading}>Test</Button>
      </div>
      {test?.result && <p className={cx('mt-1.5 text-xs', test.ok ? 'text-brand-700 dark:text-brand-300' : 'text-red-600')}>{test.ok ? '✓ ' : '✕ '}{test.result}</p>}
    </div>
  );
}

function AppearanceSection({ settings, update }: SectionProps) {
  const opts = [
    { id: 'light' as const, label: 'Light', icon: Sun },
    { id: 'dark' as const, label: 'Dark', icon: Moon },
    { id: 'system' as const, label: 'System', icon: Monitor },
  ];
  return (
    <Card title="Theme">
      <div className="grid grid-cols-3 gap-2">
        {opts.map((o) => (
          <button key={o.id} onClick={() => void update({ theme: o.id })} className={cx('flex flex-col items-center gap-2 rounded-xl border px-4 py-4 text-sm', settings.theme === o.id ? 'border-brand-500 bg-brand-50 font-semibold text-brand-800 dark:bg-brand-950/40 dark:text-brand-200' : 'border-slate-200 text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800')}>
            <o.icon className="size-5" /> {o.label}
          </button>
        ))}
      </div>
    </Card>
  );
}
