// Goals and Performance dialogs.
import { useMemo } from 'react';
import { Download } from 'lucide-react';
import type { Category, Goals, ToneResult } from '../../shared/types';
import { CATEGORIES, DEFAULT_GOALS } from '../../shared/types';
import { CATEGORY_META, GOAL_OPTIONS } from '../../shared/meta';
import { formatDuration, readabilityLabel, type TextStats } from '../../shared/text';
import { Button, Modal, Pill, ScoreRing } from '../components/ui';

export function GoalsPicker({ goals, onChange }: { goals: Goals; onChange: (g: Goals) => void }) {
  return (
    <div className="space-y-5">
      <Section title="Audience" hint={GOAL_OPTIONS.audience.find((o) => o.id === goals.audience)?.hint}>
        {GOAL_OPTIONS.audience.map((o) => (
          <Pill key={o.id} selected={goals.audience === o.id} onClick={() => onChange({ ...goals, audience: o.id })} title={o.hint}>{o.label}</Pill>
        ))}
      </Section>
      <Section title="Formality" hint={GOAL_OPTIONS.formality.find((o) => o.id === goals.formality)?.hint}>
        {GOAL_OPTIONS.formality.map((o) => (
          <Pill key={o.id} selected={goals.formality === o.id} onClick={() => onChange({ ...goals, formality: o.id })} title={o.hint}>{o.label}</Pill>
        ))}
      </Section>
      <Section title="Domain" hint={GOAL_OPTIONS.domain.find((o) => o.id === goals.domain)?.hint}>
        {GOAL_OPTIONS.domain.map((o) => (
          <Pill key={o.id} selected={goals.domain === o.id} onClick={() => onChange({ ...goals, domain: o.id })} title={o.hint}>{o.label}</Pill>
        ))}
      </Section>
      <Section title="Intent" hint="Pick any that apply. NimbleWrite tailors word-choice and tone suggestions to them.">
        {GOAL_OPTIONS.intent.map((o) => (
          <Pill
            key={o.id}
            selected={goals.intent.includes(o.id)}
            title={o.hint}
            onClick={() => onChange({ ...goals, intent: goals.intent.includes(o.id) ? goals.intent.filter((i) => i !== o.id) : [...goals.intent, o.id] })}
          >
            {o.label}
          </Pill>
        ))}
      </Section>
    </div>
  );
}

function Section({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="mb-2 text-sm font-semibold text-slate-800 dark:text-slate-100">{title}</div>
      <div className="flex flex-wrap gap-2">{children}</div>
      {hint && <p className="mt-1.5 text-xs text-slate-500 dark:text-slate-400">{hint}</p>}
    </div>
  );
}

export function GoalsModal({ open, goals, onChange, onClose, showOnNew, onShowOnNew, onMakeDefault }: {
  open: boolean;
  goals: Goals;
  onChange: (g: Goals) => void;
  onClose: () => void;
  showOnNew: boolean;
  onShowOnNew: (v: boolean) => void;
  onMakeDefault: () => void;
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Set goals"
      width="max-w-xl"
      footer={
        <>
          <Button variant="ghost" onClick={() => onChange(DEFAULT_GOALS)}>Reset to defaults</Button>
          <Button variant="secondary" onClick={onMakeDefault}>Use for new documents</Button>
          <Button variant="primary" onClick={onClose}>Done</Button>
        </>
      }
    >
      <p className="mb-5 text-sm text-slate-600 dark:text-slate-400">Get tailored writing suggestions based on your goals and audience. NimbleWrite re-checks the document when these change.</p>
      <GoalsPicker goals={goals} onChange={onChange} />
      <label className="mt-6 flex items-center gap-2 text-sm text-slate-600 dark:text-slate-400">
        <input type="checkbox" checked={showOnNew} onChange={(e) => onShowOnNew(e.target.checked)} className="size-4 accent-brand-600" />
        Show Set Goals when I start a new document
      </label>
    </Modal>
  );
}

