import { readdir, readFile, stat } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { promisify } from 'node:util';

const root = process.cwd();
const backend = path.join(root, 'backend');
const failures = [];
const notes = [];

function fail(message) {
  failures.push(message);
}

async function exists(target) {
  try {
    await stat(target);
    return true;
  } catch {
    return false;
  }
}

async function readText(target) {
  return readFile(target, 'utf8');
}

async function collectFiles(dir, out) {
  let entries = [];
  try {
    entries = await readdir(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name === 'bin' || entry.name === 'obj') continue;
      await collectFiles(full, out);
    } else if (entry.isFile()) {
      out.push(full);
    }
  }
}

function toPosix(target) {
  return path.relative(root, target).replaceAll(path.sep, '/');
}

async function productSources() {
  const all = [];
  await collectFiles(path.join(backend, 'Features'), all);
  await collectFiles(path.join(backend, 'Shared'), all);
  await collectFiles(path.join(backend, 'Infrastructure'), all);
  const product = path.join(backend, 'TemplateApp.csproj');
  if (await exists(product)) all.push(product);
  if (await exists(path.join(backend, 'Program.cs'))) all.push(path.join(backend, 'Program.cs'));
  return all
    .filter((file) => file.endsWith('.cs') || file.endsWith('.csproj'))
    .filter((file) => !toPosix(file).includes('/Tests/') && !file.endsWith('Tests.cs'));
}

async function listFeatures() {
  const featuresRoot = path.join(backend, 'Features');
  let entries = [];
  try {
    entries = await readdir(featuresRoot, { withFileTypes: true });
  } catch {
    return [];
  }
  return entries.filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort();
}

const FEATURE_FOLDERS = new Set(['Contracts', 'Domain', 'Persistence']);

async function checkSlicesHaveTests() {
  const features = await listFeatures();
  if (features.length === 0) {
    fail('sin features: falta backend/Features con al menos un directorio');
    return;
  }
  for (const feature of features) {
    const featureDir = path.join(backend, 'Features', feature);
    if (await exists(path.join(featureDir, 'Tests'))) {
      fail(`tests de ${feature} fuera de sus slices: backend/Features/${feature}/Tests/ ya no se usa`);
    }
    const entries = await readdir(featureDir, { withFileTypes: true });
    const slices = [];
    for (const entry of entries) {
      if (!entry.isDirectory() || FEATURE_FOLDERS.has(entry.name)) continue;
      if (await exists(path.join(featureDir, entry.name, `${entry.name}Endpoint.cs`))) slices.push(entry.name);
    }
    if (slices.length === 0) {
      fail(`${feature} sin slices: falta backend/Features/${feature}/<Slice>/<Slice>Endpoint.cs`);
      continue;
    }
    for (const slice of slices.sort()) {
      if (await exists(path.join(featureDir, slice, `${slice}Tests.cs`))) {
        notes.push(`slice con test: backend/Features/${feature}/${slice}/`);
      } else {
        fail(`slice ${feature}/${slice} sin ${slice}Tests.cs`);
      }
    }
  }
}

async function checkProductExcludesTests() {
  const csproj = path.join(backend, 'TemplateApp.csproj');
  if (!(await exists(csproj))) {
    fail('falta backend/TemplateApp.csproj');
    return;
  }
  const text = await readText(csproj);
  for (const pattern of ['Features/**/*Tests.cs', 'Tests/**/*.cs']) {
    if (!text.includes(pattern)) {
      fail(`TemplateApp.csproj no excluye tests del producto: falta Compile Remove "${pattern}"`);
    }
  }
  const testsCsproj = path.join(backend, 'Tests', 'TemplateApp.Tests.csproj');
  if (!(await exists(testsCsproj))) {
    fail('falta backend/Tests/TemplateApp.Tests.csproj');
    return;
  }
  const testsText = await readText(testsCsproj);
  const normalized = testsText.replaceAll('\\', '/');
  const includesTestsGlob = /<Compile\s+Include="[^"]*Features\/\*\*\/\*Tests\.cs"[^>]*\/>/.test(normalized);
  if (!testsText.includes('TemplateApp.csproj') && !includesTestsGlob) {
    fail('backend/Tests/TemplateApp.Tests.csproj no referencia el producto ni incluye Features/**/*Tests.cs por glob');
  }
}

const BUS_LIBRARIES = [
  'MediatR',
  'Mediator',
  'MassTransit',
  'Wolverine',
  'WolverineFx',
  'JasperFx',
  'NServiceBus',
  'Rebus',
  'Paramore.Brighter',
  'Paramore.Darker',
  'DotNetCore.CAP',
  'SlimMessageBus',
  'LiteBus',
];

