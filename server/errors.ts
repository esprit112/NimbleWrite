// The API's error handler: Zod and NIM errors become clean JSON responses, and nothing from a request reaches the log.
import type { NextFunction, Request, Response } from 'express';
import { ZodError } from 'zod';
import { NimError } from './nim';

export function errorHandler(err: unknown, _req: Request, res: Response, _next: NextFunction): void {
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
}

/** The error's type, message and where it happened, but none of its other properties: request data never reaches the log. */
export function describeError(err: unknown): string {
  if (!(err instanceof Error)) return `non-Error value (${typeof err})`;
  const frames = (err.stack ?? '').split('\n').slice(1, 6).join('\n');
  return `${err.name}: ${err.message.slice(0, 200)}${frames ? `\n${frames}` : ''}`;
}
