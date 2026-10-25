// The document editor: TipTap + live NVIDIA NIM checking, suggestion cards, goals, performance,
// tone detection, synonyms, AI panels, autosave and export.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { EditorContent, useEditor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import { Placeholder } from '@tiptap/extensions';
import type { Transaction } from '@tiptap/pm/state';
import {
  ArrowLeft, CircleAlert, Cloud, CloudCheck, CloudOff, Copy, Download, EllipsisVertical, Eye, EyeOff, FileUp, Gauge, Maximize2, Minimize2,
  PanelRightClose, PanelRightOpen, Printer, Target,
} from 'lucide-react';
import type { Category, Doc, Goals, HealthResponse, Settings, SynonymSense, ToneResult } from '../../shared/types';
import { CATEGORIES } from '../../shared/types';
import { GOAL_OPTIONS } from '../../shared/meta';
import { computeStats, hashText, overallScore, sentences } from '../../shared/text';
import { api } from '../api';
import { navigate } from '../router';
import { toast, useSettings } from '../store';
import { Button, cx, IconButton, Menu, MenuItem, ScoreRing, Spinner } from '../components/ui';
import { Checker, type CheckerContext, type CheckerStatus } from './checker';
import {
  applySuggestion, applySuggestionsBatch, getSuggestions, removeSuggestion, setActiveSuggestion, Suggestions, type PlacedSuggestion,
} from './suggestions';
import { Snippets } from './snippets';
import { SuggestionPopover, SuggestionsPanel, SynonymPopover, type SuggestionActions } from './SuggestionsUI';
import { GoalsModal, PerformanceModal } from './Dialogs';
import { FormatToolbar } from './Toolbar';
import { copyRich, exportDocx, exportHtml, exportMarkdown, exportText } from './editorUtils';
import { AssistantPanel, CitationsPanel, DetectorPanel, GraderPanel, PANELS, PlagiarismPanel, ReaderPanel, RewritePanel, type PanelId } from './panels/AgentPanels';

export function EditorPage({ id, health }: { id: string; health: HealthResponse | null }) {
  const settings = useSettings((s) => s.settings);
  const [doc, setDoc] = useState<Doc | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    setDoc(null);
    setError(null);
    api.getDoc(id).then((d) => alive && setDoc(d)).catch((e: Error) => alive && setError(e.message));
    return () => {
      alive = false;
    };
  }, [id]);
  if (error) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 text-center">
        <CircleAlert className="size-8 text-red-500" />
        <p className="text-slate-700 dark:text-slate-300">{error}</p>
        <Button onClick={() => navigate('/')}>Back to documents</Button>
      </div>
    );
  }
  if (!doc || !settings) {
    return (
      <div className="flex h-full items-center justify-center">
        <Spinner className="size-6" />
      </div>
    );
  }
  return <DocumentEditor key={doc.id} initial={doc} settings={settings} health={health} />;
}

type PopoverState = { id: string; rect: DOMRect; pinned: boolean } | null;
type SynonymState = { word: string; from: number; to: number; rect: DOMRect; loading: boolean; senses: SynonymSense[]; error: string | null } | null;

