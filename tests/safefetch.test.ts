// Links typed by the user (citations, plagiarism sources, DOIs) must never reach this computer or a private network.
// The positive tests talk to a throwaway server on localhost, which is exempted by name; everything else is refused.
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import { BlockedAddressError, isPrivateAddress, safeGet } from '../server/safeFetch';

describe('isPrivateAddress', () => {
  it.each([
    '127.0.0.1', '127.8.8.8', '10.1.2.3', '172.16.0.1', '172.31.255.255', '192.168.1.1', '169.254.169.254', '0.0.0.0',
    '100.64.0.1', '224.0.0.1', '255.255.255.255', '::1', '::', 'fe80::1', 'fc00::1', 'fd12:3456::1', '::ffff:127.0.0.1',
    '::ffff:7f00:1', '::ffff:a9fe:a9fe', '64:ff9b::7f00:1', '[::1]',
  ])('refuses %s', (a) => expect(isPrivateAddress(a)).toBe(true));

  it.each(['8.8.8.8', '1.1.1.1', '172.15.0.1', '172.32.0.1', '93.184.216.34', '2606:4700:4700::1111', '::ffff:8.8.8.8', 'example.com'])(
    'allows %s',
    (a) => expect(isPrivateAddress(a)).toBe(false),
  );
});

let server: http.Server | null = null;
afterEach(() => new Promise<void>((done) => (server ? server.close(() => done()) : done())).then(() => void (server = null)));

async function serve(handler: http.RequestListener): Promise<number> {
  server = http.createServer(handler);
  await new Promise<void>((r) => server!.listen(0, '127.0.0.1', r));
  return (server.address() as AddressInfo).port;
}

describe('safeGet', () => {
  it('refuses private addresses written as IPs, names that resolve to them, and odd spellings of 127.0.0.1', async () => {
    for (const u of ['http://127.0.0.1:4317/api/docs', 'http://localhost:4317/api/docs', 'http://[::1]:4317/', 'http://169.254.169.254/latest/meta-data/',
      'http://2130706433/', 'http://0x7f.1/', 'http://[::ffff:127.0.0.1]/']) {
      await expect(safeGet(u, { timeoutMs: 3000 }), u).rejects.toBeInstanceOf(BlockedAddressError);
    }
  });

  it('refuses a redirect that leads to a private address', async () => {
    const port = await serve((req, res) => {
      res.writeHead(302, { Location: req.url === '/to-ip' ? `http://127.0.0.1:${(server!.address() as AddressInfo).port}/secret` : 'http://localhost/' });
      res.end();
    });
    // The first hop (localhost) is exempt for the test; the redirect target 127.0.0.1 is not.
    await expect(safeGet(`http://localhost:${port}/to-ip`, { allowPrivateFor: ['localhost'] })).rejects.toBeInstanceOf(BlockedAddressError);
  });

  it('fetches, follows redirects and returns the final address', async () => {
    const port = await serve((req, res) => {
      if (req.url === '/start') {
        res.writeHead(301, { Location: '/end' });
        res.end();
      } else {
        res.writeHead(200, { 'Content-Type': 'text/html' });
        res.end('<title>Hi</title>');
      }
    });
    const r = await safeGet(`http://localhost:${port}/start`, { allowPrivateFor: ['localhost'] });
    expect(r.ok).toBe(true);
    expect(r.url).toBe(`http://localhost:${port}/end`);
    expect(r.body.toString()).toBe('<title>Hi</title>');
  });

  it('stops after too many redirects and keeps only maxBytes of a large body', async () => {
    const port = await serve((req, res) => {
      if (req.url === '/loop') {
        res.writeHead(302, { Location: '/loop' });
        res.end();
      } else {
        res.writeHead(200);
        res.end('x'.repeat(100_000));
      }
    });
    await expect(safeGet(`http://localhost:${port}/loop`, { allowPrivateFor: ['localhost'], maxRedirects: 3 })).rejects.toThrow(/too many/);
    const big = await safeGet(`http://localhost:${port}/big`, { allowPrivateFor: ['localhost'], maxBytes: 1000 });
    expect(big.body.length).toBe(1000);
  });

  it('only fetches http(s)', async () => {
    await expect(safeGet('file:///C:/Windows/win.ini')).rejects.toThrow(/http/);
    await expect(safeGet('ftp://example.com/x')).rejects.toThrow(/http/);
  });
});
