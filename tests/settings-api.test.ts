// Endpoints the desktop companion uses to change settings and read text statistics without porting web logic:
// PATCH /api/settings (merge, never replace), POST /api/settings/tune (apply Tune changes), POST /api/text/stats.
// Every test runs against an empty temporary data folder (never the real one) and never reads .env.
import type { AddressInfo } from 'node:net';
import type { Server } from 'node:http';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Settings } from '../shared/types';

let dataDir = '';
let server: Server | null = null;
let base = '';

beforeEach(async () => {
  dataDir = await mkdtemp(path.join(os.tmpdir(), 'nimblewrite-settings-'));
  vi.stubEnv('NIMBLEWRITE_PACKAGED', '1');
  vi.stubEnv('NIMBLEWRITE_DATA_DIR', dataDir);
  vi.stubEnv('NVIDIA_API_KEY', '');
  vi.resetModules();
  const express = (await import('express')).default;
  const { dataRouter } = await import('../server/routes/data');
  const { errorHandler } = await import('../server/errors');
  const { loadStore } = await import('../server/store');
  await loadStore();
  const app = express();
  app.use(express.json());
  app.use('/api', dataRouter);
  app.use(errorHandler);
  await new Promise<void>((resolve) => {
    server = app.listen(0, '127.0.0.1', () => resolve());
  });
  base = `http://127.0.0.1:${(server!.address() as AddressInfo).port}/api`;
});

afterEach(async () => {
  await new Promise<void>((resolve) => (server ? server.close(() => resolve()) : resolve()));
  server = null;
  vi.unstubAllEnvs();
  vi.resetModules();
  await rm(dataDir, { recursive: true, force: true });
});

const call = async (method: string, p: string, body?: unknown) => {
  const r = await fetch(`${base}${p}`, { method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, body: (await r.json().catch(() => null)) as Record<string, unknown> | null };
};

describe('PATCH /api/settings', () => {
  it('changes only the fields sent and keeps everything else', async () => {
    const before = (await call('GET', '/settings')).body as unknown as Settings;
    await call('POST', '/settings/dictionary', { word: 'Nimble' });
    const r = await call('PATCH', '/settings', { dialect: 'en-US', categories: { engagement: false } });
    expect(r.status).toBe(200);
    const after = r.body as unknown as Settings;
    expect(after.dialect).toBe('en-US');
    expect(after.categories).toEqual({ ...before.categories, engagement: false });
    expect(after.dictionary).toEqual(['Nimble']);
    expect(after.snippets).toEqual(before.snippets);
    expect(after.models).toEqual(before.models);
    expect((await call('GET', '/settings')).body).toEqual(after);
  });

  it('accepts local checks, default goals and theme', async () => {
    const goals = { audience: 'expert', formality: 'formal', domain: 'business', intent: ['convince'] };
    const r = await call('PATCH', '/settings', { localRules: false, defaultGoals: goals, theme: 'dark' });
    expect(r.status).toBe(200);
    expect(r.body).toMatchObject({ localRules: false, defaultGoals: goals, theme: 'dark' });
  });

  it('refuses to switch every suggestion category off', async () => {
    const r = await call('PATCH', '/settings', { categories: { correctness: false, clarity: false, engagement: false, delivery: false } });
    expect(r.status).toBe(400);
    expect(String(r.body?.error)).toMatch(/at least one/i);
  });

  it('rejects fields it does not allow, so the desktop can never overwrite the dictionary or models', async () => {
    expect((await call('PATCH', '/settings', { dictionary: [] })).status).toBe(400);
    expect((await call('PATCH', '/settings', { models: { check: 'x/y', write: 'x/y', translate: 'x/y' } })).status).toBe(400);
    expect((await call('PATCH', '/settings', { dialect: 'en-XX' })).status).toBe(400);
  });
});

describe('POST /api/settings/tune', () => {
  it('applies Tune changes on the server and reports what took effect', async () => {
    const r = await call('POST', '/settings/tune', {
      changes: [
        { kind: 'mute', type: 'passive_voice', title: 'Passive voice' },
        { kind: 'dictionary', word: 'Nemotron' },
        { kind: 'instruction', text: 'Allow sentence fragments in dialogue.' },
        { kind: 'style_rule', find: 'e-mail', replace: 'email' },
      ],
    });
    expect(r.status).toBe(200);
    expect(r.body).toMatchObject({ applied: 4, skipped: 0 });
    const s = r.body?.settings as unknown as Settings;
    expect(s.mutedTypes).toContain('passive_voice');
    expect(s.dictionary).toContain('Nemotron');
    expect(s.customInstructions).toContain('- Allow sentence fragments in dialogue.');
    expect(s.styleRules.map((x) => [x.find, x.replace])).toEqual([['e-mail', 'email']]);
    expect(s.styleRules[0].id).toMatch(/^[\w-]{6,64}$/);
  });

  it('skips changes that are already in place', async () => {
    await call('POST', '/settings/tune', { changes: [{ kind: 'dictionary', word: 'Nemotron' }] });
    const r = await call('POST', '/settings/tune', { changes: [{ kind: 'dictionary', word: 'nemotron' }] });
    expect(r.body).toMatchObject({ applied: 0, skipped: 1 });
  });

  it('rejects unknown change kinds and unknown issue types', async () => {
    expect((await call('POST', '/settings/tune', { changes: [{ kind: 'delete_everything' }] })).status).toBe(400);
    expect((await call('POST', '/settings/tune', { changes: [{ kind: 'mute', type: 'not_a_type', title: 'x' }] })).status).toBe(400);
  });
});

describe('POST /api/text/stats', () => {
  it('returns the same statistics as the web editor computes, plus the readability label and score', async () => {
    const { computeStats, overallScore, readabilityLabel } = await import('../shared/text');
    const text = 'Writing clearly is a skill. Most people can improve it with practice and honest feedback from readers.';
    const counts = { correctness: 1, clarity: 0, engagement: 2, delivery: 0 };
    const r = await call('POST', '/text/stats', { text, counts });
    expect(r.status).toBe(200);
    const stats = computeStats(text);
    expect(r.body).toEqual({ stats, readability: readabilityLabel(stats.readability), score: overallScore(stats.words, counts, stats.readability) });
  });

  it('gives no score when the client sends no issue counts', async () => {
    const r = await call('POST', '/text/stats', { text: 'Short text with a handful of words.' });
    expect(r.body?.score).toBeNull();
  });

  it('rejects an empty text', async () => {
    expect((await call('POST', '/text/stats', { text: '' })).status).toBe(400);
  });
});
