// Shared building blocks for the AI panels: target picker, streaming runner and result card.
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { Editor } from '@tiptap/core';
import { ArrowRight, Check, ClipboardCheck, Copy, RotateCcw, Square } from 'lucide-react';
import type { WriteRequest } from '../../../shared/types';
import { countWords, diffWords } from '../../../shared/text';
import { streamWrite } from '../../api';
import { toast } from '../../store';
import { Button, cx, Spinner } from '../../components/ui';
import { getTarget, insertAfter, markdownToHtml, replaceRange, type Target, type TargetKind } from '../editorUtils';

export interface PanelContext {
  editor: Editor;
  docId: string;
  title: string;
}

export function PanelShell({ icon, title, subtitle, children, footer }: { icon: ReactNode; title: string; subtitle?: string; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex h-full flex-col">
      <div className="flex items-start gap-3 border-b border-slate-100 px-4 py-3.5 dark:border-slate-800">
        <div className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-700 dark:bg-brand-900/40 dark:text-brand-200">{icon}</div>
        <div className="min-w-0">
          <h2 className="text-[15px] font-semibold text-slate-900 dark:text-white">{title}</h2>
          {subtitle && <p className="mt-0.5 text-xs leading-snug text-slate-500 dark:text-slate-400">{subtitle}</p>}
        </div>
      </div>
      <div className="nw-scroll flex-1 space-y-4 overflow-y-auto px-4 py-4">{children}</div>
      {footer && <div className="border-t border-slate-100 px-4 py-3 dark:border-slate-800">{footer}</div>}
    </div>
  );
}

/** Live info about the editor selection so panels can show what they will work on. */
export function useSelectionInfo(editor: Editor): { hasSelection: boolean; selectionWords: number; paragraphWords: number; docWords: number } {
  const read = useCallback(() => {
    const sel = getTarget(editor, 'selection');
    const para = getTarget(editor, 'paragraph');
    const doc = getTarget(editor, 'document');
    return {
      hasSelection: sel.kind === 'selection',
      selectionWords: sel.kind === 'selection' ? countWords(sel.text) : 0,
      paragraphWords: para.kind === 'paragraph' ? countWords(para.text) : 0,
      docWords: countWords(doc.text),
    };
  }, [editor]);
  const [info, setInfo] = useState(read);
  useEffect(() => {
    const update = () => setInfo(read());
    editor.on('selectionUpdate', update);
    editor.on('update', update);
    return () => {
      editor.off('selectionUpdate', update);
      editor.off('update', update);
    };
  }, [editor, read]);
  return info;
}

export type TargetChoice = TargetKind | 'new';