function isBusLibrary(name) {
  return BUS_LIBRARIES.some((library) => name === library || name.startsWith(`${library}.`));
}

async function buildSources() {
  const all = [];
  for (const name of ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']) {
    for (const dir of [root, backend]) {
      const candidate = path.join(dir, name);
      if (await exists(candidate)) all.push(candidate);
    }
  }
  for (const candidate of [path.join(root, 'dune', 'Dune.props'), path.join(backend, 'Tests', 'TemplateApp.Tests.csproj')]) {
    if (await exists(candidate)) all.push(candidate);
  }
  return all;
}

async function internalsSources() {
  const all = [];
  await collectFiles(backend, all);
  const own = all.filter((file) => {
    const rel = toPosix(file);
    return /\.(cs|csproj|props|targets)$/.test(rel) && !rel.startsWith('backend/Tests/') && !rel.endsWith('Tests.cs');
  });
  return [...new Set([...own, ...(await buildSources())])];
}

async function checkInternalsOnlyForTests() {
  const hits = [];
  const msbuild = [
    /<InternalsVisibleTo\b[^>]*?\b(?:Include|Update)\s*=\s*["']([^"']+)["']/g,
    /<AssemblyAttribute\b[^>]*?\bInclude\s*=\s*["']System\.Runtime\.CompilerServices\.InternalsVisibleTo(?:Attribute)?["'][^>]*?(?:\b_Parameter1\s*=\s*["']([^"']+)["']|>\s*<_Parameter1>([^<]+)<\/_Parameter1>)/g,
  ];
  const csharp = /\bInternalsVisibleTo(?:Attribute)?\s*\(\s*(?:assemblyName\s*:\s*)?(?:"([^"]*)"|([^)]*))/g;
  for (const file of await internalsSources()) {
    const text = await readText(file);
    const patterns = file.endsWith('.cs') ? [csharp] : msbuild;
    for (const pattern of patterns) {
      for (const match of text.matchAll(pattern)) {
        const literal = match[1] ?? (file.endsWith('.cs') ? undefined : match[2]);
        const target = literal === undefined ? `argumento no literal: ${match[2].trim()}` : literal.split(',')[0].trim();
        if (target !== 'TemplateApp.Tests') hits.push(`${toPosix(file)} (${target})`);
      }
    }
  }
  if (hits.length > 0) fail(`InternalsVisibleTo solo para TemplateApp.Tests: el interior de las features no se abre a otro ensamblado: ${hits.join(', ')}`);
}

async function checkNoBus() {
  const hits = [];
  const references = /<(?:PackageReference|PackageVersion|Using)\s+(?:Include|Update)="([^"]+)"/g;
  const usings = /^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?([\w.]+)\s*;/gm;
  for (const file of [...(await productSources()), ...(await buildSources())]) {
    const text = await readText(file);
    const pattern = file.endsWith('.cs') ? usings : references;
    for (const match of text.matchAll(pattern)) {
      if (isBusLibrary(match[1])) hits.push(`${toPosix(file)} (${match[1]})`);
    }
  }
  if (hits.length > 0) fail(`bus o mediator antes del paso 25: ${hits.join(', ')}`);
}

const CACHE_LIBRARIES = [
  'Microsoft.Extensions.Caching',
  'Microsoft.AspNetCore.OutputCaching',
  'Microsoft.AspNetCore.ResponseCaching',
  'ZiggyCreatures.FusionCache',
  'ZiggyCreatures.Caching',
  'EasyCaching',
  'LazyCache',
  'CacheManager',
  'CacheTower',
];

function isCacheLibrary(name) {
  return CACHE_LIBRARIES.some((library) => name === library || name.startsWith(`${library}.`));
}

async function checkNoCrossRequestCache() {
  const hits = [];
  const references = /<(?:PackageReference|PackageVersion|Using)\s+(?:Include|Update)="([^"]+)"/g;
  const namespaces = /\b(?:global::)?((?:[A-Z]\w*\.)*[A-Z]\w*)\b/g;
  const registrations = /\b(Add\w*Cach(?:e|ing)\w*|UseOutputCache|UseResponseCaching|CacheOutput)\s*[<(]/g;
  for (const file of [...(await productSources()), ...(await buildSources())]) {
    const text = await readText(file);
    if (!file.endsWith('.cs')) {
      for (const match of text.matchAll(references)) {
        if (isCacheLibrary(match[1])) hits.push(`${toPosix(file)} (${match[1]})`);
      }
      continue;
    }
    const found = new Set();
    for (const match of text.matchAll(namespaces)) {
      if (isCacheLibrary(match[1])) found.add(match[1]);
    }
    for (const match of text.matchAll(registrations)) found.add(match[1]);
    for (const name of found) hits.push(`${toPosix(file)} (${name})`);
  }
  if (hits.length > 0) fail(`caché entre requests antes del paso 33: ${hits.join(', ')}`);
}

