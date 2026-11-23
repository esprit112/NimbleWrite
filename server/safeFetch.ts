// HTTP GET for addresses a user typed in (citation links, plagiarism sources, DOIs).
//
// The server runs on the user's own PC with their key, so a link must never be able to reach the PC itself or its
// network: http://127.0.0.1:4317/api/docs, a router page, or a cloud metadata address (169.254.169.254). Two things
// are checked on every hop, redirects included:
//   1. a host given as an IP address is checked directly (Node never resolves those);
//   2. a host given as a name is resolved by our own `lookup`, which refuses private results at connect time, so a
//      name that answers differently the second time (DNS rebinding) can't get past a separate earlier check.
import dns from 'node:dns';
import http, { type IncomingHttpHeaders, type IncomingMessage } from 'node:http';
import https from 'node:https';
import net from 'node:net';
import zlib from 'node:zlib';

export class BlockedAddressError extends Error {
  constructor(public readonly host: string) {
    super('That address points at this computer or a private network, so it can\'t be fetched.');
    this.name = 'BlockedAddressError';
  }
}

const blocked = new net.BlockList();
for (const [net4, bits] of [
  ['0.0.0.0', 8], ['10.0.0.0', 8], ['100.64.0.0', 10], ['127.0.0.0', 8], ['169.254.0.0', 16], ['172.16.0.0', 12],
  ['192.0.0.0', 24], ['192.0.2.0', 24], ['192.168.0.0', 16], ['198.18.0.0', 15], ['198.51.100.0', 24],
  ['203.0.113.0', 24], ['224.0.0.0', 3], // multicast, reserved and broadcast
] as const) blocked.addSubnet(net4, bits, 'ipv4');
for (const [net6, bits] of [
  ['::', 128], ['::1', 128], ['fc00::', 7], ['fe80::', 10], ['ff00::', 8], ['2001:db8::', 32], ['100::', 64],
] as const) blocked.addSubnet(net6, bits, 'ipv6');

/** The IPv4 address inside an IPv4-mapped (::ffff:a.b.c.d / ::ffff:7f00:1) or NAT64 (64:ff9b::/96) IPv6 address, if any. */
function embeddedV4(addr: string): string | null {
  const m = /^(?:::ffff:|64:ff9b::)(?:(\d+\.\d+\.\d+\.\d+)|([0-9a-f]{1,4}):([0-9a-f]{1,4}))$/i.exec(addr);
  if (!m) return null;
  if (m[1]) return m[1];
  const hi = parseInt(m[2], 16);
  const lo = parseInt(m[3], 16);
  return `${hi >> 8}.${hi & 255}.${lo >> 8}.${lo & 255}`;
}

/** True for loopback, private, link-local, multicast and other non-public addresses (anything that isn't an IP is false). */
export function isPrivateAddress(address: string): boolean {
  const ip = address.replace(/^\[|\]$/g, '').replace(/%.*$/, '');
  const kind = net.isIP(ip);
  if (kind === 4) return blocked.check(ip, 'ipv4');
  if (kind === 6) {
    const v4 = embeddedV4(ip);
    return v4 ? blocked.check(v4, 'ipv4') : blocked.check(ip, 'ipv6');
  }
  return false;
}

export interface SafeGetOptions {
  headers?: Record<string, string>;
  /** Whole request, redirects included. */
  timeoutMs?: number;
  /** Bytes of (decompressed) body to keep; the rest is dropped. */
  maxBytes?: number;
  maxRedirects?: number;
  /** Host names exempt from the private-address check. For tests that talk to a local server; never set it from user input. */
  allowPrivateFor?: readonly string[];
}

export interface SafeResponse {
  status: number;
  ok: boolean;
  /** The address finally answered, after redirects. */
  url: string;
  headers: IncomingHttpHeaders;
  body: Buffer;
}

const REDIRECTS = new Set([301, 302, 303, 307, 308]);

function guardedLookup(exempt: boolean): net.LookupFunction {
  return (hostname, options, cb) => {
    dns.lookup(hostname, { ...options, all: true }, (err, addrs) => {
      if (err) return (cb as (e: NodeJS.ErrnoException | null, a?: string, f?: number) => void)(err);
      if (!exempt && addrs.some((a) => isPrivateAddress(a.address))) return (cb as (e: Error) => void)(new BlockedAddressError(hostname));
      if (options.all) (cb as (e: null, a: dns.LookupAddress[]) => void)(null, addrs);
      else (cb as (e: null, a: string, f: number) => void)(null, addrs[0].address, addrs[0].family);
    });
  };
}

function once(url: URL, o: SafeGetOptions, signal: AbortSignal): Promise<{ res: IncomingMessage }> {
  const exempt = o.allowPrivateFor?.includes(url.hostname) ?? false;
  if (!exempt && isPrivateAddress(url.hostname)) return Promise.reject(new BlockedAddressError(url.hostname));
  return new Promise((resolve, reject) => {
    const lib = url.protocol === 'https:' ? https : http;
    const req = lib.request(
      url,
      { method: 'GET', headers: { 'Accept-Encoding': 'gzip, deflate, br', ...o.headers }, lookup: guardedLookup(exempt), signal },
      (res) => resolve({ res }),
    );
    req.on('error', reject);
    req.end();
  });
}

async function readBody(res: IncomingMessage, maxBytes: number): Promise<Buffer> {
  const enc = String(res.headers['content-encoding'] ?? '').toLowerCase();
  const stream: NodeJS.ReadableStream =
    enc === 'gzip' || enc === 'x-gzip' ? res.pipe(zlib.createGunzip())
    : enc === 'deflate' ? res.pipe(zlib.createInflate())
    : enc === 'br' ? res.pipe(zlib.createBrotliDecompress())
    : res;
  const chunks: Buffer[] = [];
  let size = 0;
  try {
    for await (const chunk of stream) {
      const buf = chunk as Buffer;
      size += buf.length;
      if (size > maxBytes) {
        chunks.push(buf.subarray(0, buf.length - (size - maxBytes)));
        break;
      }
      chunks.push(buf);
    }
  } finally {
    res.destroy(); // stops the download (and a decompression bomb) as soon as enough has arrived
  }
  return Buffer.concat(chunks);
}

/** GET a user-supplied http(s) address, refusing private destinations on every hop. Throws BlockedAddressError for them. */
export async function safeGet(input: string, o: SafeGetOptions = {}): Promise<SafeResponse> {
  const maxRedirects = o.maxRedirects ?? 5;
  const signal = AbortSignal.timeout(o.timeoutMs ?? 12_000);
  let url = new URL(input);
  for (let hop = 0; ; hop++) {
    if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error('Only http(s) links can be fetched');
    const { res } = await once(url, o, signal);
    const status = res.statusCode ?? 0;
    const location = res.headers.location;
    if (REDIRECTS.has(status) && location) {
      res.resume();
      res.destroy();
      if (hop >= maxRedirects) throw new Error('The link redirects too many times');
      url = new URL(location, url);
      continue;
    }
    const body = await readBody(res, o.maxBytes ?? 3 * 1024 * 1024);
    return { status, ok: status >= 200 && status < 300, url: url.href, headers: res.headers, body };
  }
}
