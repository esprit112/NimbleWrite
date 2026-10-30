// Incremental checking engine: only paragraphs whose text changed are sent to NVIDIA NIM.
// Results are cached per paragraph hash, local rules run instantly, and accepted fixes update
// the cache directly so accepting a suggestion never costs another model call.
import type { Editor } from '@tiptap/core';
import type { Category, Dialect, Goals, Issue, StyleRule } from '../../shared/types';
import { dedupeIssues, runLocalRules } from '../../shared/rules';
import { countWords, hashText } from '../../shared/text';
import { api } from '../api';
import { extractBlocks, type Block } from './blocks';
import { getSuggestions, setSuggestions, type PlacedSuggestion } from './suggestions';

export interface CheckerContext {
  dialect: Dialect;
  goals: Goals;
  categories: Record<Category, boolean>;
  dictionary: string[];
  styleRules: StyleRule[];
  localRules: boolean;
  dismissed: Set<string>;
  model: string;
  instructions: string;
  mutedTypes: string[];
}

export type CheckerStatus = { state: 'idle' | 'checking' | 'error'; message?: string; pending: number };

const MAX_BATCH_CHARS = 1100;
const MAX_BATCH_PARAS = 6;
const MAX_PARALLEL = 3;

export function signature(blockText: string, i: Issue): string {
  return hashText(`${blockText.slice(Math.max(0, i.offset - 24), i.offset)}|${i.original}|${i.replacements[0] ?? ''}|${i.type}`);
}

export class Checker {
  private cache = new Map<string, Issue[]>();
  private ruleCache = new Map<string, Issue[]>();
  private rulesFp = '';
  private pending = new Set<string>();
  private queue: Block[][] = [];
  private active = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private publishTimer: ReturnType<typeof setTimeout> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;
  private lastError = '';

  constructor(
    private editor: Editor,
    private ctx: () => CheckerContext,
    private onStatus: (s: CheckerStatus) => void,
  ) {}

  destroy(): void {
    this.destroyed = true;
    for (const t of [this.timer, this.publishTimer, this.retryTimer]) if (t) clearTimeout(t);
  }

  /** Fingerprint of everything that changes what the model would say. */
  private fp(c: CheckerContext): string {
    const cats = (Object.keys(c.categories) as Category[]).filter((k) => c.categories[k]).sort();
    return hashText(JSON.stringify([c.dialect, c.goals, cats, c.model, c.instructions]));
  }

  private emit(): void {
    const pending = this.pending.size;
    if (pending > 0) this.onStatus({ state: 'checking', pending });
    else if (this.lastError) this.onStatus({ state: 'error', message: this.lastError, pending: 0 });
    else this.onStatus({ state: 'idle', pending: 0 });
  }

  /** Call on every document change. Local rules update quickly; the model runs after a pause. */
  onDocChange(): void {
    if (this.publishTimer) clearTimeout(this.publishTimer);
    this.publishTimer = setTimeout(() => this.publish(), 160);
    this.schedule(900);
  }

