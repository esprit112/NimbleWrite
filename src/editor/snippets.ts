// Snippets: type \trigger then Space/Tab/Enter to expand it (like Grammarly Business snippets).
import { Extension } from '@tiptap/core';
import type { Snippet } from '../../shared/types';

export const Snippets = Extension.create<{ getSnippets: () => Snippet[] }>({
  name: 'nwSnippets',
  addOptions() {
    return { getSnippets: () => [] };
  },
  addKeyboardShortcuts() {
    const expand = () => {
      const { state, view } = this.editor;
      const { $from, empty } = state.selection;
      if (!empty || !$from.parent.isTextblock) return false;
      const before = $from.parent.textBetween(Math.max(0, $from.parentOffset - 40), $from.parentOffset, undefined, '\ufffc');
      const m = /(?:^|\s)\\([\w-]{1,30})$/.exec(before);
      if (!m) return false;
      const snippet = this.options.getSnippets().find((s) => s.trigger.toLowerCase() === m[1].toLowerCase());
      if (!snippet) return false;
      const start = $from.pos - m[1].length - 1;
      const tr = state.tr.delete(start, $from.pos);
      let pos = start;
      snippet.text.split('\n').forEach((line, i) => {
        if (i > 0) {
          tr.insert(pos, state.schema.nodes.hardBreak.create());
          pos += 1;
        }
        if (line) {
          tr.insertText(line, pos);
          pos += line.length;
        }
      });
      view.dispatch(tr.scrollIntoView());
      return true;
    };
    return { Space: expand, Tab: expand, Enter: expand };
  },
});
