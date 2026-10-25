// Builds the Windows installer:  npm run package:win  ->  build/installer/NimbleWrite-Setup-<version>.exe
//
//  1. web editor (vite build, plus the bundle's own secret check)
//  2. server as one self-contained file (esbuild inlines every dependency, so no node_modules ship)
//  3. Node.js runtime: this node.exe, plus its licence
//  4. desktop app: dotnet publish, self-contained (the PC needs no .NET install)
//  5. third-party notices for everything that ships
//  6. secret scan of everything staged: the build stops if any NVIDIA key, .env file or data folder is found
//  7. Inno Setup compiles the installer
//
// Needs Windows, Node 24+, the .NET 10 SDK and Inno Setup 6 (set ISCC to ISCC.exe's path if it isn't found).
// Installed copies use each person's own NVIDIA key: nothing from .env is ever packaged.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFileSync, cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const pkg = JSON.parse(readFileSync(path.join(root, 'package.json'), 'utf8'));
const version = pkg.version;
const stage = path.join(root, 'build', 'stage');
const installerOut = path.join(root, 'build', 'installer');
const started = Date.now();

const mb = (n) => `${(n / 1048576).toFixed(1)} MB`;
const rel = (p) => path.relative(root, p).replaceAll('\\', '/');
const step = (n, text) => console.log(`\n[${n}/7] ${text}`);
const run = (cmd, args, opts = {}) => execFileSync(cmd, args, { cwd: root, stdio: 'inherit', ...opts });
// npm is a .cmd script, so it needs a shell; the whole command goes in one string (no argument array with shell: true).
const npm = (command, opts = {}) => execFileSync(`npm ${command}`, { cwd: root, shell: true, ...opts });

if (process.platform !== 'win32') throw new Error('The Windows installer has to be built on Windows.');
const iscc = findIscc();
rmSync(stage, { recursive: true, force: true });
mkdirSync(stage, { recursive: true });

step(1, 'Web editor');
npm('run build', { stdio: 'inherit' });
cpSync(path.join(root, 'dist'), path.join(stage, 'web'), { recursive: true });

step(2, 'Server bundle');
const serverDir = path.join(stage, 'server');
const bundle = await build({
  entryPoints: [path.join(root, 'server', 'index.ts')],
  outfile: path.join(serverDir, 'nimblewrite-server.mjs'),
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node24',
  metafile: true,
  legalComments: 'eof',
  logLevel: 'warning',
  // Dependencies written as CommonJS still call require() and read __dirname inside an ES module bundle.
  banner: {
    js: [
      "import { createRequire as __nwCreateRequire } from 'node:module';",
      "import { fileURLToPath as __nwFileURLToPath } from 'node:url';",
      "import { dirname as __nwDirname } from 'node:path';",
      'const require = __nwCreateRequire(import.meta.url);',
      'const __filename = __nwFileURLToPath(import.meta.url);',
      'const __dirname = __nwDirname(__filename);',
    ].join('\n'),
  },
});
const inputs = Object.keys(bundle.metafile.inputs);
const native = inputs.filter((f) => f.endsWith('.node'));
if (native.length) throw new Error(`The server needs native modules, which can't be bundled: ${native.join(', ')}`);
const serverPackages = packageDirs(inputs);
console.log(`  ${inputs.length} source files from ${serverPackages.size} packages -> ${rel(path.join(serverDir, 'nimblewrite-server.mjs'))}`);

step(3, 'Node.js runtime');
if (Number(process.versions.node.split('.')[0]) < 24) {
  throw new Error(`Build with Node 24 or later: this node.exe (${process.versions.node}) becomes the installed app's runtime.`);
}
copyFileSync(process.execPath, path.join(serverDir, 'node.exe'));
console.log(`  node.exe ${process.versions.node} (${mb(statSync(process.execPath).size)})`);

step(4, 'Desktop app (self-contained .NET)');
run('dotnet', [
  'publish', path.join(root, 'desktop', 'NimbleWrite.Desktop.csproj'),
  '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
  '-p:PublishSingleFile=false', '-p:DebugType=none', '-p:DebugSymbols=false', `-p:Version=${version}`,
  '-o', stage, '-nologo', '-v:minimal',
]);
for (const f of ['NimbleWrite.exe', 'nimblewrite.ico', 'hostfxr.dll', 'PresentationFramework.dll']) {
  if (!existsSync(path.join(stage, f))) throw new Error(`dotnet publish didn't produce ${f}`);
}

