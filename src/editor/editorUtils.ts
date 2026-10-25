// Helpers for reading and changing the document from panels (selection targets, replace, insert, export).
import type { Editor } from '@tiptap/core';
import { marked } from 'marked';
import DOMPurify from 'dompurify';
// docx and turndown are only needed for downloads, so they are loaded on demand (keeps first load fast).
type Docx = typeof import('docx');
type ParagraphChild = import('docx').ParagraphChild;
type DocxParagraph = import('docx').Paragraph;
import { docPlainText } from './blocks';

export type TargetKind = 'selection' | 'paragraph' | 'document';

export interface Target {
  kind: TargetKind;
  from: number;
  to: number;
  text: string;
}

export function getTarget(editor: Editor, prefer: TargetKind | 'auto' = 'auto'): Target {
  const { state } = editor;
  const { from, to, empty, $from } = state.selection;
  const docTarget = (): Target => ({ kind: 'document', from: 0, to: state.doc.content.size, text: docPlainText(state.doc) });
  if ((prefer === 'auto' || prefer === 'selection') && !empty) {
    return { kind: 'selection', from, to, text: state.doc.textBetween(from, to, '\n\n', '\n') };
  }
  if (prefer === 'document') return docTarget();
  if ($from.parent.isTextblock && $from.parent.textContent.trim()) {
    const start = $from.start();
    const end = $from.end();
    return { kind: 'paragraph', from: start, to: end, text: state.doc.textBetween(start, end, '\n\n', '\n') };
  }
  return docTarget();
}

export function markdownToHtml(md: string): string {
  return DOMPurify.sanitize(marked.parse(md, { async: false }) as string);
}

