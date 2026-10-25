// Post-build guard: fail the build if the NVIDIA key (or anything shaped like one) reached the browser bundle.
// Prints only file names, never the key.
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dist = path.join(root, 'dist');
if (!existsSync(dist)) {
  console.error('check-bundle: dist/ not found. Run vite build first.');
  process.exit(1);
}

let key = '';
const envFile = path.join(root, '.env');
if (existsSync(envFile)) {
  const m = /^\s*NVIDIA_API_KEY\s*=\s*(.+?)\s*$/m.exec(readFileSync(envFile, 'utf8'));
  key = (m?.[1] ?? '').replace(/^['"]|['"]$/g, '');
}

const files = [];
(function walk(dir) {
  for (const name of readdirSync(dir)) {
    const p = path.join(dir, name);
    if (statSync(p).isDirectory()) walk(p);
    else files.push(p);
  }
})(dist);

const leaks = files.filter((f) => {
  const text = readFileSync(f, 'utf8');
  return /nvapi-[A-Za-z0-9_-]{20,}/.test(text) || (key.length > 12 && text.includes(key));
});

if (leaks.length) {
  console.error(`check-bundle: SECRET FOUND in ${leaks.map((f) => path.relative(root, f)).join(', ')}. Do not deploy this build.`);
  process.exit(1);
}
console.log(`check-bundle: OK, no API key in ${files.length} bundle files.`);
