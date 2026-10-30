// "Tune suggestions": tell NimbleWrite in plain language how you want your suggestions to change.
// The model proposes settings changes; nothing is applied until you press Apply.
import { useRef, useState } from 'react';
import { ChevronDown, Send, SlidersHorizontal } from 'lucide-react';
import type { TuneChange, TuneResult } from '../../shared/types';
import { applyTuneChanges } from '../../shared/tune';
import { api } from '../api';
import { toast, useSettings } from '../store';
import { Button, cx, Spinner } from '../components/ui';
import { inputClass } from './panels/common';

const EXAMPLES = ['Stop flagging passive voice', 'Allow sentence fragments', 'Never touch my spelling of names and brands'];

const uid = () => Math.random().toString(36).slice(2, 10);

export function describeChange(c: TuneChange): string {
  switch (c.kind) {
    case 'instruction': return `Preference: “${c.text}”`;
    case 'mute': return `Stop suggesting: ${c.title}`;
    case 'unmute': return `Bring back: ${c.title}`;
    case 'dictionary': return `Add “${c.word}” to your dictionary`;
    case 'style_rule': return c.replace ? `Style rule: “${c.find}” → “${c.replace}”` : `Style rule: flag “${c.find}”`;
  }
}

export function TuneBox() {
  const settings = useSettings((s) => s.settings);
  const update = useSettings((s) => s.update);
  const [open, setOpen] = useState(false);
  const [text, setText] = useState('');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<TuneResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const abort = useRef<AbortController | null>(null);

  const send = async (message = text) => {
    const msg = message.trim();
    if (msg.length < 2 || loading) return;
    abort.current?.abort();
    const ac = new AbortController();
    abort.current = ac;
    setText(msg);
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      setResult(await api.tune(msg, ac.signal));
    } catch (e) {
      if ((e as Error).name !== 'AbortError') setError((e as Error).message);
    } finally {
      if (abort.current === ac) setLoading(false);
    }
  };

  const apply = async () => {
    if (!result || !settings) return;
    const { patch, applied, skipped } = applyTuneChanges(settings, result.changes, uid);
    if (!applied) {
      toast('Those changes are already in place.');
    } else {
      await update(patch);
      toast(`Applied ${applied} change${applied === 1 ? '' : 's'}${skipped ? ` (${skipped} already in place)` : ''}. Review them any time in Settings → Suggestions.`, 'success');
    }
    setResult(null);
    setText('');
  };

  const close = () => {
    abort.current?.abort();
    setOpen(false);
    setLoading(false);
  };

  return (
    <div className="border-t border-slate-100 dark:border-slate-800">
      <button
        onClick={() => (open ? close() : setOpen(true))}
        className="flex w-full items-center gap-2 px-4 py-2.5 text-left text-[13px] font-medium text-slate-600 hover:bg-slate-100/70 dark:text-slate-300 dark:hover:bg-slate-800/60"
        aria-expanded={open}
      >
        <SlidersHorizontal className="size-4 text-brand-600 dark:text-brand-300" />
        Tune suggestions
        <ChevronDown className={cx('ml-auto size-4 text-slate-400 transition-transform', open && 'rotate-180')} />
      </button>
      {open && (
        <div className="space-y-2.5 px-4 pb-3.5">
          <p className="text-xs leading-snug text-slate-500 dark:text-slate-400">Tell NimbleWrite what you do and don’t want to be corrected on. You’ll see the changes before anything is applied.</p>
          <div className="relative">
            <textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  void send();
                }
              }}
              rows={2}
              maxLength={800}
              placeholder="e.g. Stop flagging passive voice"
              className={cx(inputClass, 'resize-none pr-11')}
            />
            <button
              onClick={() => void send()}
              disabled={text.trim().length < 2 || loading}
              className="absolute bottom-2 right-2 flex size-8 items-center justify-center rounded-lg bg-brand-600 text-white hover:bg-brand-700 disabled:opacity-40"
              title="Send (Enter)"
              aria-label="Send"
            >
              {loading ? <Spinner className="size-4" /> : <Send className="size-4" />}
            </button>
          </div>
          {!result && !loading && (
            <div className="flex flex-wrap gap-1.5">
              {EXAMPLES.map((x) => (
                <button key={x} onClick={() => void send(x)} className="rounded-full border border-slate-200 bg-white px-2.5 py-1 text-xs text-slate-600 hover:border-brand-300 hover:bg-brand-50 hover:text-brand-800 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-300 dark:hover:bg-brand-900/40">
                  {x}
                </button>
              ))}
            </div>
          )}
          {error && <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">{error}</p>}
          {result && (
            <div className="nw-pop space-y-2.5 rounded-xl border border-brand-200 bg-brand-50/40 p-3 text-sm dark:border-brand-800 dark:bg-brand-950/30">
              <p className="text-slate-700 dark:text-slate-200">{result.reply}</p>
              {result.changes.length > 0 && (
                <>
                  <ul className="space-y-1 text-[13px] text-slate-700 dark:text-slate-200">
                    {result.changes.map((c, i) => (
                      <li key={i} className="flex gap-2"><span className="text-brand-600 dark:text-brand-300">•</span>{describeChange(c)}</li>
                    ))}
                  </ul>
                  <div className="flex gap-1.5">
                    <Button size="sm" variant="primary" onClick={() => void apply()}>Apply {result.changes.length === 1 ? 'change' : `${result.changes.length} changes`}</Button>
                    <Button size="sm" variant="ghost" onClick={() => setResult(null)}>Dismiss</Button>
                  </div>
                </>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
