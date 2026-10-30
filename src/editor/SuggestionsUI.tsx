// Suggestion cards (sidebar + inline popover), the Review panel and the synonym popover.
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { BellOff, BookA, Check, ChevronDown, CircleCheck, Ellipsis, Sparkles, Trash } from 'lucide-react';
import type { Category, SynonymSense } from '../../shared/types';
import { CATEGORIES } from '../../shared/types';
import { CATEGORY_META } from '../../shared/meta';
import type { PlacedSuggestion } from './suggestions';
import { Button, cx, EmptyState, ScoreRing, Spinner } from '../components/ui';
import { TuneBox } from './TuneBox';

export interface SuggestionActions {
  onAccept: (s: PlacedSuggestion, replacement: string) => void;
  onDismiss: (s: PlacedSuggestion) => void;
  onAddToDictionary: (s: PlacedSuggestion) => void;
  /** Never show this kind of suggestion again (Settings → Suggestions lists the muted kinds). */
  onMuteType: (s: PlacedSuggestion) => void;
}

function Change({ s, onPick }: { s: PlacedSuggestion; onPick?: (r: string) => void }) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {s.replacements.length ? (
        <>
          <span className="text-slate-500 line-through decoration-slate-400 dark:text-slate-400">{s.original.trim() || '␣'}</span>
          {s.replacements.map((r) => (
            <button
              key={r}
              onClick={(e) => {
                e.stopPropagation();
                onPick?.(r);
              }}
              className="rounded-md bg-brand-600 px-2 py-0.5 font-medium text-white shadow-sm transition-colors hover:bg-brand-700"
              title="Accept this suggestion"
            >
              {r === '' ? 'Remove' : r.trim() === '' ? '␣' : r}
            </button>
          ))}
        </>
      ) : (
        <span className="font-medium text-slate-700 dark:text-slate-200">“{s.original}”</span>
      )}
    </div>
  );
}

export function SuggestionCard({ s, expanded, onExpand, actions, onHover, compact }: {
  s: PlacedSuggestion;
  expanded: boolean;
  onExpand?: () => void;
  actions: SuggestionActions;
  onHover?: (on: boolean) => void;
  compact?: boolean;
}) {
  const meta = CATEGORY_META[s.category];
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (expanded && !compact) ref.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [expanded, compact]);
  return (
    <div
      ref={ref}
      onMouseEnter={() => onHover?.(true)}
      onMouseLeave={() => onHover?.(false)}
      onClick={onExpand}
      className={cx(
        'rounded-xl border bg-white transition-all dark:bg-slate-900',
        expanded ? 'border-slate-200 shadow-md dark:border-slate-700' : 'cursor-pointer border-transparent hover:border-slate-200 hover:shadow-sm dark:hover:border-slate-700',
        compact && 'border-slate-200 shadow-xl dark:border-slate-700',
      )}
    >
      <div className="flex items-start gap-2.5 px-3.5 py-3">
        <span className="mt-1.5 size-2 shrink-0 rounded-full" style={{ background: meta.color }} />
        <div className="min-w-0 flex-1">
          {!expanded ? (
            <div className="flex items-center gap-2 text-sm">
              <span className="truncate font-medium text-slate-800 dark:text-slate-100">{s.original.trim() || 'Spacing'}</span>
              <span className="text-slate-400">·</span>
              <span className="truncate text-slate-500 dark:text-slate-400">{s.title}</span>
            </div>
          ) : (
            <>
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="text-xs font-semibold uppercase tracking-wide" style={{ color: meta.color }}>
                  {meta.label}
                  {s.source === 'ai' && <Sparkles className="ml-1 inline size-3 opacity-70" />}
                </span>
                {!compact && <ChevronDown className="size-4 rotate-180 text-slate-400" />}
              </div>
              <div className="mb-2 text-[15px] font-semibold text-slate-900 dark:text-white">{s.title}</div>
              <div className="text-[15px]">
                <Change s={s} onPick={(r) => actions.onAccept(s, r)} />
              </div>
              {s.explanation && <p className="mt-2.5 text-sm leading-relaxed text-slate-600 dark:text-slate-400">{s.explanation}</p>}
              <div className="mt-3 flex flex-wrap items-center gap-x-1 gap-y-0.5 border-t border-slate-100 pt-2 dark:border-slate-800">
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    actions.onDismiss(s);
                  }}
                  className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-1 text-[13px] text-slate-500 hover:bg-slate-100 hover:text-slate-800 dark:hover:bg-slate-800 dark:hover:text-slate-200"
                >
                  <Trash className="size-3.5" /> Dismiss
                </button>
                {(s.type === 'spelling' || s.type === 'dialect') && (
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      actions.onAddToDictionary(s);
                    }}
                    className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-1 text-[13px] text-slate-500 hover:bg-slate-100 hover:text-slate-800 dark:hover:bg-slate-800 dark:hover:text-slate-200"
                  >
                    <BookA className="size-3.5" /> Add to dictionary
                  </button>
                )}
                {s.type !== 'style_guide' && (
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      actions.onMuteType(s);
                    }}
                    title={`Never suggest “${s.title}” again`}
                    className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-1 text-[13px] text-slate-500 hover:bg-slate-100 hover:text-slate-800 dark:hover:bg-slate-800 dark:hover:text-slate-200"
                  >
                    <BellOff className="size-3.5" /> Stop suggesting this
                  </button>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

