// Blank-page help: shown under the cursor in an empty document. Pick a starter or describe what you need,
// and NimbleWrite drafts it for you to insert. It disappears as soon as there is text.
import { useRef, useState } from 'react';
import type { Editor } from '@tiptap/core';
import { Lightbulb, Send } from 'lucide-react';
import type { Goals } from '../../shared/types';
import { toast, useSettings } from '../store';
import { cx, Spinner } from '../components/ui';
import { inputClass, ResultCard, resolveTarget, useWriter } from './panels/common';

const STARTERS: { label: string; prompt: string }[] = [
  { label: 'Draft an email', prompt: 'Write an email to ' },
  { label: 'Outline an essay', prompt: 'Create an outline for an essay about ' },
  { label: 'Brainstorm ideas', prompt: 'Brainstorm ideas for ' },
  { label: 'Opening paragraph', prompt: 'Write an opening paragraph about ' },
  { label: 'Cover letter', prompt: 'Write a cover letter for a job as ' },
  { label: 'Social post', prompt: 'Write a short LinkedIn post about ' },
];

export function Starters({ editor, goals }: { editor: Editor; goals: Goals }) {
  const dialect = useSettings((s) => s.settings?.dialect);
  const writer = useWriter();
  const [prompt, setPrompt] = useState('');
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const pick = (p: string) => {
    setPrompt(p);
    requestAnimationFrame(() => {
      const el = inputRef.current;
      if (!el) return;
      el.focus();
      el.setSelectionRange(p.length, p.length);
    });
  };

  const draft = () => {
    const instruction = prompt.trim();
    if (instruction.length < 3 || writer.running) return toast('Say a little more about what you want to write.');
    void writer.run({ task: 'compose', text: '', instruction, goals, dialect }, resolveTarget(editor, 'new'));
  };

  return (
    <div className="nw-chrome absolute left-0 right-0 top-14 max-w-xl rounded-2xl border border-slate-200 bg-slate-50/70 p-4 dark:border-slate-800 dark:bg-slate-900/50">
      <div className="mb-2.5 flex items-center gap-2 text-sm font-medium text-slate-700 dark:text-slate-200">
        <Lightbulb className="size-4 text-brand-600 dark:text-brand-300" /> Not sure where to start?
      </div>
      <div className="mb-3 flex flex-wrap gap-1.5">
        {STARTERS.map((s) => (
          <button
            key={s.label}
            onClick={() => pick(s.prompt)}
            disabled={writer.running}
            className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-[13px] text-slate-700 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-800 disabled:opacity-50 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:hover:border-brand-700 dark:hover:bg-brand-900/40 dark:hover:text-brand-100"
          >
            {s.label}
          </button>
        ))}
      </div>
      <div className="relative">
        <textarea
          ref={inputRef}
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault();
              draft();
            }
          }}
          rows={2}
          maxLength={1000}
          placeholder="Or describe what you want to write…"
          aria-label="What do you want to write?"
          className={cx(inputClass, 'resize-none pr-11')}
        />
        <button
          onClick={draft}
          disabled={prompt.trim().length < 3 || writer.running}
          className="absolute bottom-2 right-2 flex size-8 items-center justify-center rounded-lg bg-brand-600 text-white hover:bg-brand-700 disabled:opacity-40"
          title="Draft it (Enter)"
          aria-label="Draft it"
        >
          {writer.running ? <Spinner className="size-4" /> : <Send className="size-4" />}
        </button>
      </div>
      <div className="mt-3 empty:hidden">
        <ResultCard editor={editor} writer={writer} mode="insert" />
      </div>
    </div>
  );
}
