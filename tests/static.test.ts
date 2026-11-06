import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import type { Server } from 'node:http';
import express from 'express';
import { serveWebApp } from '../server/static';

// The built web app: hashed files under /assets never change, so browsers keep them; index.html always revalidates.
describe('serving the built web app', () => {
  let dir: string;
  let server: Server;
  let base: string;

  beforeAll(async () => {
    dir = mkdtempSync(path.join(tmpdir(), 'nw-dist-'));
    mkdirSync(path.join(dir, 'assets'));
    writeFileSync(path.join(dir, 'index.html'), '<!doctype html><title>NimbleWrite</title>');
    writeFileSync(path.join(dir, 'assets', 'index-Ab12Cd34.js'), 'console.log(1)');
    writeFileSync(path.join(dir, 'favicon.svg'), '<svg/>');
    const app = express();
    expect(serveWebApp(app, dir)).toBe(true);
    await new Promise<void>((resolve) => {
      server = app.listen(0, '127.0.0.1', () => resolve());
    });
    const addr = server.address();
    base = `http://127.0.0.1:${typeof addr === 'object' && addr ? addr.port : 0}`;
  });

  afterAll(() => {
    server?.close();
    rmSync(dir, { recursive: true, force: true });
  });

  it('lets browsers keep hashed assets for a year', async () => {
    const r = await fetch(`${base}/assets/index-Ab12Cd34.js`);
    expect(r.status).toBe(200);
    expect(r.headers.get('cache-control')).toBe('public, max-age=31536000, immutable');
  });

  it('always revalidates index.html, whether asked for by name or by route', async () => {
    for (const p of ['/', '/index.html', '/doc/abc']) {
      const r = await fetch(base + p);
      expect(r.status).toBe(200);
      expect(await r.text()).toContain('<title>NimbleWrite</title>');
      expect(r.headers.get('cache-control')).toBe('no-cache');
    }
  });

  it('revalidates other files at the root (they are not hashed)', async () => {
    const r = await fetch(`${base}/favicon.svg`);
    expect(r.headers.get('cache-control')).toBe('no-cache');
  });

  it('answers a missing asset with 404 instead of the app page', async () => {
    const r = await fetch(`${base}/assets/gone-123.js`);
    expect(r.status).toBe(404);
  });

  it('does nothing when the web app has not been built', () => {
    const empty = mkdtempSync(path.join(tmpdir(), 'nw-nodist-'));
    try {
      expect(serveWebApp(express(), empty)).toBe(false);
    } finally {
      rmSync(empty, { recursive: true, force: true });
    }
  });
});