const looksStructured = (t: string) => /\n\s*\n|^\s*(?:[-*+]\s|\d+\.\s|#{1,6}\s)/m.test(t.trim());

/** Replace a range with AI output. Plain one-paragraph text keeps the surrounding formatting. */
export function replaceRange(editor: Editor, target: Target, output: string): boolean {
  const { state, view } = editor;
  const size = state.doc.content.size;
  if (target.to > size) return false;
  const current = state.doc.textBetween(target.from, target.to, '\n\n', '\n');
  if (current !== target.text) return false;
  const text = output.trim();
  if (target.kind === 'document') {
    editor.chain().focus().setContent(markdownToHtml(text)).run();
    return true;
  }
  if (!looksStructured(text)) {
    view.dispatch(state.tr.insertText(text.replace(/\n/g, ' '), target.from, target.to).scrollIntoView());
    view.focus();
    return true;
  }
  editor.chain().focus().insertContentAt({ from: target.from, to: target.to }, markdownToHtml(text)).run();
  return true;
}

/** Insert AI output as new paragraphs after the top-level block that contains `pos`. */
export function insertAfter(editor: Editor, pos: number, output: string): void {
  const { state } = editor;
  const $pos = state.doc.resolve(Math.min(pos, state.doc.content.size));
  const after = $pos.depth >= 1 ? $pos.after(1) : state.doc.content.size;
  editor.chain().focus().insertContentAt(after, markdownToHtml(output.trim())).run();
}

export function selectRange(editor: Editor, from: number, to: number): void {
  const size = editor.state.doc.content.size;
  editor.chain().focus().setTextSelection({ from: Math.min(from, size), to: Math.min(to, size) }).scrollIntoView().run();
}

/** Find `needle` in the document text and return its position range (first match). */
export function findText(editor: Editor, needle: string): { from: number; to: number } | null {
  if (!needle.trim()) return null;
  const { doc } = editor.state;
  let found: { from: number; to: number } | null = null;
  const probe = needle.trim().slice(0, 80);
  doc.descendants((node, pos) => {
    if (found || !node.isTextblock) return !found;
    let text = '';
    node.forEach((child) => {
      text += child.isText ? child.text ?? '' : '\n';
    });
    const i = text.indexOf(probe);
    if (i >= 0) found = { from: pos + 1 + i, to: pos + 1 + i + probe.length };
    return false;
  });
  return found;
}

// ---------- export ----------
function download(name: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
}

const safeName = (title: string) => (title.trim() || 'document').replace(/[\\/:*?"<>|]+/g, '').slice(0, 80);

export function exportText(editor: Editor, title: string): void {
  download(`${safeName(title)}.txt`, new Blob([editor.getText({ blockSeparator: '\n\n' })], { type: 'text/plain;charset=utf-8' }));
}

export async function exportMarkdown(editor: Editor, title: string): Promise<void> {
  const { default: TurndownService } = await import('turndown');
  const td = new TurndownService({ headingStyle: 'atx', bulletListMarker: '-', codeBlockStyle: 'fenced' });
  download(`${safeName(title)}.md`, new Blob([td.turndown(editor.getHTML())], { type: 'text/markdown;charset=utf-8' }));
}

export function exportHtml(editor: Editor, title: string): void {
  const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>${title.replace(/</g, '&lt;')}</title>
<style>body{font-family:Georgia,serif;max-width:720px;margin:3rem auto;line-height:1.7;color:#1e293b;padding:0 1rem}h1,h2,h3{font-family:system-ui,sans-serif}</style></head><body>${editor.getHTML()}</body></html>`;
  download(`${safeName(title)}.html`, new Blob([html], { type: 'text/html;charset=utf-8' }));
}

interface JNode {
  type: string;
  text?: string;
  attrs?: Record<string, unknown>;
  marks?: { type: string; attrs?: Record<string, unknown> }[];
  content?: JNode[];
}

function runs(d: Docx, nodes: JNode[] = []): ParagraphChild[] {
  const out: ParagraphChild[] = [];
  for (const n of nodes) {
    if (n.type === 'hardBreak') {
      out.push(new d.TextRun({ text: '', break: 1 }));
      continue;
    }
    if (n.type !== 'text' || !n.text) continue;
    const m = new Set((n.marks ?? []).map((x) => x.type));
    const link = n.marks?.find((x) => x.type === 'link')?.attrs?.href as string | undefined;
    const run = new d.TextRun({
      text: n.text,
      bold: m.has('bold'),
      italics: m.has('italic'),
      underline: m.has('underline') || link ? {} : undefined,
      strike: m.has('strike'),
      font: m.has('code') ? 'Consolas' : undefined,
      color: link ? '0B6B3A' : undefined,
    });
    out.push(link ? new d.ExternalHyperlink({ link, children: [run] }) : run);
  }
  return out;
}

function blocksToDocx(d: Docx, nodes: JNode[] = [], list?: { ordered: boolean; level: number }): DocxParagraph[] {
  const out: DocxParagraph[] = [];
  const headings = [d.HeadingLevel.HEADING_1, d.HeadingLevel.HEADING_2, d.HeadingLevel.HEADING_3, d.HeadingLevel.HEADING_4];
  for (const n of nodes) {
    switch (n.type) {
      case 'paragraph':
        out.push(new d.Paragraph({
          children: runs(d, n.content),
          ...(list ? (list.ordered ? { numbering: { reference: 'nw-ordered', level: list.level } } : { bullet: { level: list.level } }) : {}),
        }));
        break;
      case 'heading':
        out.push(new d.Paragraph({ heading: headings[Math.min(4, Number(n.attrs?.level ?? 1)) - 1], children: runs(d, n.content) }));
        break;
      case 'bulletList':
      case 'orderedList':
        for (const item of n.content ?? []) out.push(...blocksToDocx(d, item.content, { ordered: n.type === 'orderedList', level: list ? Math.min(list.level + 1, 8) : 0 }));
        break;
      case 'blockquote':
        out.push(...blocksToDocx(d, n.content));
        break;
      case 'codeBlock':
        out.push(new d.Paragraph({ children: [new d.TextRun({ text: (n.content ?? []).map((c) => c.text ?? '').join(''), font: 'Consolas' })] }));
        break;
      case 'horizontalRule':
        out.push(new d.Paragraph({ children: [], border: { bottom: { style: 'single', size: 6, color: 'CBD5E1', space: 1 } } }));
        break;
      default:
        if (n.content) out.push(...blocksToDocx(d, n.content, list));
    }
  }
  return out;
}

export async function exportDocx(editor: Editor, title: string): Promise<void> {
  const d = await import('docx');
  const json = editor.getJSON() as JNode;
  const doc = new d.Document({
    creator: 'NimbleWrite',
    title,
    numbering: {
      config: [{
        reference: 'nw-ordered',
        levels: Array.from({ length: 9 }, (_, level) => ({ level, format: d.LevelFormat.DECIMAL, text: `%${level + 1}.`, alignment: d.AlignmentType.START, style: { paragraph: { indent: { left: 720 * (level + 1), hanging: 360 } } } })),
      }],
    },
    sections: [{ children: blocksToDocx(d, json.content) }],
  });
  download(`${safeName(title)}.docx`, await d.Packer.toBlob(doc));
}

export async function copyRich(editor: Editor): Promise<void> {
  const html = editor.getHTML();
  const text = editor.getText({ blockSeparator: '\n\n' });
  try {
    await navigator.clipboard.write([new ClipboardItem({ 'text/html': new Blob([html], { type: 'text/html' }), 'text/plain': new Blob([text], { type: 'text/plain' }) })]);
  } catch {
    await navigator.clipboard.writeText(text);
  }
}