function DocumentEditor({ initial, settings, health }: { initial: Doc; settings: Settings; health: HealthResponse | null }) {
  const updateSettings = useSettings((s) => s.update);
  const [title, setTitle] = useState(initial.title);
  const [goals, setGoals] = useState<Goals>(initial.goals);
  const [dismissed, setDismissed] = useState(() => new Set(initial.dismissed));
  const [items, setItems] = useState<PlacedSuggestion[]>([]);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [popover, setPopover] = useState<PopoverState>(null);
  const [status, setStatus] = useState<CheckerStatus>({ state: 'idle', pending: 0 });
  const [panel, setPanel] = useState<PanelId>('suggestions');
  const [panelOpen, setPanelOpen] = useState(() => window.innerWidth >= 1024);
  const [text, setText] = useState(initial.text);
  const [tone, setTone] = useState<ToneResult | null>(null);
  const [toneLoading, setToneLoading] = useState(false);
  const [toneOpen, setToneOpen] = useState(false);
  const [saveState, setSaveState] = useState<'saved' | 'saving' | 'unsaved' | 'error'>('saved');
  const [goalsOpen, setGoalsOpen] = useState(() => settings.showGoalsOnNewDoc && !initial.text.trim() && Date.now() - initial.createdAt < 120_000);
  const [perfOpen, setPerfOpen] = useState(false);
  const [synonym, setSynonym] = useState<SynonymState>(null);
  const [focusMode, setFocusMode] = useState(false);
  const [showUnderlines, setShowUnderlines] = useState(true);
  const fileRef = useRef<HTMLInputElement>(null);

  // ---- refs read by long-lived callbacks (editor plugins, timers) ----
  const settingsRef = useRef(settings);
  settingsRef.current = settings;
  const titleRef = useRef(title);
  titleRef.current = title;
  const goalsRef = useRef(goals);
  goalsRef.current = goals;
  const dismissedRef = useRef(dismissed);
  dismissedRef.current = dismissed;
  const ctxRef = useRef<CheckerContext>(null as unknown as CheckerContext);
  ctxRef.current = {
    dialect: settings.dialect,
    goals,
    categories: settings.categories,
    dictionary: settings.dictionary,
    styleRules: settings.styleRules,
    localRules: settings.localRules,
    dismissed,
    model: settings.models.check,
  };
  const checkerRef = useRef<Checker | null>(null);
  const hoverTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const leaveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const overPopover = useRef(false);
  const popoverRef = useRef<PopoverState>(null);
  popoverRef.current = popover;

  const handlers = useRef({
    onClick: (_id: string, _rect: DOMRect) => {},
    onHover: (_id: string, _rect: DOMRect) => {},
    onHoverEnd: () => {},
  });

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ heading: { levels: [1, 2, 3] }, link: { openOnClick: false, autolink: true, defaultProtocol: 'https' } }),
      Placeholder.configure({ placeholder: 'Type or paste (Ctrl+V) your text here, or upload a document.' }),
      Suggestions.configure({
        onClick: (id, rect) => handlers.current.onClick(id, rect),
        onHover: (id, rect) => handlers.current.onHover(id, rect),
        onHoverEnd: () => handlers.current.onHoverEnd(),
      }),
      Snippets.configure({ getSnippets: () => settingsRef.current.snippets }),
    ],
    content: initial.html || '',
    editorProps: { attributes: { spellcheck: 'false', 'aria-label': 'Document text', class: 'nw-doc' } },
  });

  // ---- autosave ----
  const dirtyRef = useRef(false);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scoreRef = useRef<number | null>(initial.score);

  const payload = useCallback(() => {
    if (!editor || editor.isDestroyed) return null;
    return {
      title: titleRef.current.trim() || 'Untitled document',
      html: editor.getHTML(),
      text: editor.getText({ blockSeparator: '\n\n' }),
      goals: goalsRef.current,
      dismissed: [...dismissedRef.current].slice(-5000),
      score: scoreRef.current,
    };
  }, [editor]);

  const saveNow = useCallback(async () => {
    if (saveTimer.current) {
      clearTimeout(saveTimer.current);
      saveTimer.current = null;
    }
    if (!dirtyRef.current) return;
    const body = payload();
    if (!body) return;
    dirtyRef.current = false;
    setSaveState('saving');
    try {
      await api.updateDoc(initial.id, body);
      setSaveState(dirtyRef.current ? 'unsaved' : 'saved');
    } catch {
      dirtyRef.current = true;
      setSaveState('error');
    }
  }, [initial.id, payload]);

  const markDirty = useCallback(() => {
    dirtyRef.current = true;
    setSaveState('unsaved');
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => void saveNow(), 800);
  }, [saveNow]);

  // Flush unsaved work when leaving the page or the document.
  useEffect(() => {
    const flush = () => {
      if (!dirtyRef.current) return;
      const body = payload();
      if (!body) return;
      const json = JSON.stringify(body);
      dirtyRef.current = false;
      void fetch(`/api/docs/${initial.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: json, keepalive: json.length < 60_000 }).catch(() => undefined);
    };
    window.addEventListener('pagehide', flush);
    return () => {
      window.removeEventListener('pagehide', flush);
      if (saveTimer.current) clearTimeout(saveTimer.current);
      flush();
    };
  }, [initial.id, payload]);

  // ---- checker lifecycle + mirror plugin state into React ----
  useEffect(() => {
    if (!editor) return;
    const checker = new Checker(editor, () => ctxRef.current, setStatus);
    checkerRef.current = checker;
    checker.publish();
    checker.schedule(150);
    let raf = 0;
    let textTimer: ReturnType<typeof setTimeout> | null = null;
    let last: PlacedSuggestion[] | null = null;
    const sync = () => {
      raf = 0;
      const { items: next } = getSuggestions(editor.state);
      if (next !== last) {
        last = next;
        setItems(next);
      }
    };
    const onTr = ({ transaction }: { transaction: Transaction }) => {
      if (transaction.docChanged) {
        checker.onDocChange();
        markDirty();
        if (textTimer) clearTimeout(textTimer);
        textTimer = setTimeout(() => setText(editor.getText({ blockSeparator: '\n\n' })), 250);
        if (popoverRef.current) setPopover(null);
        setSynonym(null);
      }
      if (!raf) raf = requestAnimationFrame(sync);
    };
    editor.on('transaction', onTr);
    sync();
    return () => {
      editor.off('transaction', onTr);
      if (raf) cancelAnimationFrame(raf);
      if (textTimer) clearTimeout(textTimer);
      checker.destroy();
      checkerRef.current = null;
    };
  }, [editor, markDirty]);

  // Re-check when anything that changes the model's answer (or the local rules) changes.
  const firstRun = useRef(true);
  useEffect(() => {
    if (firstRun.current) {
      firstRun.current = false;
      return;
    }
    checkerRef.current?.refresh();
  }, [goals, dismissed, settings.dialect, settings.categories, settings.models.check, settings.dictionary, settings.styleRules, settings.localRules]);

  // ---- stats, score and tone ----
  const stats = useMemo(() => computeStats(text), [text]);
  const counts = useMemo(() => {
    const c: Record<Category, number> = { correctness: 0, clarity: 0, engagement: 0, delivery: 0 };
    for (const s of items) c[s.category]++;
    return c;
  }, [items]);
  const score = useMemo(() => overallScore(stats.words, counts, stats.readability), [stats, counts]);
  scoreRef.current = score;

  // Persist the score for the documents list once checking settles.
  const savedScore = useRef(initial.score);
  useEffect(() => {
    if (status.state !== 'idle' || score === savedScore.current) return;
    const t = setTimeout(() => {
      savedScore.current = score;
      if (!dirtyRef.current) void api.updateDoc(initial.id, { score }).catch(() => undefined);
    }, 1500);
    return () => clearTimeout(t);
  }, [score, status.state, initial.id]);

  const toneHash = useRef('');
  useEffect(() => {
    if (stats.words < 20) {
      setTone(null);
      toneHash.current = '';
      return;
    }
    const h = hashText(text);
    if (h === toneHash.current) return;
    const ac = new AbortController();
    const t = setTimeout(() => {
      setToneLoading(true);
      api
        .tone(text, ac.signal)
        .then((r) => {
          toneHash.current = h;
          setTone(r);
        })
        .catch(() => undefined)
        .finally(() => setToneLoading(false));
    }, 3500);
    return () => {
      clearTimeout(t);
      ac.abort();
    };
  }, [text, stats.words]);

  // ---- suggestion actions ----
  const accept = useCallback(
    (s: PlacedSuggestion, replacement: string) => {
      const checker = checkerRef.current;
      if (!editor || !checker) return;
      const fresh = getSuggestions(editor.state).items.find((x) => x.id === s.id) ?? s;
      const idx = checker.blockIndexAt(fresh.from);
      if (!applySuggestion(editor.view, fresh, replacement)) {
        removeSuggestion(editor.view, fresh.id);
        toast('That text has changed, so the suggestion no longer applies.');
        return;
      }
      checker.adoptBlocks([idx]);
      void api.accepted(fresh.category);
      setPopover(null);
      const after = getSuggestions(editor.state).items;
      setExpandedId((after.find((x) => x.from >= fresh.from) ?? after[after.length - 1])?.id ?? null);
    },
    [editor],
  );

  const dismiss = useCallback(
    (s: PlacedSuggestion) => {
      if (!editor) return;
      const next = new Set(dismissedRef.current);
      next.add(s.sig);
      dismissedRef.current = next;
      ctxRef.current = { ...ctxRef.current, dismissed: next };
      setDismissed(next);
      removeSuggestion(editor.view, s.id);
      setPopover(null);
      markDirty();
    },
    [editor, markDirty],
  );

  const addToDictionary = useCallback(
    (s: PlacedSuggestion) => {
      if (!editor) return;
      const word = s.original.trim();
      const current = settingsRef.current.dictionary;
      if (!current.some((w) => w.toLowerCase() === word.toLowerCase())) void updateSettings({ dictionary: [...current, word] });
      removeSuggestion(editor.view, s.id);
      setPopover(null);
      toast(`“${word}” added to your dictionary`, 'success');
    },
    [editor, updateSettings],
  );

  const actions: SuggestionActions = useMemo(() => ({ onAccept: accept, onDismiss: dismiss, onAddToDictionary: addToDictionary }), [accept, dismiss, addToDictionary]);

  const acceptAll = useCallback(
    (cat: Category | 'all') => {
      const checker = checkerRef.current;
      if (!editor || !checker) return;
      const list = getSuggestions(editor.state).items.filter((s) => (cat === 'all' || s.category === cat) && s.replacements.length > 0);
      const idxs = list.map((s) => checker.blockIndexAt(s.from));
      const applied = applySuggestionsBatch(editor.view, list);
      checker.adoptBlocks(idxs);
      const per: Partial<Record<Category, number>> = {};
      for (const s of applied) per[s.category] = (per[s.category] ?? 0) + 1;
      for (const c of CATEGORIES) if (per[c]) void api.accepted(c, per[c]);
      toast(`Accepted ${applied.length} suggestion${applied.length === 1 ? '' : 's'}. Press Ctrl+Z to undo.`, 'success');
    },
    [editor],
  );

  // Underline interactions: hover shows a card after a short pause, click pins it.
  handlers.current = {
    onClick: (id, rect) => {
      if (!editor) return;
      if (hoverTimer.current) clearTimeout(hoverTimer.current);
      setPopover({ id, rect, pinned: true });
      setExpandedId(id);
      setActiveSuggestion(editor.view, id);
    },
    onHover: (id, rect) => {
      if (!editor) return;
      if (leaveTimer.current) clearTimeout(leaveTimer.current);
      if (hoverTimer.current) clearTimeout(hoverTimer.current);
      if (popoverRef.current?.pinned) return;
      hoverTimer.current = setTimeout(() => {
        setPopover({ id, rect, pinned: false });
        setActiveSuggestion(editor.view, id);
      }, 450);
    },
    onHoverEnd: () => {
      if (hoverTimer.current) clearTimeout(hoverTimer.current);
      leaveTimer.current = setTimeout(() => {
        const p = popoverRef.current;
        if (p && !p.pinned && !overPopover.current) {
          setPopover(null);
          if (editor) setActiveSuggestion(editor.view, null);
        }
      }, 280);
    },
  };

  const closePopover = useCallback(() => {
    setPopover(null);
    if (editor) setActiveSuggestion(editor.view, null);
  }, [editor]);

  const popoverItem = popover ? items.find((s) => s.id === popover.id) : undefined;
  useEffect(() => {
    if (popover && !popoverItem) setPopover(null);
  }, [popover, popoverItem]);

  // ---- synonyms on double-click ----
  // Resolve the word under the pointer directly (posAtCoords) instead of reading the selection:
  // ProseMirror syncs the DOM selection asynchronously, so it may still be collapsed at this point.
  useEffect(() => {
    if (!editor) return;
    const dom = editor.view.dom as HTMLElement;
    let ac: AbortController | null = null;
    const isWordChar = (c: string | undefined) => !!c && /[A-Za-z'’-]/.test(c);
    const onDbl = (e: MouseEvent) => {
      if ((e.target as HTMLElement | null)?.closest('.nw-sg')) return; // underlined words open their suggestion instead
      const hit = editor.view.posAtCoords({ left: e.clientX, top: e.clientY });
      if (!hit) return;
      const $pos = editor.state.doc.resolve(hit.pos);
      if (!$pos.parent.isTextblock || $pos.parent.type.name === 'codeBlock') return;
      const start = $pos.start();
      const para = $pos.parent.textBetween(0, $pos.parent.content.size, undefined, '\ufffc');
      let a = hit.pos - start;
      let b = a;
      while (a > 0 && isWordChar(para[a - 1])) a--;
      while (b < para.length && isWordChar(para[b])) b++;
      while (a < b && /['’-]/.test(para[a])) a++;
      while (b > a && /['’-]/.test(para[b - 1])) b--;
      const word = para.slice(a, b);
      if (!/^[A-Za-z][A-Za-z'’-]{1,40}$/.test(word)) return;
      const from = start + a;
      const to = start + b;
      const sentence = sentences(para).find((x) => x.includes(word)) ?? para;
      const ca = editor.view.coordsAtPos(from);
      const cb = editor.view.coordsAtPos(to);
      const rect = new DOMRect(ca.left, ca.top, Math.max(8, cb.right - ca.left), ca.bottom - ca.top);
      ac?.abort();
      ac = new AbortController();
      const signal = ac.signal;
      setSynonym({ word, from, to, rect, loading: true, senses: [], error: null });
      api
        .synonyms(word, sentence, settingsRef.current.dialect, signal)
        .then((r) => setSynonym((s) => (s && s.from === from && s.word === word ? { ...s, loading: false, senses: r.senses } : s)))
        .catch((err: Error) => {
          if (err.name !== 'AbortError') setSynonym((s) => (s && s.from === from ? { ...s, loading: false, error: err.message } : s));
        });
    };
    dom.addEventListener('dblclick', onDbl);
    return () => {
      dom.removeEventListener('dblclick', onDbl);
      ac?.abort();
    };
  }, [editor]);

  const pickSynonym = (w: string) => {
    if (!editor || !synonym) return;
    const { from, to, word } = synonym;
    if (editor.state.doc.textBetween(from, to) !== word) {
      setSynonym(null);
      return toast('The word has changed.');
    }
    const cased = word === word.toUpperCase() && word.length > 1 ? w.toUpperCase() : /^[A-Z]/.test(word) ? w.charAt(0).toUpperCase() + w.slice(1) : w;
    editor.view.dispatch(editor.state.tr.insertText(cased, from, to));
    editor.commands.focus();
    setSynonym(null);
  };

  // ---- keyboard: Ctrl+S saves now ----
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        dirtyRef.current = true;
        void saveNow().then(() => toast('Saved', 'success'));
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [saveNow]);

  // ---- import into this document ----
  const importFile = async (file: File) => {
    if (!editor) return;
    try {
      const r = await api.importFile(file);
      if (!editor.getText().trim()) {
        editor.commands.setContent(r.html);
        if (/^untitled/i.test(titleRef.current)) setTitle(r.title);
      } else {
        editor.chain().focus().insertContentAt(editor.state.doc.content.size, r.html).run();
      }
      markDirty();
      toast(`Imported “${file.name}”`, 'success');
    } catch (e) {
      toast((e as Error).message, 'error');
    }
  };

  if (!editor) return null;

  const statusText =
    status.state === 'checking'
      ? `Checking ${status.pending} paragraph${status.pending === 1 ? '' : 's'} with NVIDIA NIM…`
      : status.state === 'error'
        ? `Couldn't reach the model (${status.message}). Retrying shortly.`
        : items.length
          ? `${items.length} suggestion${items.length === 1 ? '' : 's'} · ${settings.models.check.split('/')[1]}`
          : stats.words
            ? 'All caught up'
            : 'Start writing to get suggestions';

  const ctx = { editor, docId: initial.id, title };
  const goalsSummary = `${GOAL_OPTIONS.domain.find((d) => d.id === goals.domain)?.label} · ${GOAL_OPTIONS.formality.find((d) => d.id === goals.formality)?.label}`;
  const showPanel = panelOpen && !focusMode;

  return (
    <div className="flex h-full flex-col">
      {/* header */}
      <header className="nw-chrome flex h-14 shrink-0 items-center gap-2 border-b border-slate-200 bg-white px-3 dark:border-slate-800 dark:bg-slate-950">
        <IconButton label="All documents" onClick={() => void saveNow().finally(() => navigate('/'))}>
          <ArrowLeft className="size-5" />
        </IconButton>
        <input
          value={title}
          onChange={(e) => {
            setTitle(e.target.value);
            markDirty();
          }}
          onBlur={() => {
            if (!title.trim()) setTitle('Untitled document');
          }}
          aria-label="Document title"
          className="min-w-0 max-w-md flex-1 truncate rounded-md bg-transparent px-2 py-1 text-[15px] font-medium text-slate-900 outline-none hover:bg-slate-50 focus:bg-slate-50 dark:text-white dark:hover:bg-slate-900 dark:focus:bg-slate-900"
        />
        <span className="hidden items-center gap-1 text-xs text-slate-400 sm:flex" title={saveState === 'error' ? 'Saving failed. Retrying on the next change, or press Ctrl+S.' : undefined}>
          {saveState === 'saved' ? <CloudCheck className="size-4" /> : saveState === 'error' ? <CloudOff className="size-4 text-red-500" /> : <Cloud className="size-4" />}
          {saveState === 'saved' ? 'Saved' : saveState === 'saving' ? 'Saving…' : saveState === 'error' ? 'Not saved' : 'Edited'}
        </span>
        <div className="ml-auto flex items-center gap-1.5">
          <Button variant="ghost" size="sm" onClick={() => setGoalsOpen(true)} title="Set goals">
            <Target className="size-4" />
            <span className="hidden md:inline">Goals</span>
            <span className="hidden text-xs font-normal text-slate-400 xl:inline">{goalsSummary}</span>
          </Button>
          <button onClick={() => setPerfOpen(true)} className="flex items-center gap-2 rounded-lg px-2 py-1 hover:bg-slate-50 dark:hover:bg-slate-900" title="Performance">
            <span className="relative">
              <ScoreRing score={score} size={34} stroke={3.5} />
              <span className="absolute inset-0 flex items-center justify-center text-[11px] font-semibold">{score ?? '–'}</span>
            </span>
            <span className="hidden text-left text-[11px] leading-tight text-slate-500 lg:block">
              Overall
              <br />
              score
            </span>
          </button>
          <Menu
            trigger={(_open, toggle) => (
              <IconButton label="More" onClick={toggle}>
                <EllipsisVertical className="size-5" />
              </IconButton>
            )}
          >
            {(close) => (
              <>
                <MenuItem icon={<FileUp className="size-4" />} onClick={() => { close(); fileRef.current?.click(); }}>Import file into this document…</MenuItem>
                <div className="my-1 border-t border-slate-100 dark:border-slate-800" />
                <MenuItem icon={<Download className="size-4" />} onClick={() => { close(); void exportDocx(editor, title); }}>Download as Word (.docx)</MenuItem>
                <MenuItem icon={<Download className="size-4" />} onClick={() => { close(); exportText(editor, title); }}>Download as text (.txt)</MenuItem>
                <MenuItem icon={<Download className="size-4" />} onClick={() => { close(); void exportMarkdown(editor, title); }}>Download as Markdown (.md)</MenuItem>
                <MenuItem icon={<Download className="size-4" />} onClick={() => { close(); exportHtml(editor, title); }}>Download as web page (.html)</MenuItem>
                <MenuItem icon={<Copy className="size-4" />} onClick={() => { close(); void copyRich(editor).then(() => toast('Copied to clipboard', 'success')); }}>Copy all to clipboard</MenuItem>
                <MenuItem icon={<Printer className="size-4" />} onClick={() => { close(); window.print(); }}>Print / save as PDF</MenuItem>
                <div className="my-1 border-t border-slate-100 dark:border-slate-800" />
                <MenuItem icon={showUnderlines ? <EyeOff className="size-4" /> : <Eye className="size-4" />} onClick={() => { close(); setShowUnderlines((v) => !v); }}>{showUnderlines ? 'Hide underlines' : 'Show underlines'}</MenuItem>
                <MenuItem icon={focusMode ? <Minimize2 className="size-4" /> : <Maximize2 className="size-4" />} onClick={() => { close(); setFocusMode((v) => !v); }}>{focusMode ? 'Exit focus mode' : 'Focus mode'}</MenuItem>
                <MenuItem icon={<Gauge className="size-4" />} onClick={() => { close(); setPerfOpen(true); }}>Performance</MenuItem>
              </>
            )}
          </Menu>
          <IconButton label={showPanel ? 'Hide assistant' : 'Show assistant'} onClick={() => { setFocusMode(false); setPanelOpen((v) => !(v && !focusMode)); }} className="hidden md:inline-flex">
            {showPanel ? <PanelRightClose className="size-5" /> : <PanelRightOpen className="size-5" />}
          </IconButton>
        </div>
        <input ref={fileRef} type="file" accept=".docx,.txt,.md,.markdown,.html,.htm" hidden onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) void importFile(f); }} />
      </header>

      {health && !health.nimConfigured && (
        <div className="nw-chrome border-b border-amber-200 bg-amber-50 px-4 py-2 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950/40 dark:text-amber-200">
          {health.packaged ? (
            <>
              AI checks are off until you add your NVIDIA API key: click the NimbleWrite icon in the notification area and choose <strong>Connect to NVIDIA…</strong> Instant local checks still work.
            </>
          ) : (
            <>
              No NVIDIA API key is configured, so AI checks are off. Instant local checks still work. Add <code>NVIDIA_API_KEY</code> to <code>.env</code> and restart the server.
            </>
          )}
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {/* editor column */}
        <div className="flex min-w-0 flex-1 flex-col">
          <div className="nw-scroll nw-print-root min-h-0 flex-1 overflow-y-auto">
            <div className={cx('mx-auto px-6 pb-40 pt-10 sm:px-12', focusMode ? 'max-w-3xl' : 'max-w-[760px]', !showUnderlines && 'nw-hide-sg')}>
              <EditorContent editor={editor} className="nw-editor font-serif text-slate-800 dark:text-slate-100" />
            </div>
          </div>
          {/* bottom bar */}
          <div className="nw-chrome flex h-12 shrink-0 items-center gap-2 border-t border-slate-200 bg-white px-3 dark:border-slate-800 dark:bg-slate-950">
            <div className="min-w-0 overflow-x-auto">
              <FormatToolbar editor={editor} />
            </div>
            <div className="ml-auto flex shrink-0 items-center gap-2">
              <ToneChip tone={tone} loading={toneLoading} words={stats.words} open={toneOpen} setOpen={setToneOpen} />
              <button onClick={() => setPerfOpen(true)} className="rounded-md px-2 py-1 text-xs text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800" title="Word count and performance">
                {stats.words.toLocaleString()} word{stats.words === 1 ? '' : 's'}
              </button>
            </div>
          </div>
        </div>

        {/* assistant panel */}
        {showPanel && (
          <aside className="nw-chrome fixed inset-y-14 right-14 z-30 flex w-[min(380px,calc(100vw-3.5rem))] flex-col border-l border-slate-200 bg-slate-50/95 shadow-xl backdrop-blur md:static md:z-auto md:w-[360px] md:shadow-none dark:border-slate-800 dark:bg-slate-900/95 xl:w-[380px]">
            {panel === 'suggestions' && (
              <SuggestionsPanel
                items={items}
                expandedId={expandedId}
                setExpandedId={(id) => {
                  setExpandedId(id);
                  setActiveSuggestion(editor.view, id);
                  const s = id ? items.find((x) => x.id === id) : undefined;
                  if (s) {
                    const dom = editor.view.domAtPos(s.from);
                    const el = (dom.node.nodeType === 3 ? dom.node.parentElement : (dom.node as HTMLElement)) as HTMLElement | null;
                    el?.scrollIntoView({ block: 'center', behavior: 'smooth' });
                  }
                }}
                onHoverItem={(id) => setActiveSuggestion(editor.view, id ?? expandedId)}
                actions={actions}
                score={score}
                onOpenPerformance={() => setPerfOpen(true)}
                status={statusText}
                onAcceptAll={acceptAll}
                checking={status.state === 'checking'}
              />
            )}
            {panel === 'assistant' && <AssistantPanel ctx={ctx} />}
            {(panel === 'paraphrase' || panel === 'humanize' || panel === 'translate') && <RewritePanel key={panel} ctx={ctx} kind={panel} />}
            {panel === 'reader' && <ReaderPanel ctx={ctx} />}
            {panel === 'grader' && <GraderPanel ctx={ctx} />}
            {panel === 'citations' && <CitationsPanel ctx={ctx} searchConfigured={!!health?.searchConfigured} packaged={!!health?.packaged} />}
            {panel === 'detector' && <DetectorPanel ctx={ctx} onHumanize={() => setPanel('humanize')} />}
            {panel === 'plagiarism' && <PlagiarismPanel ctx={ctx} searchConfigured={!!health?.searchConfigured} packaged={!!health?.packaged} />}
          </aside>
        )}

        {/* rail */}
        {!focusMode && (
          <nav className="nw-chrome flex w-14 shrink-0 flex-col items-center gap-1 border-l border-slate-200 bg-white py-3 dark:border-slate-800 dark:bg-slate-950" aria-label="Assistant tools">
            {PANELS.map((p) => {
              const Icon = p.icon;
              const active = showPanel && panel === p.id;
              return (
                <button
                  key={p.id}
                  title={p.label}
                  aria-label={p.label}
                  onClick={() => {
                    if (active) setPanelOpen(false);
                    else {
                      setPanel(p.id);
                      setPanelOpen(true);
                    }
                  }}
                  className={cx(
                    'relative flex size-10 items-center justify-center rounded-xl transition-colors',
                    active ? 'bg-brand-50 text-brand-700 dark:bg-brand-900/50 dark:text-brand-200' : 'text-slate-500 hover:bg-slate-100 hover:text-slate-800 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-100',
                  )}
                >
                  <Icon className="size-5" />
                  {p.id === 'suggestions' && items.length > 0 && (
                    <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-correctness px-1 text-[10px] font-semibold text-white">{items.length > 99 ? '99+' : items.length}</span>
                  )}
                </button>
              );
            })}
          </nav>
        )}
      </div>

      {popover && popoverItem && (
        <SuggestionPopover
          s={popoverItem}
          rect={popover.rect}
          actions={actions}
          onClose={closePopover}
          onKeepOpen={(on) => {
            overPopover.current = on;
            if (on && leaveTimer.current) clearTimeout(leaveTimer.current);
            if (!on && !popoverRef.current?.pinned) handlers.current.onHoverEnd();
          }}
        />
      )}
      {synonym && (
        <SynonymPopover word={synonym.word} rect={synonym.rect} loading={synonym.loading} senses={synonym.senses} error={synonym.error} onPick={pickSynonym} onClose={() => setSynonym(null)} />
      )}
      <GoalsModal
        open={goalsOpen}
        goals={goals}
        onChange={(g) => {
          setGoals(g);
          markDirty();
        }}
        onClose={() => setGoalsOpen(false)}
        showOnNew={settings.showGoalsOnNewDoc}
        onShowOnNew={(v) => void updateSettings({ showGoalsOnNewDoc: v })}
        onMakeDefault={() => {
          void updateSettings({ defaultGoals: goals });
          toast('New documents will start with these goals', 'success');
        }}
      />
      <PerformanceModal open={perfOpen} onClose={() => setPerfOpen(false)} score={score} stats={stats} counts={counts} tone={tone} title={title} />
    </div>
  );
}

function ToneChip({ tone, loading, words, open, setOpen }: { tone: ToneResult | null; loading: boolean; words: number; open: boolean; setOpen: (v: boolean) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false);
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open, setOpen]);
  if (words < 20 && !tone) return null;
  return (
    <div className="relative" ref={ref}>
      <button onClick={() => setOpen(!open)} className="flex items-center gap-1 rounded-full border border-slate-200 px-2 py-0.5 text-sm hover:bg-slate-50 dark:border-slate-700 dark:hover:bg-slate-800" title="Tone detector">
        {loading && !tone ? <Spinner className="size-3.5" /> : tone ? tone.tones.map((t) => <span key={t.name}>{t.emoji}</span>) : <span className="text-xs text-slate-400">Tone</span>}
      </button>
      {open && (
        <div className="nw-pop absolute bottom-9 right-0 z-40 w-72 rounded-xl border border-slate-200 bg-white p-4 shadow-xl dark:border-slate-700 dark:bg-slate-900">
          <div className="mb-2 flex items-center justify-between">
            <span className="text-sm font-semibold text-slate-900 dark:text-white">Here’s how your text sounds</span>
            {loading && <Spinner className="size-3.5" />}
          </div>
          {tone ? (
            <>
              <div className="space-y-2">
                {tone.tones.map((t) => (
                  <div key={t.name} className="flex items-center gap-2 text-sm">
                    <span className="text-lg">{t.emoji}</span>
                    <span className="w-28 text-slate-700 dark:text-slate-200">{t.name}</span>
                    <div className="h-1.5 flex-1 rounded-full bg-slate-100 dark:bg-slate-800">
                      <div className="h-full rounded-full bg-brand-500" style={{ width: `${t.score}%` }} />
                    </div>
                  </div>
                ))}
              </div>
              {tone.summary && <p className="mt-3 text-sm text-slate-600 dark:text-slate-400">{tone.summary}</p>}
              <div className="mt-3 grid grid-cols-2 gap-2 text-xs text-slate-500">
                <div className="rounded-lg bg-slate-50 p-2 dark:bg-slate-800/60">Formality<div className="font-semibold text-slate-800 dark:text-slate-100">{tone.formality}/100</div></div>
                <div className="rounded-lg bg-slate-50 p-2 dark:bg-slate-800/60">Sentiment<div className="font-semibold capitalize text-slate-800 dark:text-slate-100">{tone.sentiment}</div></div>
              </div>
            </>
          ) : (
            <p className="text-sm text-slate-500">Analysing your tone…</p>
          )}
          <p className="mt-3 text-[11px] text-slate-400">Detected by NVIDIA NIM from the whole document.</p>
        </div>
      )}
    </div>
  );
}
