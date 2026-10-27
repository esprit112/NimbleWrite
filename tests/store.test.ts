// Documents that arrive as plain text, like the desktop companion's "Open in NimbleWrite", must open with their text:
// the editor shows a document's HTML, so the store makes HTML from the text whenever none comes with it.
// Every test runs against an empty temporary data folder (never the real one) and never reads .env.
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

let dataDir = '';

beforeEach(async () => {
  dataDir = await mkdtemp(path.join(os.tmpdir(), 'nimblewrite-store-'));
});

afterEach(async () => {
  vi.unstubAllEnvs();
  vi.resetModules();
  await rm(dataDir, { recursive: true, force: true });
});

async function freshStore() {
  vi.stubEnv('NIMBLEWRITE_PACKAGED', '1');
  vi.stubEnv('NIMBLEWRITE_DATA_DIR', dataDir);
  vi.resetModules();
  return import('../server/store');
}

describe('plainTextToHtml', () => {
  it('starts a paragraph at every line break, whichever kind the app uses', async () => {
    const { plainTextToHtml } = await freshStore();
    const want = '<p>One.</p><p>Two.</p><p>Three.</p>';
    expect(plainTextToHtml('One.\rTwo.\rThree.')).toBe(want); // Windows 11 Notepad and Word
    expect(plainTextToHtml('One.\r\nTwo.\r\nThree.')).toBe(want);
    expect(plainTextToHtml('One.\nTwo.\nThree.')).toBe(want);
    expect(plainTextToHtml('One.\u0007Two.\fThree.')).toBe(want); // Word: end of a table cell, page break
    expect(plainTextToHtml('One.\u2029Two.\u2029Three.')).toBe(want);
  });

  it('keeps manual line breaks inside a paragraph and drops blank lines', async () => {
    const { plainTextToHtml } = await freshStore();
    expect(plainTextToHtml('Hi Sam,\r\r\rKind regards,\vMartin\r  \r')).toBe('<p>Hi Sam,</p><p>Kind regards,<br>Martin</p>');
    expect(plainTextToHtml('12 High Street\u2028Leeds')).toBe('<p>12 High Street<br>Leeds</p>');
    expect(plainTextToHtml('')).toBe('');
    expect(plainTextToHtml(' \r\n\t\r ')).toBe('');
  });

  it('escapes the text, so it can never turn into markup', async () => {
    const { plainTextToHtml } = await freshStore();
    expect(plainTextToHtml('if a < b && c > d\r<script>alert(1)</script>')).toBe(
      '<p>if a &lt; b &amp;&amp; c &gt; d</p><p>&lt;script&gt;alert(1)&lt;/script&gt;</p>',
    );
  });
});

describe('documents created or updated from plain text', () => {
  it('get HTML made from their text when only text is sent (the companion\'s "Open in NimbleWrite")', async () => {
    const store = await freshStore();
    await store.loadStore();
    const doc = await store.createDoc({ title: 'From Notepad, 27 Oct 19:00', text: 'Their going to the libary.\rSee you there.' });
    expect(doc.html).toBe('<p>Their going to the libary.</p><p>See you there.</p>');
    expect(doc.text).toBe('Their going to the libary.\rSee you there.');
    expect(doc.wordCount).toBe(8);
    expect(store.getDoc(doc.id)?.html).toBe(doc.html);
  });

  it('keep the HTML the editor sends, and an empty document stays empty', async () => {
    const store = await freshStore();
    await store.loadStore();
    const html = '<h1>Title</h1><p>Body with <strong>bold</strong>.</p>';
    expect((await store.createDoc({ html, text: 'Title\n\nBody with bold.' })).html).toBe(html);
    expect((await store.createDoc({})).html).toBe('');
    expect((await store.createDoc({ html: '', text: 'Plain.' })).html).toBe('<p>Plain.</p>'); // empty html counts as none
  });

  it('get new HTML when text alone is updated, but HTML that is sent always wins', async () => {
    const store = await freshStore();
    await store.loadStore();
    const doc = await store.createDoc({ html: '<p>Old text.</p>', text: 'Old text.' });
    expect((await store.updateDoc(doc.id, { text: 'New text.\rSecond line.' }))?.html).toBe('<p>New text.</p><p>Second line.</p>');
    expect((await store.updateDoc(doc.id, { html: '', text: 'Plain again.' }))?.html).toBe('<p>Plain again.</p>'); // as in createDoc
    expect((await store.updateDoc(doc.id, { html: '<p>From the <em>editor</em>.</p>', text: 'From the editor.' }))?.html).toBe('<p>From the <em>editor</em>.</p>');
    expect((await store.updateDoc(doc.id, { score: 90 }))?.html).toBe('<p>From the <em>editor</em>.</p>');
  });

  it('that were already saved without HTML open with their text', async () => {
    const docsDir = path.join(dataDir, 'docs');
    await mkdir(docsDir, { recursive: true });
    const base = { goals: { audience: 'general', formality: 'neutral', domain: 'general', intent: [] }, dismissed: [], score: null, createdAt: 1, updatedAt: 1, deletedAt: null };
    const blank = { ...base, id: '0f8fad5b-d9cb-469f-a165-70867728950e', title: 'From Notepad, 26 Oct 21:15', html: '', text: 'Saved blank.\rStill here.', wordCount: 4 };
    const fine = { ...base, id: '7c9e6679-7425-40de-944b-e07fc1f90ae7', title: 'Written in the editor', html: '<p>Untouched <em>HTML</em>.</p>', text: 'Untouched HTML.', wordCount: 2 };
    await writeFile(path.join(docsDir, `${blank.id}.json`), JSON.stringify(blank));
    await writeFile(path.join(docsDir, `${fine.id}.json`), JSON.stringify(fine));

    const store = await freshStore();
    await store.loadStore();
    expect(store.getDoc(blank.id)?.html).toBe('<p>Saved blank.</p><p>Still here.</p>');
    expect(store.getDoc(fine.id)?.html).toBe(fine.html);
    expect(store.listDocs(false)).toHaveLength(2); // no welcome document added, nothing lost
  });
});
