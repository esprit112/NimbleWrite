// NimbleWrite server: JSON API + (in production) the built web app on one port.
import express from 'express';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { config } from './config';
import { describeError, errorHandler } from './errors';
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

app.use(errorHandler);

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