step(5, 'Third-party notices');
mkdirSync(path.join(stage, 'licenses'), { recursive: true });
const notices = buildNotices();
writeFileSync(path.join(stage, 'licenses', 'THIRD-PARTY-NOTICES.txt'), notices.text, 'utf8');
console.log(`  ${notices.count} components -> licenses/THIRD-PARTY-NOTICES.txt`);

step(6, 'Secret scan');
scanForSecrets();

step(7, 'Installer (Inno Setup)');
mkdirSync(installerOut, { recursive: true });
run(iscc, ['/Qp', `/DAppVersion=${version}`, `/DStageDir=${stage}`, `/O${installerOut}`, path.join(root, 'installer', 'NimbleWrite.iss')]);
const setup = path.join(installerOut, `NimbleWrite-Setup-${version}.exe`);
const sha = createHash('sha256').update(readFileSync(setup)).digest('hex');
writeFileSync(`${setup}.sha256`, `${sha}  ${path.basename(setup)}\n`);
console.log(`\nDone in ${Math.round((Date.now() - started) / 1000)} s: ${rel(setup)} (${mb(statSync(setup).size)})`);
console.log(`SHA-256 ${sha}`);

// ---------------------------------------------------------------------------------------------------------------------

function findIscc() {
  const roots = [process.env['ProgramFiles(x86)'], process.env.ProgramFiles, process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'Programs')];
  const candidates = [process.env.ISCC, ...roots.filter(Boolean).map((r) => path.join(r, 'Inno Setup 6', 'ISCC.exe'))].filter(Boolean);
  const found = candidates.find((p) => existsSync(p));
  if (!found) throw new Error('Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or set ISCC to the full path of ISCC.exe.');
  return found;
}

/** Package folders (name -> dir) that esbuild pulled source files from. */
function packageDirs(files) {
  const dirs = new Map();
  for (const f of files) {
    const norm = f.replaceAll('\\', '/');
    const i = norm.lastIndexOf('node_modules/');
    if (i < 0) continue;
    const parts = norm.slice(i + 13).split('/');
    const name = parts[0].startsWith('@') ? `${parts[0]}/${parts[1]}` : parts[0];
    dirs.set(name, path.resolve(root, norm.slice(0, i + 13) + name));
  }
  return dirs;
}

function* walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(p);
    else yield p;
  }
}

function licenceText(dir) {
  if (!existsSync(dir)) return null;
  const file = readdirSync(dir).find((n) => /^(licen[cs]e|copying|notice)(\.(md|txt|markdown))?$/i.test(n));
  return file ? readFileSync(path.join(dir, file), 'utf8').trim() : null;
}

