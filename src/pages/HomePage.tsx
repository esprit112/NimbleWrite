// Documents home: list, search, create, upload (incl. drag & drop), trash and weekly insights.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ArchiveRestore, ChartColumn, Copy, EllipsisVertical, FilePlus, FileText, FileUp, Inbox, Search, Settings as SettingsIcon, Trash2,
} from 'lucide-react';
import type { DocSummary, HealthResponse, InsightsResponse } from '../../shared/types';
import { CATEGORIES } from '../../shared/types';
import { CATEGORY_META } from '../../shared/meta';
import { api } from '../api';
import { navigate } from '../router';
import { toast, useSettings } from '../store';
import { Button, cx, EmptyState, Logo, Menu, MenuItem, ScoreRing, Spinner } from '../components/ui';

function ago(ts: number): string {
  const s = Math.round((Date.now() - ts) / 1000);
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  if (s < 86_400) return `${Math.floor(s / 3600)} hr ago`;
  if (s < 7 * 86_400) return `${Math.floor(s / 86_400)} day${s < 2 * 86_400 ? '' : 's'} ago`;
  return new Date(ts).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}

export function HomePage({ trash, health }: { trash: boolean; health: HealthResponse | null }) {
  const settings = useSettings((s) => s.settings);
  const [docs, setDocs] = useState<DocSummary[] | null>(null);
  const [query, setQuery] = useState('');
  const [insights, setInsights] = useState<InsightsResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [dragging, setDragging] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(() => {
    api.listDocs(trash).then(setDocs).catch((e: Error) => toast(e.message, 'error'));
  }, [trash]);
  useEffect(() => {
    setDocs(null);
    load();
  }, [load]);
  useEffect(() => {
    api.insights().then(setInsights).catch(() => undefined);
  }, []);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (docs ?? []).filter((d) => !q || d.title.toLowerCase().includes(q) || d.snippet.toLowerCase().includes(q));
  }, [docs, query]);

  const create = async () => {
    setBusy(true);
    try {
      const d = await api.createDoc({ goals: settings?.defaultGoals });
      navigate(`/doc/${d.id}`);
    } catch (e) {
      toast((e as Error).message, 'error');
      setBusy(false);
    }
  };

  const upload = async (files: FileList | File[]) => {
    const list = [...files].slice(0, 10);
    if (!list.length) return;
    setBusy(true);
    let last: string | null = null;
    for (const f of list) {
      try {
        const r = await api.importFile(f);
        const d = await api.createDoc({ title: r.title, html: r.html, text: r.text, goals: settings?.defaultGoals });
        last = d.id;
      } catch (e) {
        toast(`${f.name}: ${(e as Error).message}`, 'error');
      }
    }
    setBusy(false);
    if (list.length === 1 && last) navigate(`/doc/${last}`);
    else {
      toast(`Imported ${list.length} files`, 'success');
      load();
    }
  };

  const act = async (fn: () => Promise<unknown>, message: string) => {
    try {
      await fn();
      toast(message, 'success');
      load();
    } catch (e) {
      toast((e as Error).message, 'error');
    }
  };

  return (
    <div
      className="flex h-full"
      onDragOver={(e) => {
        if (trash) return;
        e.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(e) => e.currentTarget === e.target && setDragging(false)}
      onDrop={(e) => {
        if (trash) return;
        e.preventDefault();
        setDragging(false);
        void upload(e.dataTransfer.files);
      }}
    >
      {/* left nav */}
      <aside className="hidden w-60 shrink-0 flex-col border-r border-slate-200 bg-slate-50/70 p-4 md:flex dark:border-slate-800 dark:bg-slate-900/50">
        <div className="mb-6 px-2">
          <Logo />
        </div>
        <nav className="space-y-1 text-sm">
          <NavItem icon={<FileText className="size-4" />} active={!trash} onClick={() => navigate('/')}>Documents</NavItem>
          <NavItem icon={<Trash2 className="size-4" />} active={trash} onClick={() => navigate('/trash')}>Trash</NavItem>
          <NavItem icon={<SettingsIcon className="size-4" />} active={false} onClick={() => navigate('/settings')}>Settings</NavItem>
        </nav>
        <div className="mt-auto space-y-2 rounded-xl border border-slate-200 bg-white p-3 text-xs dark:border-slate-800 dark:bg-slate-900">
          <div className="flex items-center gap-1.5 font-semibold text-slate-700 dark:text-slate-200">
            <span className={cx('size-2 rounded-full', health?.nimConfigured ? 'bg-nv' : 'bg-red-500')} />
            NVIDIA NIM {health?.nimConfigured ? 'connected' : health ? 'not configured' : '…'}
          </div>
          {health && (
            <div className="space-y-0.5 text-slate-500">
              <div title={health.models.check}>Checks: {health.models.check.split('/')[1]}</div>
              <div title={health.models.write}>Writing: {health.models.write.split('/')[1]}</div>
            </div>
          )}
        </div>
      </aside>

      <main className="nw-scroll min-w-0 flex-1 overflow-y-auto">
        <div className="mx-auto max-w-6xl px-5 py-6 sm:px-8">
          <div className="mb-6 flex flex-wrap items-center gap-3">
            <div className="md:hidden">
              <Logo compact />
            </div>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900 dark:text-white">{trash ? 'Trash' : 'Documents'}</h1>
            <div className="relative ml-auto w-full max-w-xs">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
              <input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search documents"
                className="h-9 w-full rounded-lg border border-slate-200 bg-white pl-9 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100 dark:border-slate-700 dark:bg-slate-900 dark:focus:ring-brand-900"
              />
            </div>
            <div className="flex gap-2 md:hidden">
              <Button size="sm" variant="ghost" onClick={() => navigate(trash ? '/' : '/trash')}>{trash ? 'Documents' : 'Trash'}</Button>
              <Button size="sm" variant="ghost" onClick={() => navigate('/settings')}><SettingsIcon className="size-4" /></Button>
            </div>
          </div>

          {!trash && (
            <div className="mb-8 grid gap-4 lg:grid-cols-[1fr_340px]">
              <div className="grid gap-4 sm:grid-cols-2">
                <button
                  onClick={() => void create()}
                  disabled={busy}
                  className="group flex items-center gap-4 rounded-2xl border-2 border-dashed border-brand-200 bg-brand-50/50 p-5 text-left transition-colors hover:border-brand-400 hover:bg-brand-50 disabled:opacity-60 dark:border-brand-900 dark:bg-brand-950/30 dark:hover:border-brand-700"
                >
                  <span className="flex size-12 items-center justify-center rounded-xl bg-brand-600 text-white shadow-sm transition-transform group-hover:scale-105">
                    {busy ? <Spinner className="text-white" /> : <FilePlus className="size-6" />}
                  </span>
                  <span>
                    <span className="block font-semibold text-slate-900 dark:text-white">New document</span>
                    <span className="text-sm text-slate-500 dark:text-slate-400">Start writing with live AI suggestions</span>
                  </span>
                </button>
                <button
                  onClick={() => fileRef.current?.click()}
                  disabled={busy}
                  className={cx(
                    'group flex items-center gap-4 rounded-2xl border-2 border-dashed p-5 text-left transition-colors disabled:opacity-60',
                    dragging ? 'border-brand-500 bg-brand-50 dark:bg-brand-950/40' : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50 dark:border-slate-700 dark:hover:bg-slate-900',
                  )}
                >
                  <span className="flex size-12 items-center justify-center rounded-xl bg-slate-100 text-slate-600 transition-transform group-hover:scale-105 dark:bg-slate-800 dark:text-slate-300">
                    <FileUp className="size-6" />
                  </span>
                  <span>
                    <span className="block font-semibold text-slate-900 dark:text-white">{dragging ? 'Drop to upload' : 'Upload a file'}</span>
                    <span className="text-sm text-slate-500 dark:text-slate-400">Word (.docx), text, Markdown or HTML. Or drag files here.</span>
                  </span>
                </button>
                <input ref={fileRef} type="file" multiple accept=".docx,.txt,.md,.markdown,.html,.htm" hidden onChange={(e) => { const f = e.target.files; if (f) void upload([...f]); e.target.value = ''; }} />
              </div>
              <InsightsCard insights={insights} />
            </div>
          )}

          {trash && (docs?.length ?? 0) > 0 && (
            <div className="mb-4 flex items-center justify-between rounded-xl bg-slate-50 px-4 py-3 text-sm text-slate-600 dark:bg-slate-900 dark:text-slate-400">
              Documents in the trash are kept until you delete them.
              <Button size="sm" variant="danger" onClick={() => { if (confirm('Permanently delete everything in the trash?')) void act(() => api.emptyTrash(), 'Trash emptied'); }}>Empty trash</Button>
            </div>
          )}

          {docs === null ? (
            <div className="flex justify-center py-16"><Spinner className="size-6" /></div>
          ) : filtered.length === 0 ? (
            <EmptyState icon={trash ? <Inbox className="size-6" /> : <FileText className="size-6" />} title={query ? 'No matching documents' : trash ? 'The trash is empty' : 'No documents yet'}>
              {query ? 'Try a different search.' : trash ? 'Deleted documents will appear here.' : 'Create a new document or upload one to get started.'}
            </EmptyState>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
              {filtered.map((d) => (
                <article
                  key={d.id}
                  onClick={() => !trash && navigate(`/doc/${d.id}`)}
                  className={cx(
                    'group relative flex flex-col rounded-2xl border border-slate-200 bg-white p-4 transition-all dark:border-slate-800 dark:bg-slate-900',
                    !trash && 'cursor-pointer hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md dark:hover:border-slate-700',
                  )}
                >
                  <div className="flex items-start gap-3">
                    <div className="min-w-0 flex-1">
                      <h2 className="truncate font-semibold text-slate-900 dark:text-white">{d.title || 'Untitled document'}</h2>
                      <p className="mt-0.5 text-xs text-slate-400">{ago(d.updatedAt)} · {d.wordCount.toLocaleString()} words</p>
                    </div>
                    {d.score !== null && !trash && (
                      <span className="relative shrink-0" title="Overall score">
                        <ScoreRing score={d.score} size={36} stroke={3.5} />
                        <span className="absolute inset-0 flex items-center justify-center text-[11px] font-semibold">{d.score}</span>
                      </span>
                    )}
                  </div>
                  <p className="mt-3 line-clamp-4 min-h-[5rem] text-sm leading-relaxed text-slate-600 dark:text-slate-400">{d.snippet || <span className="italic text-slate-400">Empty document</span>}</p>
                  <div className="mt-3 flex justify-end" onClick={(e) => e.stopPropagation()}>
                    {trash ? (
                      <div className="flex gap-1.5">
                        <Button size="sm" variant="secondary" onClick={() => void act(() => api.restoreDoc(d.id), 'Document restored')}><ArchiveRestore className="size-4" /> Restore</Button>
                        <Button size="sm" variant="ghost" className="text-red-600" onClick={() => { if (confirm(`Permanently delete “${d.title}”?`)) void act(() => api.deleteDoc(d.id, true), 'Deleted forever'); }}>Delete forever</Button>
                      </div>
                    ) : (
                      <Menu
                        trigger={(_o, toggle) => (
                          <button onClick={toggle} className="rounded-md p-1 text-slate-400 opacity-60 hover:bg-slate-100 hover:text-slate-700 group-hover:opacity-100 dark:hover:bg-slate-800" aria-label="Document actions">
                            <EllipsisVertical className="size-4" />
                          </button>
                        )}
                      >
                        {(close) => (
                          <>
                            <MenuItem icon={<FileText className="size-4" />} onClick={() => { close(); navigate(`/doc/${d.id}`); }}>Open</MenuItem>
                            <MenuItem icon={<Copy className="size-4" />} onClick={() => { close(); void act(() => api.duplicateDoc(d.id), 'Document duplicated'); }}>Duplicate</MenuItem>
                            <MenuItem icon={<Trash2 className="size-4" />} danger onClick={() => { close(); void act(() => api.deleteDoc(d.id), 'Moved to trash'); }}>Move to trash</MenuItem>
                          </>
                        )}
                      </Menu>
                    )}
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

function NavItem({ icon, active, onClick, children }: { icon: React.ReactNode; active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      className={cx(
        'flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-left transition-colors',
        active ? 'bg-white font-semibold text-slate-900 shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:text-white dark:ring-slate-700' : 'text-slate-600 hover:bg-white hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-white',
      )}
    >
      {icon}
      {children}
    </button>
  );
}

function InsightsCard({ insights }: { insights: InsightsResponse | null }) {
  const days = insights?.days ?? [];
  const max = Math.max(1, ...days.map((d) => d.stats.wordsChecked));
  const accepted = CATEGORIES.map((c) => ({ c, n: days.reduce((s, d) => s + (d.stats.accepted[c] ?? 0), 0) }));
  const totalAccepted = accepted.reduce((s, a) => s + a.n, 0);
  const wordsWeek = days.reduce((s, d) => s + d.stats.wordsChecked, 0);
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900">
      <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-slate-900 dark:text-white">
        <ChartColumn className="size-4 text-brand-600" /> Writing insights · last 7 days
      </div>
      {!insights ? (
        <div className="flex h-24 items-center justify-center"><Spinner /></div>
      ) : (
        <>
          <div className="mb-3 grid grid-cols-3 gap-2 text-center">
            <Stat label="Words checked" value={wordsWeek.toLocaleString()} />
            <Stat label="Fixes accepted" value={totalAccepted.toLocaleString()} />
            <Stat label="Documents" value={insights.totals.docs.toLocaleString()} />
          </div>
          <div className="flex h-16 items-end gap-1.5" aria-label="Words checked per day">
            {days.map((d) => (
              <div key={d.date} className="flex flex-1 flex-col items-center gap-1" title={`${d.date}: ${d.stats.wordsChecked} words checked`}>
                <div className="w-full rounded-t bg-brand-500/80" style={{ height: `${Math.max(3, (d.stats.wordsChecked / max) * 48)}px` }} />
                <span className="text-[10px] text-slate-400">{new Date(`${d.date}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'narrow' })}</span>
              </div>
            ))}
          </div>
          {totalAccepted > 0 && (
            <div className="mt-3 flex h-2 overflow-hidden rounded-full">
              {accepted.filter((a) => a.n).map((a) => (
                <div key={a.c} style={{ width: `${(a.n / totalAccepted) * 100}%`, background: CATEGORY_META[a.c].color }} title={`${CATEGORY_META[a.c].label}: ${a.n}`} />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg bg-slate-50 px-2 py-1.5 dark:bg-slate-800/60">
      <div className="text-base font-semibold text-slate-900 dark:text-white">{value}</div>
      <div className="text-[10.5px] text-slate-500">{label}</div>
    </div>
  );
}