  schedule(delay = 900): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.run(), delay);
  }

  /** Settings or goals changed: re-publish and re-check whatever is not cached for the new settings. */
  refresh(): void {
    this.publish();
    this.schedule(50);
  }

  run(): void {
    if (this.destroyed || this.editor.isDestroyed) return;
    const c = this.ctx();
    const fp = this.fp(c);
    const seen = new Set<string>();
    const todo = extractBlocks(this.editor.state.doc).filter((b) => {
      const k = `${fp}:${b.key}`;
      if (seen.has(k) || this.cache.has(k) || this.pending.has(k) || !/[A-Za-z\u00C0-\u024F]/.test(b.text) || countWords(b.text) < 1) return false;
      seen.add(k);
      return true;
    });
    if (!todo.length) {
      this.emit();
      return;
    }
    let batch: Block[] = [];
    let size = 0;
    for (const b of todo) {
      if (batch.length && (size + b.text.length > MAX_BATCH_CHARS || batch.length >= MAX_BATCH_PARAS)) {
        this.queue.push(batch);
        batch = [];
        size = 0;
      }
      batch.push(b);
      size += b.text.length;
      this.pending.add(`${fp}:${b.key}`);
    }
    if (batch.length) this.queue.push(batch);
    this.emit();
    this.pump(fp, c);
  }

  private pump(fp: string, c: CheckerContext): void {
    while (this.active < MAX_PARALLEL && this.queue.length) {
      const batch = this.queue.shift()!;
      this.active++;
      const categories = (Object.keys(c.categories) as Category[]).filter((k) => c.categories[k]);
      api
        .check(batch.map((b) => ({ id: b.key, text: b.text })), { dialect: c.dialect, goals: c.goals, dictionary: c.dictionary, categories, model: c.model, instructions: c.instructions })
        .then((res) => {
          for (const r of res.results) this.store(`${fp}:${r.id}`, r.issues);
          this.lastError = '';
        })
        .catch((err: Error) => {
          this.lastError = err.message;
          if (this.retryTimer) clearTimeout(this.retryTimer);
          this.retryTimer = setTimeout(() => this.schedule(0), 12_000);
        })
        .finally(() => {
          for (const b of batch) this.pending.delete(`${fp}:${b.key}`);
          this.active--;
          if (!this.destroyed) {
            this.publish();
            this.emit();
            this.pump(fp, c);
          }
        });
    }
  }

  private store(key: string, issues: Issue[]): void {
    this.cache.set(key, issues);
    if (this.cache.size > 3000) this.cache.delete(this.cache.keys().next().value as string);
  }

  /** Rebuild the full suggestion list for the current document and hand it to the editor plugin. */
  publish(): void {
    if (this.destroyed || this.editor.isDestroyed) return;
    const c = this.ctx();
    const fp = this.fp(c);
    const view = this.editor.view;
    const current = getSuggestions(view.state).items;
    const dict = new Set(c.dictionary.map((w) => w.toLowerCase()));
    // Muting only filters what is shown, so un-muting brings the cached suggestions straight back.
    const muted = new Set(c.mutedTypes);
    const rulesFp = hashText(JSON.stringify([c.dialect, c.styleRules, c.localRules]));
    if (rulesFp !== this.rulesFp) {
      this.ruleCache.clear();
      this.rulesFp = rulesFp;
    }
    const out: PlacedSuggestion[] = [];
    for (const b of extractBlocks(view.state.doc)) {
      const end = b.from + b.text.length;
      let rules = this.ruleCache.get(b.key);
      if (!rules) {
        rules = runLocalRules(b.text, { dialect: c.dialect, styleRules: c.styleRules, mechanics: c.localRules });
        this.ruleCache.set(b.key, rules);
        if (this.ruleCache.size > 4000) this.ruleCache.delete(this.ruleCache.keys().next().value as string);
      }
      const cached = this.cache.get(`${fp}:${b.key}`);
      // While a changed paragraph waits for the model, keep its still-valid AI underlines.
      const ai = cached ?? current.filter((s) => s.source === 'ai' && s.from >= b.from && s.to <= end).map((s) => toRelative(s, b));
      for (const i of dedupeIssues([...ai, ...rules].filter((x) => !muted.has(x.type)))) {
        if (!c.categories[i.category]) continue;
        if ((i.type === 'spelling' || i.type === 'dialect') && dict.has(i.original.toLowerCase().trim())) continue;
        const sig = signature(b.text, i);
        if (c.dismissed.has(sig)) continue;
        out.push({
          ...i,
          id: `${b.key}:${i.offset}:${i.length}:${hashText(i.replacements.join('|'))}`,
          from: b.from + i.offset,
          to: b.from + i.offset + i.length,
          blockKey: b.key,
          sig,
        });
      }
    }
    out.sort((a, b) => a.from - b.from || a.to - b.to);
    setSuggestions(view, out);
  }

  /** Index of the paragraph (textblock) that contains a position, in document order. */
  blockIndexAt(pos: number): number {
    return extractBlocks(this.editor.state.doc).findIndex((x) => pos >= x.from && pos <= x.from + x.text.length);
  }

  /**
   * After suggestions were accepted, those paragraphs have new text (new hashes). Store their remaining
   * AI suggestions under the new hashes so the model is not asked again about edits we made ourselves.
   * Block indexes are stable here because accepting only edits inline text, never the block structure.
   */
  adoptBlocks(indexes: Iterable<number>): void {
    const fp = this.fp(this.ctx());
    const blocks = extractBlocks(this.editor.state.doc);
    const items = getSuggestions(this.editor.state).items;
    for (const idx of new Set(indexes)) {
      const b = blocks[idx];
      if (!b) continue;
      const end = b.from + b.text.length;
      this.store(`${fp}:${b.key}`, items.filter((s) => s.source === 'ai' && s.from >= b.from && s.to <= end).map((s) => toRelative(s, b)));
    }
    this.publish();
  }

  stats(): { cached: number; pending: number } {
    return { cached: this.cache.size, pending: this.pending.size };
  }
}

function toRelative(s: PlacedSuggestion, b: Block): Issue {
  return {
    offset: s.from - b.from,
    length: s.to - s.from,
    original: s.original,
    replacements: s.replacements,
    category: s.category,
    type: s.type,
    title: s.title,
    explanation: s.explanation,
    source: s.source,
  };
}