export function SuggestionsPanel({ items, expandedId, setExpandedId, onHoverItem, actions, score, onOpenPerformance, status, onAcceptAll, checking }: {
  items: PlacedSuggestion[];
  expandedId: string | null;
  setExpandedId: (id: string | null) => void;
  onHoverItem: (id: string | null) => void;
  actions: SuggestionActions;
  score: number | null;
  onOpenPerformance: () => void;
  status: string;
  onAcceptAll: (cat: Category | 'all') => void;
  checking: boolean;
}) {
  const [filter, setFilter] = useState<Category | 'all'>('all');
  const counts = useMemo(() => {
    const c: Record<Category, number> = { correctness: 0, clarity: 0, engagement: 0, delivery: 0 };
    for (const s of items) c[s.category]++;
    return c;
  }, [items]);
  const visible = filter === 'all' ? items : items.filter((s) => s.category === filter);
  const fixable = visible.filter((s) => s.replacements.length > 0);
  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-slate-100 px-4 pb-3 pt-4 dark:border-slate-800">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-[15px] font-semibold text-slate-900 dark:text-white">Review suggestions</h2>
            <p className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400">
              {checking && <Spinner className="size-3" />}
              {status}
            </p>
          </div>
          <button onClick={onOpenPerformance} className="flex items-center gap-2 rounded-lg px-2 py-1 hover:bg-slate-50 dark:hover:bg-slate-800" title="See performance">
            <span className="relative">
              <ScoreRing score={score} size={42} />
              <span className="absolute inset-0 flex items-center justify-center text-[13px] font-semibold text-slate-800 dark:text-slate-100">{score ?? '–'}</span>
            </span>
            <span className="text-left text-[11px] leading-tight text-slate-500 dark:text-slate-400">
              Overall
              <br />
              score
            </span>
          </button>
        </div>
        <div className="mt-3 grid grid-cols-5 gap-1">
          <FilterTab label="All" count={items.length} color="#475569" active={filter === 'all'} onClick={() => setFilter('all')} />
          {CATEGORIES.map((c) => (
            <FilterTab key={c} label={CATEGORY_META[c].label} count={counts[c]} color={CATEGORY_META[c].color} active={filter === c} onClick={() => setFilter(c)} />
          ))}
        </div>
      </div>
      <div className="nw-scroll flex-1 space-y-1.5 overflow-y-auto px-3 py-3">
        {visible.length === 0 ? (
          checking ? (
            <EmptyState icon={<Spinner className="size-5" />} title="Checking your writing…">NVIDIA NIM is reading your text.</EmptyState>
          ) : (
            <EmptyState icon={<CircleCheck className="size-6" />} title={items.length ? 'Nothing in this category' : 'Looking good!'}>
              {items.length ? 'Pick another category above.' : 'No suggestions right now. Keep writing and NimbleWrite will check as you go.'}
            </EmptyState>
          )
        ) : (
          visible.map((s) => (
            <SuggestionCard
              key={s.id}
              s={s}
              expanded={expandedId === s.id}
              onExpand={() => setExpandedId(expandedId === s.id ? null : s.id)}
              onHover={(on) => onHoverItem(on ? s.id : null)}
              actions={actions}
            />
          ))
        )}
      </div>
      {fixable.length > 1 && (
        <div className="border-t border-slate-100 px-4 py-2.5 dark:border-slate-800">
          <Button variant="subtle" size="sm" className="w-full" onClick={() => onAcceptAll(filter)}>
            <Check className="size-4" /> Accept all {fixable.length} {filter === 'all' ? '' : CATEGORY_META[filter].label.toLowerCase()} suggestions
          </Button>
        </div>
      )}
      <TuneBox />
    </div>
  );
}