export function TargetPicker({ editor, value, onChange, allowNew, allowParagraph = true }: { editor: Editor; value: TargetChoice | 'auto'; onChange: (v: TargetChoice) => void; allowNew?: boolean; allowParagraph?: boolean }) {
  const info = useSelectionInfo(editor);
  const effective: TargetChoice = value === 'auto' ? (info.hasSelection ? 'selection' : allowParagraph && info.paragraphWords ? 'paragraph' : 'document') : value;
  const opts: { id: TargetChoice; label: string; detail: string; disabled?: boolean }[] = [
    { id: 'selection', label: 'Selection', detail: info.hasSelection ? `${info.selectionWords} words` : 'none', disabled: !info.hasSelection },
    ...(allowParagraph ? [{ id: 'paragraph' as const, label: 'Paragraph', detail: `${info.paragraphWords} words`, disabled: !info.paragraphWords }] : []),
    { id: 'document', label: 'Document', detail: `${info.docWords} words`, disabled: !info.docWords },
    ...(allowNew ? [{ id: 'new' as const, label: 'New text', detail: 'at cursor' }] : []),
  ];
  return (
    <div>
      <div className="mb-1.5 text-xs font-medium text-slate-500 dark:text-slate-400">Work on</div>
      <div className="flex rounded-lg bg-slate-100 p-0.5 dark:bg-slate-800">
        {opts.map((o) => (
          <button
            key={o.id}
            disabled={o.disabled}
            onClick={() => onChange(o.id)}
            className={cx(
              'flex-1 rounded-md px-1.5 py-1 text-center text-xs transition-colors disabled:opacity-40',
              effective === o.id ? 'bg-white font-semibold text-slate-900 shadow-sm dark:bg-slate-950 dark:text-white' : 'text-slate-600 hover:text-slate-900 dark:text-slate-400',
            )}
          >
            {o.label}
            <span className="block text-[10px] font-normal text-slate-400">{o.detail}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

export function resolveTarget(editor: Editor, choice: TargetChoice | 'auto'): Target | null {
  if (choice === 'new') {
    const pos = editor.state.selection.to;
    return { kind: 'selection', from: pos, to: pos, text: '' };
  }
  if (choice === 'auto') return getTarget(editor, 'auto');
  const t = getTarget(editor, choice);
  if (choice === 'selection' && t.kind !== 'selection') return null;
  return t;
}

export interface RunState {
  text: string;
  running: boolean;
  error: string | null;
  target: Target | null;
  request: WriteRequest | null;
  model: string;
  ms: number;
}

/** Runs a streaming write request and keeps the output, original target and errors. */
export function useWriter() {
  const [state, setState] = useState<RunState>({ text: '', running: false, error: null, target: null, request: null, model: '', ms: 0 });
  const abortRef = useRef<AbortController | null>(null);
  const run = useCallback(async (request: WriteRequest, target: Target | null) => {
    abortRef.current?.abort();
    const ac = new AbortController();
    abortRef.current = ac;
    setState({ text: '', running: true, error: null, target, request, model: '', ms: 0 });
    try {
      const r = await streamWrite(request, (chunk) => setState((s) => ({ ...s, text: s.text + chunk })), ac.signal);
      setState((s) => ({ ...s, running: false, model: r.model, ms: r.ms, text: r.text || s.text }));
    } catch (e) {
      if ((e as Error).name === 'AbortError') setState((s) => ({ ...s, running: false }));
      else setState((s) => ({ ...s, running: false, error: (e as Error).message }));
    }
  }, []);
  const stop = useCallback(() => abortRef.current?.abort(), []);
  const reset = useCallback(() => {
    abortRef.current?.abort();
    setState({ text: '', running: false, error: null, target: null, request: null, model: '', ms: 0 });
  }, []);
  useEffect(() => () => abortRef.current?.abort(), []);
  return { ...state, run, stop, reset };
}

export function ResultCard({ editor, writer, mode }: { editor: Editor; writer: ReturnType<typeof useWriter>; mode: 'replace' | 'insert' }) {
  const [showDiff, setShowDiff] = useState(true);
  const [copied, setCopied] = useState(false);
  const { text, running, error, target, request } = writer;
  // A word diff only makes sense when the output is a revision of the same text (not a translation).
  const canDiff = mode === 'replace' && !!target?.text && !running && !!text && request?.task !== 'translate';
  const diff = useMemo(() => (canDiff && showDiff ? diffWords(target!.text, text.trim()) : null), [canDiff, showDiff, target, text]);
  if (!running && !text && !error) return null;

  const replace = () => {
    if (!target) return;
    if (!replaceRange(editor, target, text)) {
      toast('The original text changed, so the result was inserted below instead.');
      insertAfter(editor, target.to, text);
    } else toast('Replaced', 'success');
    writer.reset();
  };
  const insert = () => {
    const pos = target ? target.to : editor.state.selection.to;
    if (target && target.from === target.to) {
      editor.chain().focus().insertContentAt(pos, markdownToHtml(text.trim())).run();
    } else insertAfter(editor, pos, text);
    toast('Inserted', 'success');
    writer.reset();
  };
  const copy = async () => {
    await navigator.clipboard.writeText(text.trim());
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  return (
    <div className="nw-pop rounded-xl border border-brand-200 bg-brand-50/40 dark:border-brand-800 dark:bg-brand-950/30">
      <div className="flex items-center justify-between gap-2 px-3.5 pt-3">
        <span className="flex min-w-0 items-center gap-1.5 text-xs font-semibold text-brand-700 dark:text-brand-300" title={writer.model || undefined}>
          {running ? <Spinner className="size-3.5" /> : <Check className="size-3.5" />}
          {running ? 'Writing…' : 'Result'}
          {writer.model && !running && <span className="truncate font-normal text-slate-400">· {(writer.ms / 1000).toFixed(1)} s</span>}
        </span>
        {canDiff && (
          <label className="flex shrink-0 cursor-pointer items-center gap-1 text-xs text-slate-500 dark:text-slate-400">
            <input type="checkbox" checked={showDiff} onChange={(e) => setShowDiff(e.target.checked)} className="accent-brand-600" /> Show changes
          </label>
        )}
      </div>
      <div className="px-3.5 py-2.5 text-[14.5px] leading-relaxed text-slate-800 dark:text-slate-100">
        {error ? (
          <p className="text-red-600">{error}</p>
        ) : diff ? (
          <p className="whitespace-pre-wrap">
            {diff.map((p, i) =>
              p.type === 'same' ? <span key={i}>{p.text}</span>
                : p.type === 'add' ? <span key={i} className="rounded-sm bg-emerald-100 text-emerald-900 dark:bg-emerald-900/50 dark:text-emerald-100">{p.text}</span>
                  : <span key={i} className="rounded-sm bg-red-100 text-red-800 line-through decoration-red-400 dark:bg-red-900/40 dark:text-red-200">{p.text}</span>,
            )}
          </p>
        ) : (
          // markdownToHtml() runs the model output through DOMPurify before it is rendered.
          <div className="nw-prose" dangerouslySetInnerHTML={{ __html: markdownToHtml(text || ' ') }} />
        )}
      </div>
      <div className="flex flex-wrap items-center gap-1.5 border-t border-brand-100 px-3 py-2 dark:border-brand-900">
        {running ? (
          <Button size="sm" variant="secondary" onClick={writer.stop}><Square className="size-3.5" /> Stop</Button>
        ) : (
          <>
            {!error && mode === 'replace' && target && target.text && <Button size="sm" variant="primary" onClick={replace}><Check className="size-4" /> Replace</Button>}
            {!error && <Button size="sm" variant={mode === 'insert' ? 'primary' : 'secondary'} onClick={insert}><ArrowRight className="size-4" /> Insert</Button>}
            {!error && <Button size="sm" variant="ghost" onClick={copy}>{copied ? <ClipboardCheck className="size-4" /> : <Copy className="size-4" />} Copy</Button>}
            {request && <Button size="sm" variant="ghost" onClick={() => writer.run(request, target)}><RotateCcw className="size-4" /> Retry</Button>}
            <Button size="sm" variant="ghost" className="ml-auto" onClick={writer.reset}>Discard</Button>
          </>
        )}
      </div>
    </div>
  );
}

export function useAsync<T>() {
  const [state, setState] = useState<{ data: T | null; loading: boolean; error: string | null }>({ data: null, loading: false, error: null });
  const run = useCallback(async (fn: () => Promise<T>) => {
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const data = await fn();
      setState({ data, loading: false, error: null });
      return data;
    } catch (e) {
      setState((s) => ({ ...s, loading: false, error: (e as Error).message }));
      return null;
    }
  }, []);
  return { ...state, run, setData: (data: T | null) => setState((s) => ({ ...s, data })) };
}

export function ErrorNote({ message }: { message: string | null }) {
  if (!message) return null;
  return <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">{message}</div>;
}

export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-slate-500 dark:text-slate-400">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-[11px] text-slate-400">{hint}</span>}
    </label>
  );
}

export const inputClass = 'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 placeholder:text-slate-400 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-100 dark:border-slate-700 dark:bg-slate-950 dark:text-slate-100 dark:focus:ring-brand-900';