export function PerformanceModal({ open, onClose, score, stats, counts, tone, title }: {
  open: boolean;
  onClose: () => void;
  score: number | null;
  stats: TextStats;
  counts: Record<Category, number>;
  tone: ToneResult | null;
  title: string;
}) {
  const read = readabilityLabel(stats.readability);
  const total = CATEGORIES.reduce((s, c) => s + counts[c], 0);
  const report = useMemo(() => {
    const lines = [
      `NimbleWrite performance report: ${title}`,
      `Generated ${new Date().toLocaleString('en-GB')}`,
      '',
      `Overall score: ${score ?? 'n/a'} / 100`,
      '',
      'Word count',
      `  Characters: ${stats.characters}`, `  Words: ${stats.words}`, `  Sentences: ${stats.sentences}`,
      `  Reading time: ${formatDuration(stats.readingTimeSec)}`, `  Speaking time: ${formatDuration(stats.speakingTimeSec)}`,
      '',
      'Readability',
      `  Word length: ${stats.avgWordLength.toFixed(1)} characters`, `  Sentence length: ${stats.avgSentenceLength.toFixed(1)} words`,
      `  Readability score: ${stats.readability} (${read.label}; likely understood by ${read.audience})`,
      '',
      'Vocabulary',
      `  Unique words: ${stats.uniqueWordsPct}%`, `  Rare words: ${stats.rareWordsPct}%`,
      '',
      'Open suggestions',
      ...CATEGORIES.map((c) => `  ${CATEGORY_META[c].label}: ${counts[c]}`),
      tone ? `\nTone: ${tone.tones.map((t) => t.name).join(', ')}. ${tone.summary}` : '',
    ];
    return lines.join('\n');
  }, [title, score, stats, counts, tone, read]);

  const download = () => {
    const url = URL.createObjectURL(new Blob([report], { type: 'text/plain;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = `${title || 'document'} - performance.txt`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 3000);
  };

  return (
    <Modal open={open} onClose={onClose} title="Performance" width="max-w-2xl" footer={<Button onClick={download}><Download className="size-4" /> Download report</Button>}>
      <div className="flex items-center gap-5 rounded-xl bg-slate-50 p-4 dark:bg-slate-800/60">
        <span className="relative">
          <ScoreRing score={score} size={84} stroke={7} />
          <span className="absolute inset-0 flex items-center justify-center text-2xl font-bold text-slate-900 dark:text-white">{score ?? '–'}</span>
        </span>
        <div>
          <div className="font-semibold text-slate-900 dark:text-white">Text score: {score ?? '–'} out of 100</div>
          <p className="mt-1 text-sm text-slate-600 dark:text-slate-400">
            This score reflects the quality of writing in this document, weighted by the {total} open suggestion{total === 1 ? '' : 's'} and how readable it is. Address suggestions to raise it.
          </p>
        </div>
      </div>

      <div className="mt-6 grid gap-6 sm:grid-cols-2">
        <Metrics title="Word count" rows={[
          ['Characters', stats.characters.toLocaleString()],
          ['Words', stats.words.toLocaleString()],
          ['Sentences', stats.sentences.toLocaleString()],
          ['Reading time', formatDuration(stats.readingTimeSec)],
          ['Speaking time', formatDuration(stats.speakingTimeSec)],
        ]} />
        <Metrics title="Readability" rows={[
          ['Word length', `${stats.avgWordLength.toFixed(1)}`],
          ['Sentence length', `${stats.avgSentenceLength.toFixed(1)}`],
          ['Readability score', `${stats.readability}`],
          ['Grade level', `${stats.gradeLevel}`],
          ['Long sentences (30+ words)', `${stats.longSentences}`],
        ]} note={stats.words ? `${read.label}. Your text is likely to be understood by ${read.audience}.` : undefined} />
        <Metrics title="Vocabulary" rows={[
          ['Unique words', `${stats.uniqueWordsPct}%`],
          ['Rare words', `${stats.rareWordsPct}%`],
        ]} note="Unique words: share of words used only once. Rare words: words outside the 5,000 most common English words." />
        <div>
          <div className="mb-2 text-sm font-semibold text-slate-800 dark:text-slate-100">Open suggestions</div>
          <div className="space-y-2">
            {CATEGORIES.map((c) => (
              <div key={c} className="flex items-center gap-2 text-sm">
                <span className="w-24 text-slate-600 dark:text-slate-400">{CATEGORY_META[c].label}</span>
                <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-800">
                  <div className="h-full rounded-full" style={{ width: `${total ? (counts[c] / total) * 100 : 0}%`, background: CATEGORY_META[c].color }} />
                </div>
                <span className="w-6 text-right font-medium">{counts[c]}</span>
              </div>
            ))}
          </div>
          {tone && (
            <div className="mt-4 text-sm">
              <div className="mb-1 font-semibold text-slate-800 dark:text-slate-100">Tone</div>
              <div className="flex flex-wrap gap-1.5">
                {tone.tones.map((t) => (
                  <span key={t.name} className="rounded-full bg-slate-100 px-2 py-0.5 dark:bg-slate-800">{t.emoji} {t.name}</span>
                ))}
              </div>
              {tone.summary && <p className="mt-1.5 text-slate-600 dark:text-slate-400">{tone.summary}</p>}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
}

function Metrics({ title, rows, note }: { title: string; rows: [string, string][]; note?: string }) {
  return (
    <div>
      <div className="mb-2 text-sm font-semibold text-slate-800 dark:text-slate-100">{title}</div>
      <dl className="divide-y divide-slate-100 text-sm dark:divide-slate-800">
        {rows.map(([k, v]) => (
          <div key={k} className="flex justify-between py-1.5">
            <dt className="text-slate-600 dark:text-slate-400">{k}</dt>
            <dd className="font-medium text-slate-900 dark:text-slate-100">{v}</dd>
          </div>
        ))}
      </dl>
      {note && <p className="mt-2 text-xs text-slate-500 dark:text-slate-400">{note}</p>}
    </div>
  );
}