function buildNotices() {
  const parts = [];
  const add = (title, licence, text) => parts.push(`${'='.repeat(100)}\n${title}\nLicence: ${licence}\n${'-'.repeat(100)}\n${text ?? '(The package has no separate licence file; see its licence identifier above.)'}\n`);

  add(`Node.js ${process.versions.node} (server/node.exe)`, 'MIT and others (below)', readFileSync(path.join(root, 'installer', 'licenses', 'node.txt'), 'utf8').trim());

  // .NET: the runtime packs this app was published with.
  const nuget = process.env.NUGET_PACKAGES || path.join(os.homedir(), '.nuget', 'packages');
  const runtimeConfig = JSON.parse(readFileSync(path.join(stage, 'NimbleWrite.runtimeconfig.json'), 'utf8'));
  for (const fw of runtimeConfig.runtimeOptions?.includedFrameworks ?? []) {
    const dir = path.join(nuget, `${fw.name.toLowerCase()}.runtime.win-x64`, fw.version);
    const licence = licenceText(dir);
    const third = existsSync(dir) ? readdirSync(dir).find((n) => /^third-party-notices\.txt$/i.test(n)) : null;
    add(`${fw.name} ${fw.version} (the .NET runtime inside the app folder)`, 'MIT', [licence, third && readFileSync(path.join(dir, third), 'utf8').trim()].filter(Boolean).join('\n\n') || null);
  }
  const uia = path.join(nuget, 'interop.uiautomationclient', '10.19041.0');
  add('Interop.UIAutomationClient 10.19041.0', nuspecLicence(uia), licenceText(uia));

  // npm packages: the production dependency tree, everything esbuild bundled, and the build tools whose code or CSS
  // ends up in the web bundle.
  const dirs = new Map();
  let listing = '';
  try {
    listing = npm('ls --omit=dev --all --parseable', { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
  } catch (err) {
    listing = String(err.stdout ?? ''); // npm ls exits 1 on peer-dependency warnings but still lists everything
  }
  for (const line of listing.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)) {
    if (path.resolve(line) !== root) dirs.set(path.resolve(line), true);
  }
  for (const dir of serverPackages.values()) dirs.set(dir, true);
  for (const name of ['vite', 'tailwindcss']) dirs.set(path.join(root, 'node_modules', name), true);

  const seen = new Set();
  const npmParts = [];
  for (const dir of dirs.keys()) {
    const pj = path.join(dir, 'package.json');
    if (!existsSync(pj)) continue;
    const p = JSON.parse(readFileSync(pj, 'utf8'));
    if (!p.name || seen.has(`${p.name}@${p.version}`)) continue;
    seen.add(`${p.name}@${p.version}`);
    npmParts.push({ name: p.name, version: p.version, licence: typeof p.license === 'string' ? p.license : p.license?.type ?? 'see package', text: licenceText(dir) });
  }
  npmParts.sort((a, b) => a.name.localeCompare(b.name));
  for (const p of npmParts) add(`${p.name} ${p.version} (npm)`, p.licence, p.text);

  const header = `NimbleWrite ${version} includes the third-party software listed below, under their own licences.\n` +
    `Generated by scripts/package-win.mjs on ${new Date().toISOString().slice(0, 10)}.\n\n`;
  return { text: header + parts.join('\n'), count: parts.length };
}

function nuspecLicence(dir) {
  if (!existsSync(dir)) return 'see package';
  const spec = readdirSync(dir).find((n) => n.endsWith('.nuspec'));
  if (!spec) return 'see package';
  const xml = readFileSync(path.join(dir, spec), 'utf8');
  return /<license[^>]*>([^<]+)<\/license>/i.exec(xml)?.[1] ?? /<licenseUrl>([^<]+)<\/licenseUrl>/i.exec(xml)?.[1] ?? 'see package';
}

/** Fails the build if an NVIDIA key (this repo's, or anything shaped like one), an .env file or user data was staged. */
function scanForSecrets() {
  let key = '';
  const envFile = path.join(root, '.env');
  if (existsSync(envFile)) {
    const m = /^\s*NVIDIA_API_KEY\s*=\s*(.+?)\s*$/m.exec(readFileSync(envFile, 'utf8'));
    key = (m?.[1] ?? '').replace(/^['"]|['"]$/g, '');
  }
  const needles = key.length > 12 ? [Buffer.from(key, 'utf8'), Buffer.from(key, 'utf16le')] : [];
  const shape = /nvapi-[A-Za-z0-9_-]{20,}/;
  const hits = [];
  let files = 0;
  let bytes = 0;
  for (const f of walk(stage)) {
    const name = path.basename(f).toLowerCase();
    if (name === '.env' || name.startsWith('.env.')) hits.push(`${rel(f)} (an .env file)`);
    const buf = readFileSync(f);
    files++;
    bytes += buf.length;
    // UTF-16 text can start at an odd byte too. Decode that from an aligned copy: on Node 24.19, toString('utf16le') on
    // a buffer that starts at an odd offset AND has an odd length kills the process outright (exit 127, no error).
    const found = needles.some((n) => buf.indexOf(n) >= 0) ||
      shape.test(buf.toString('latin1')) || shape.test(buf.toString('utf16le')) || shape.test(Buffer.from(buf.subarray(1)).toString('utf16le'));
    if (found) hits.push(rel(f));
  }
  if (existsSync(path.join(stage, 'data'))) hits.push('data/ (documents and settings)');
  if (hits.length) throw new Error(`SECRET FOUND in ${hits.join(', ')}. The installer was NOT built.`);
  console.log(`  OK: ${files} files (${mb(bytes)}) checked${key ? ", including for this repo's own key" : ''}: no NVIDIA key, .env file or user data.`);
}
