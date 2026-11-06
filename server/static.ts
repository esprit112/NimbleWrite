// Serves the built web app (dist/) from the same port as the API.
import express, { type Express } from 'express';
import { existsSync } from 'node:fs';
import path from 'node:path';

const YEAR = 'public, max-age=31536000, immutable';

/**
 * Vite puts a content hash in every file name under /assets, so a file there never changes: browsers may keep it for
 * a year without asking. Everything else (index.html above all, which names the current assets) is revalidated on
 * every load, so an update shows up straight away. Any other GET is a page of the app, answered with index.html.
 * Returns false (and serves nothing) when the web app hasn't been built.
 */
export function serveWebApp(app: Express, distDir: string): boolean {
  const indexHtml = path.join(distDir, 'index.html');
  if (!existsSync(indexHtml)) return false;
  app.use(
    '/assets',
    express.static(path.join(distDir, 'assets'), {
      index: false,
      fallthrough: false, // a missing asset is a 404, not the app page (which the browser would try to run as script)
      setHeaders: (res) => res.setHeader('Cache-Control', YEAR),
    }),
  );
  app.use(express.static(distDir, { index: false, setHeaders: (res) => res.setHeader('Cache-Control', 'no-cache') }));
  app.use((req, res, next) => {
    if (req.method !== 'GET') return next();
    res.setHeader('Cache-Control', 'no-cache');
    res.sendFile(indexHtml);
  });
  return true;
}
