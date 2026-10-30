// Formatting toolbar (bottom of the editor) and the link dialog.
import { useEffect, useState } from 'react';
import type { Editor } from '@tiptap/core';
import { useEditorState } from '@tiptap/react';
import { Bold, Heading1, Heading2, Italic, Link, List, ListOrdered, Quote, Redo2, RemoveFormatting, Strikethrough, Underline, Undo2, Unlink } from 'lucide-react';
import { Button, IconButton, Modal } from '../components/ui';
import { inputClass } from './panels/common';

export function FormatToolbar({ editor }: { editor: Editor }) {
  const s = useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      bold: e.isActive('bold'),
      italic: e.isActive('italic'),
      underline: e.isActive('underline'),
      strike: e.isActive('strike'),
      h1: e.isActive('heading', { level: 1 }),
      h2: e.isActive('heading', { level: 2 }),
      bullet: e.isActive('bulletList'),
      ordered: e.isActive('orderedList'),
      quote: e.isActive('blockquote'),
      link: e.isActive('link'),
      canUndo: e.can().undo(),
      canRedo: e.can().redo(),
    }),
  });
  const [linkOpen, setLinkOpen] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k' && editor.isFocused) {
        e.preventDefault();
        setLinkOpen(true);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [editor]);

  const c = () => editor.chain().focus();
  return (
    <div className="flex items-center gap-0.5">
      <IconButton label="Bold (Ctrl+B)" active={s.bold} onClick={() => c().toggleBold().run()}><Bold className="size-4" /></IconButton>
      <IconButton label="Italic (Ctrl+I)" active={s.italic} onClick={() => c().toggleItalic().run()}><Italic className="size-4" /></IconButton>
      <IconButton label="Underline (Ctrl+U)" active={s.underline} onClick={() => c().toggleUnderline().run()}><Underline className="size-4" /></IconButton>
      <IconButton label="Strikethrough" active={s.strike} onClick={() => c().toggleStrike().run()} className="hidden sm:inline-flex"><Strikethrough className="size-4" /></IconButton>
      <span className="mx-1 h-5 w-px bg-slate-200 dark:bg-slate-700" />
      <IconButton label="Heading 1" active={s.h1} onClick={() => c().toggleHeading({ level: 1 }).run()}><Heading1 className="size-4" /></IconButton>
      <IconButton label="Heading 2" active={s.h2} onClick={() => c().toggleHeading({ level: 2 }).run()}><Heading2 className="size-4" /></IconButton>
      <IconButton label={s.link ? 'Edit link (Ctrl+K)' : 'Insert link (Ctrl+K)'} active={s.link} onClick={() => setLinkOpen(true)}><Link className="size-4" /></IconButton>
      <IconButton label="Numbered list" active={s.ordered} onClick={() => c().toggleOrderedList().run()}><ListOrdered className="size-4" /></IconButton>
      <IconButton label="Bulleted list" active={s.bullet} onClick={() => c().toggleBulletList().run()}><List className="size-4" /></IconButton>
      <IconButton label="Quote" active={s.quote} onClick={() => c().toggleBlockquote().run()} className="hidden sm:inline-flex"><Quote className="size-4" /></IconButton>
      <IconButton label="Clear formatting" onClick={() => c().unsetAllMarks().clearNodes().run()}><RemoveFormatting className="size-4" /></IconButton>
      <span className="mx-1 h-5 w-px bg-slate-200 dark:bg-slate-700" />
      <IconButton label="Undo (Ctrl+Z)" disabled={!s.canUndo} onClick={() => c().undo().run()}><Undo2 className="size-4" /></IconButton>
      <IconButton label="Redo (Ctrl+Y)" disabled={!s.canRedo} onClick={() => c().redo().run()}><Redo2 className="size-4" /></IconButton>
      <LinkDialog editor={editor} open={linkOpen} onClose={() => setLinkOpen(false)} />
    </div>
  );
}

function LinkDialog({ editor, open, onClose }: { editor: Editor; open: boolean; onClose: () => void }) {
  const [url, setUrl] = useState('');
  useEffect(() => {
    if (open) setUrl((editor.getAttributes('link').href as string | undefined) ?? '');
  }, [open, editor]);
  const apply = () => {
    const href = url.trim();
    if (!href) {
      editor.chain().focus().extendMarkRange('link').unsetLink().run();
    } else {
      const safe = /^(https?:|mailto:|tel:)/i.test(href) ? href : `https://${href}`;
      if (editor.state.selection.empty && !editor.isActive('link')) {
        editor.chain().focus().insertContent({ type: 'text', text: href, marks: [{ type: 'link', attrs: { href: safe } }] }).run();
      } else {
        editor.chain().focus().extendMarkRange('link').setLink({ href: safe }).run();
      }
    }
    onClose();
  };
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Link"
      width="max-w-md"
      footer={
        <>
          {editor.isActive('link') && (
            <Button variant="ghost" onClick={() => { editor.chain().focus().extendMarkRange('link').unsetLink().run(); onClose(); }}>
              <Unlink className="size-4" /> Remove link
            </Button>
          )}
          <Button variant="primary" onClick={apply}>Apply</Button>
        </>
      }
    >
      <input autoFocus value={url} onChange={(e) => setUrl(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && apply()} placeholder="https://example.com" className={inputClass} />
    </Modal>
  );
}
