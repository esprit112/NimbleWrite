// Map the ProseMirror document to plain-text paragraphs with exact positions.
// In a textblock every character and every inline leaf (e.g. hard break) takes exactly one
// position, so a paragraph's character i lives at document position `from + i`.
import type { Node as PMNode } from '@tiptap/pm/model';
import { hashText } from '../../shared/text';

export interface Block {
  from: number;
  text: string;
  key: string;
}

export function extractBlocks(doc: PMNode): Block[] {
  const blocks: Block[] = [];
  doc.descendants((node, pos) => {
    if (!node.isTextblock) return true;
    if (node.type.name === 'codeBlock') return false;
    let text = '';
    node.forEach((child) => {
      if (child.isText) text += child.text ?? '';
      else text += child.type.name === 'hardBreak' ? '\n' : '\ufffc';
    });
    blocks.push({ from: pos + 1, text, key: hashText(text) });
    return false;
  });
  return blocks;
}

export function blockAt(blocks: Block[], pos: number): Block | undefined {
  return blocks.find((b) => pos >= b.from && pos <= b.from + b.text.length);
}

export function docPlainText(doc: PMNode): string {
  return doc.textBetween(0, doc.content.size, '\n\n', '\n');
}
