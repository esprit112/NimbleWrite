// NimbleWrite server: JSON API + (in production) the built web app on one port.
import express, { type NextFunction, type Request, type Response } from 'express';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { ZodError } from 'zod';
import { config } from './config';
import { NimError } from './nim';
import { loadStore } from './store';
import { aiRouter } from './routes/ai';
import { dataRouter } from './routes/data';

const app = express();
app.disable('x-powered-by');

// DNS-rebinding guard: a website that points its own domain at 127.0.0.1 still sends its own name as the Host, and
// same-origin requests carry no Origin header, so only the Host shows it isn't one of this computer's own pages.
app.use((req, res, next) => {
  if (!/^(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$/i.test(req.headers.host ?? '')) {
    res.status(403).json({ error: 'NimbleWrite only answers requests addressed to this computer.' });
    return;
  }
  next();
});

app.use(express.json({ limit: '12mb' }));

// Only accept API calls from this machine's own pages (blocks other sites from driving your NIM key).
app.use('/api', (req, res, next) => {
  const origin = req.headers.origin;
  if (origin && !/^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$/.test(origin)) {
    res.status(403).json({ error: 'Cross-origin requests are not allowed' });
    return;
  }
  next();
});

app.use('/api/ai', aiRouter);
app.use('/api', dataRouter);
app.use('/api', (_req, res) => {
  res.status(404).json({ error: 'Not found' });
});

const indexHtml = path.join(config.distDir, 'index.html');
if (existsSync(indexHtml)) {
  app.use(express.static(config.distDir, { index: false, maxAge: '1h' }));
  app.use((req, res, next) => {
    if (req.method !== 'GET') return next();
    res.sendFile(indexHtml);
  });
}

app.use((err: unknown, _req: Request, res: Response, _next: NextFunction) => {
  if (res.headersSent) {
    res.end();
    return;
  }
  if (err instanceof ZodError) {
    res.status(400).json({ error: `Invalid request: ${err.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; ')}` });
    return;
  }
  if (err instanceof NimError) {
    res.status(err.status === 499 ? 499 : err.status >= 400 && err.status < 600 ? err.status : 502).json({ error: err.message });
    return;
  }
  // Malformed or oversized request bodies are the client's mistake. Never log them: these errors carry the body.
  const status = (err as { status?: unknown } | null)?.status;
  if (typeof status === 'number' && status >= 400 && status < 500) {
    res.status(status).json({ error: status === 413 ? 'That request is too large.' : 'Invalid request.' });
    return;
  }
  console.error('[NimbleWrite]', describeError(err));
  res.status(500).json({ error: err instanceof Error ? err.message : 'Unexpected server error' });
});

/** The error's type, message and where it happened, but none of its other properties: request data never reaches the log. */
function describeError(err: unknown): string {
  if (!(err instanceof Error)) return `non-Error value (${typeof err})`;
  const frames = (err.stack ?? '').split('\n').slice(1, 6).join('\n');
  return `${err.name}: ${err.message.slice(0, 200)}${frames ? `\n${frames}` : ''}`;
}

// Installed copy: exit when the desktop app that started this server has gone, so no orphan keeps the port.
const parentPid = Number(process.env.NIMBLEWRITE_PARENT_PID);
if (Number.isInteger(parentPid) && parentPid > 0) {
  setInterval(() => {
    try {
      process.kill(parentPid, 0);
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === 'ESRCH') process.exit(0);
    }
  }, 2000).unref();
}

await loadStore();
const server = app.listen(config.port, '127.0.0.1', () => {
  const ui = existsSync(indexHtml) ? `http://localhost:${config.port}` : 'http://localhost:5317 (vite dev server)';
  const keyHelp = config.packaged ? 'add it from the NimbleWrite tray icon' : 'set NVIDIA_API_KEY in .env';
  console.log(`NimbleWrite API on http://127.0.0.1:${config.port}  |  app: ${ui}`);
  console.log(`NVIDIA NIM key: ${config.nim.apiKey ? 'configured' : `MISSING (${keyHelp})`}  |  web search: ${config.braveKey ? 'Brave' : 'off'}`);
});
server.on('error', (err: NodeJS.ErrnoException) => {
  console.error(err.code === 'EADDRINUSE' ? `[NimbleWrite] port ${config.port} is already in use` : `[NimbleWrite] ${describeError(err)}`);
  process.exit(1);
});
