// The installed Windows app: the desktop companion starts the bundled server with its own folders and the user's own
// NVIDIA key, and tests a key before saving it. Key assertions compare booleans so a failure never prints a key.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.resetModules();
});

async function loadConfig(env: Record<string, string | undefined>) {
  for (const [k, v] of Object.entries(env)) vi.stubEnv(k, v);
  vi.resetModules();
  return import('../server/config');
}

describe('server config for the installed app', () => {
  it('uses the folders the desktop app passes and never reads .env', async () => {
    const data = path.resolve('C:/Users/Someone/AppData/Roaming/NimbleWrite/data');
    const web = path.resolve('C:/Users/Someone/AppData/Local/Programs/NimbleWrite/web');
    const { config } = await loadConfig({ NIMBLEWRITE_PACKAGED: '1', NIMBLEWRITE_DATA_DIR: data, NIMBLEWRITE_WEB_DIR: web, NVIDIA_API_KEY: undefined });
    expect(config.packaged).toBe(true);
    expect(config.dataDir).toBe(data);
    expect(config.distDir).toBe(web);
    // This repo's .env has a key; an installed copy must only ever use the key its desktop app passes in.
    expect(config.nim.apiKey === '').toBe(true);
  });

  it('takes the key from the environment the desktop app sets', async () => {
    const { config } = await loadConfig({ NIMBLEWRITE_PACKAGED: '1', NVIDIA_API_KEY: '  nvapi-FAKE-from-desktop-000000  ' });
    expect(config.nim.apiKey === 'nvapi-FAKE-from-desktop-000000').toBe(true);
  });

  it('keeps the project folders in development', async () => {
    const { config, ROOT } = await loadConfig({
      NIMBLEWRITE_PACKAGED: undefined,
      NIMBLEWRITE_DATA_DIR: undefined,
      NIMBLEWRITE_WEB_DIR: '   ',
      NVIDIA_API_KEY: 'nvapi-FAKE-dev-placeholder-0000',
    });
    expect(config.packaged).toBe(false);
    expect(config.dataDir).toBe(path.join(ROOT, 'data'));
    expect(config.distDir).toBe(path.join(ROOT, 'dist'));
  });

  it('reports the same version as package.json (the installer is named after it)', async () => {
    const { config, ROOT } = await loadConfig({});
    const pkg = JSON.parse(readFileSync(path.join(ROOT, 'package.json'), 'utf8')) as { version: string };
    expect(config.version).toBe(pkg.version);
  });
});

describe('testing an NVIDIA key before it is saved', () => {
  const KEY = 'nvapi-FAKE-test-key-0000000000000000';

  async function testWith(status: number | 'offline') {
    const calls: { url: string; init: RequestInit }[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init: RequestInit) => {
        calls.push({ url, init });
        if (status === 'offline') throw new TypeError('fetch failed');
        return new Response('{}', { status });
      }),
    );
    const { testApiKey } = await import('../server/nim');
    return { result: await testApiKey(KEY, 'nvidia/check-model'), calls };
  }

  it('accepts a key NVIDIA accepts, using it for that one tiny request', async () => {
    const { result, calls } = await testWith(200);
    expect(result).toEqual({ ok: true });
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toMatch(/\/chat\/completions$/);
    const headers = calls[0].init.headers as Record<string, string>;
    expect(headers.Authorization === `Bearer ${KEY}`).toBe(true);
    expect(JSON.parse(String(calls[0].init.body))).toMatchObject({ model: 'nvidia/check-model', max_tokens: 1, stream: false });
  });

  it.each([401, 403])('reports a rejected key (HTTP %i)', async (status) => {
    const { result } = await testWith(status);
    expect(result).toMatchObject({ ok: false, reason: 'rejected' });
  });

  it('tells "accepted with a caveat" apart from "could not check"', async () => {
    expect((await testWith(404)).result).toMatchObject({ ok: true, reason: 'model' });
    expect((await testWith(429)).result).toMatchObject({ ok: true, reason: 'rate' });
    expect((await testWith(400)).result).toEqual({ ok: true });
    expect((await testWith(500)).result).toMatchObject({ ok: false, reason: 'error' });
    expect((await testWith('offline')).result).toMatchObject({ ok: false, reason: 'network' });
  });

  it('never repeats the key back', async () => {
    for (const status of [200, 401, 404, 429, 500, 'offline'] as const) {
      const { result } = await testWith(status);
      expect(JSON.stringify(result).includes('FAKE-test-key')).toBe(false);
    }
  });
});