async function checkRateLimitExemptionOnlyInHealth() {
  const hits = [];
  const exemption = /\bDisableRateLimiting(?:Attribute)?\b/;
  for (const file of await productSources()) {
    if (!file.endsWith('.cs')) continue;
    const rel = toPosix(file);
    if (rel.startsWith('backend/Features/Health/')) continue;
    if (exemption.test(await readText(file))) hits.push(rel);
  }
  if (hits.length > 0) fail(`limiter global: DisableRateLimiting solo en backend/Features/Health: ${hits.join(', ')}`);
}

function coversCSharp(section) {
  if (section === '*' || section === '**') return true;
  if (/^\*\*?\.cs$/.test(section)) return true;
  const list = /^\*\*?\.\{([^}]*)\}$/.exec(section);
  return list !== null && list[1].split(',').map((item) => item.trim()).includes('cs');
}

async function checkEditorconfigSeverities() {
  const file = path.join(root, '.editorconfig');
  if (!(await exists(file))) return;
  const misplaced = [];
  let section = null;
  const lines = (await readText(file)).split(/\r?\n/);
  lines.forEach((line, index) => {
    const header = /^\s*\[(.+)\]\s*$/.exec(line);
    if (header) {
      section = header[1];
      return;
    }
    if (!/^\s*dotnet_diagnostic\./.test(line)) return;
    if (section === null || !coversCSharp(section)) misplaced.push(`línea ${index + 1} bajo [${section ?? ''}]`);
  });
  if (misplaced.length > 0) {
    fail(`.editorconfig con severidades que no aplican a *.cs: ${misplaced.join(', ')}`);
  }
}

async function checkNoToggleAlias() {
  const files = await productSources();
  const hits = [];
  for (const file of files) {
    const text = await readText(file);
    if (/ToggleTask|toggle/i.test(text) && toPosix(file).toLowerCase().includes('task')) {
      hits.push(toPosix(file));
    }
  }
  const filtered = hits.filter((hit) => !hit.includes('/Tests/'));
  if (filtered.length > 0) fail(`alias toggle residual en producto: ${filtered.join(', ')}`);
}

async function listTrackedPaths() {
  try {
    const { stdout } = await promisify(execFile)('git', ['ls-files', '-z', '--cached'], { cwd: root });
    if (!stdout) return [];
    return String(stdout).split('\0').filter(Boolean);
  } catch {
    return null;
  }
}

function isTrackedArtifact(rel) {
  if (/(^|\/)(bin|obj)(\/|$)/.test(rel)) return true;
  return /\.(nupkg|snupkg|trx|db|db-shm|db-wal)$/.test(rel);
}

async function checkTrackedArtifacts() {
  const tracked = await listTrackedPaths();
  if (tracked === null) {
    fail('no se pudo inventariar archivos versionados: git ls-files falló (¿repo git disponible?)');
    return;
  }
  const offenders = tracked.filter(isTrackedArtifact);
  if (offenders.length > 0) fail(`artefactos versionados: ${offenders.join(', ')}`);
}

async function checkNoNestedGit() {
  const offenders = [];
  const prune = new Set([
    'node_modules',
    'bin',
    'obj',
    'dist',
    '.angular',
    'coverage',
    'TestResults',
    'artifacts',
    'Generated',
    '.vs',
    '.idea',
    'otel-export',
  ]);
  async function scan(dir) {
    let entries = [];
    try {
      entries = await readdir(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.name === '.git') {
        if (path.resolve(dir) === path.resolve(root)) continue;
        const marker = entry.isDirectory() ? `${toPosix(full)}/` : toPosix(full);
        offenders.push(`${marker} (git anidado)`);
        continue;
      }
      if (!entry.isDirectory()) continue;
      if (prune.has(entry.name)) continue;
      await scan(full);
    }
  }
  await scan(root);
  if (offenders.length > 0) fail(`git anidado: ${offenders.join(', ')}`);
}

await checkSlicesHaveTests();
await checkProductExcludesTests();
await checkInternalsOnlyForTests();
await checkNoBus();
await checkNoCrossRequestCache();
await checkRateLimitExemptionOnlyInHealth();
await checkEditorconfigSeverities();
await checkNoToggleAlias();
await checkTrackedArtifacts();
await checkNoNestedGit();

if (failures.length > 0) {
  console.error('check-structure: FAIL');
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log('check-structure: OK');
for (const note of notes) console.log(`- ${note}`);