function FilterTab({ label, count, color, active, onClick }: { label: string; count: number; color: string; active: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} className={cx('flex flex-col items-start rounded-lg px-1.5 pb-1.5 pt-1 text-left transition-colors', active ? 'bg-slate-100 dark:bg-slate-800' : 'hover:bg-slate-50 dark:hover:bg-slate-800/60')}>
      <span className="text-base font-semibold leading-tight text-slate-800 dark:text-slate-100">{count}</span>
      <span className="w-full truncate text-[10.5px] text-slate-500 dark:text-slate-400">{label}</span>
      <span className="mt-1 h-[3px] w-full rounded-full" style={{ background: active ? color : `${color}55` }} />
    </button>
  );
}

/** Floating card next to an underline. */
export function SuggestionPopover({ s, rect, actions, onClose, onKeepOpen }: { s: PlacedSuggestion; rect: DOMRect; actions: SuggestionActions; onClose: () => void; onKeepOpen: (on: boolean) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState({ top: rect.bottom + 8, left: rect.left });
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const w = el.offsetWidth;
    const h = el.offsetHeight;
    let top = rect.bottom + 8;
    if (top + h > window.innerHeight - 12) top = Math.max(12, rect.top - h - 8);
    const left = Math.min(Math.max(12, rect.left - 12), window.innerWidth - w - 12);
    setPos({ top, left });
  }, [rect]);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    const onDown = (e: MouseEvent) => {
      const t = e.target as HTMLElement;
      if (ref.current?.contains(t) || t.closest('.nw-sg')) return;
      onClose();
    };
    window.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onDown);
    return () => {
      window.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onDown);
    };
  }, [onClose]);
  return createPortal(
    <div ref={ref} className="nw-pop nw-chrome fixed z-40 w-[340px]" style={pos} onMouseEnter={() => onKeepOpen(true)} onMouseLeave={() => onKeepOpen(false)}>
      <SuggestionCard s={s} expanded compact actions={actions} />
    </div>,
    document.body,
  );
}

export function SynonymPopover({ word, rect, loading, senses, error, onPick, onClose }: {
  word: string;
  rect: DOMRect;
  loading: boolean;
  senses: SynonymSense[];
  error: string | null;
  onPick: (w: string) => void;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    const onDown = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && onClose();
    window.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onDown);
    return () => {
      window.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onDown);
    };
  }, [onClose]);
  const top = rect.bottom + 8 + 260 > window.innerHeight ? Math.max(12, rect.top - 268) : rect.bottom + 8;
  return createPortal(
    <div ref={ref} className="nw-pop nw-chrome fixed z-40 w-72 rounded-xl border border-slate-200 bg-white p-3 shadow-xl dark:border-slate-700 dark:bg-slate-900" style={{ top, left: Math.min(rect.left, window.innerWidth - 300) }}>
      <div className="mb-2 flex items-center justify-between">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">Synonyms for “{word}”</span>
        {loading && <Spinner className="size-3.5" />}
      </div>
      {error && <p className="text-sm text-red-600">{error}</p>}
      {!loading && !error && senses.length === 0 && <p className="text-sm text-slate-500">No good synonyms for this context.</p>}
      <div className="space-y-2.5">
        {senses.map((sense) => (
          <div key={sense.meaning}>
            <div className="mb-1 text-xs italic text-slate-500 dark:text-slate-400">{sense.meaning}</div>
            <div className="flex flex-wrap gap-1.5">
              {sense.synonyms.map((w) => (
                <button key={w} onClick={() => onPick(w)} className="rounded-md border border-slate-200 px-2 py-0.5 text-sm text-slate-700 hover:border-brand-400 hover:bg-brand-50 hover:text-brand-700 dark:border-slate-700 dark:text-slate-200 dark:hover:bg-brand-900/40">
                  {w}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
      {loading && senses.length === 0 && (
        <div className="space-y-2">
          <div className="h-3 w-24 animate-pulse rounded bg-slate-100 dark:bg-slate-800" />
          <div className="flex gap-1.5">
            {[48, 64, 40].map((w) => (
              <div key={w} className="h-6 animate-pulse rounded bg-slate-100 dark:bg-slate-800" style={{ width: w }} />
            ))}
          </div>
        </div>
      )}
      <div className="mt-2 flex items-center gap-1 text-[11px] text-slate-400">
        <Ellipsis className="size-3" /> Double-click any word for synonyms
      </div>
    </div>,
    document.body,
  );
}
