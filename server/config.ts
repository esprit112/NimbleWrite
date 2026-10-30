// Server configuration. Secrets never leave the server: in development they come from .env; the installed Windows
// app starts this server itself and passes the user's own NVIDIA key in its environment.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { config as loadEnv } from 'dotenv';

export const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** True when the installed Windows app started this server (NIMBLEWRITE_PACKAGED=1). */
const packaged = process.env.NIMBLEWRITE_PACKAGED === '1';
if (!packaged) loadEnv({ path: path.join(ROOT, '.env'), quiet: true });

const num = (v: string | undefined, d: number) => (v && Number.isFinite(Number(v)) ? Number(v) : d);
const dir = (v: string | undefined, fallback: string) => (v?.trim() ? path.resolve(v.trim()) : fallback);

export const config = {
  port: num(process.env.PORT, 4317),
  nim: {
    baseUrl: (process.env.NIM_BASE_URL || 'https://integrate.api.nvidia.com/v1').replace(/\/+$/, ''),
    apiKey: (process.env.NVIDIA_API_KEY || '').trim(),
    checkModel: process.env.NIM_CHECK_MODEL || 'nvidia/nemotron-3.5-lightning-30b-a3b',
    writeModel: process.env.NIM_WRITE_MODEL || 'nvidia/nemotron-3-ultra-550b-a55b',
    translateModel: process.env.NIM_TRANSLATE_MODEL || 'nvidia/nemotron-3-ultra-550b-a55b',
    maxConcurrency: num(process.env.NIM_MAX_CONCURRENCY, 4),
  },
  braveKey: (process.env.BRAVE_API_KEY || '').trim(),
  /** Documents, settings and stats. The installed app keeps them in %APPDATA%\NimbleWrite\data. */
  dataDir: dir(process.env.NIMBLEWRITE_DATA_DIR, path.join(ROOT, 'data')),
  /** The built web editor, served on the API port. */
  distDir: dir(process.env.NIMBLEWRITE_WEB_DIR, path.join(ROOT, 'dist')),
  packaged,
  version: '1.0.4',
};
