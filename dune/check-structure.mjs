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
    .filter((file) => !toPosix(file).includes('/Tests/'));
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

async function checkTestsColocated() {
  const features = await listFeatures();
  if (features.length === 0) {
    fail('sin features: falta backend/Features con al menos un directorio');
    return;
  }
  for (const feature of features) {
    const dir = path.join(backend, 'Features', feature, 'Tests');
    if (!(await exists(dir))) {
      fail(`tests de ${feature} fuera de su feature: falta backend/Features/${feature}/Tests/`);
      continue;
    }
    const collected = [];
    await collectFiles(dir, collected);
    if (!collected.some((file) => file.endsWith('.cs'))) {
      fail(`tests de ${feature} fuera de su feature: backend/Features/${feature}/Tests/ sin *.cs`);
    } else {
      notes.push(`tests colocados: backend/Features/${feature}/Tests/`);
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
  for (const pattern of ['Features/**/Tests/**/*.cs', 'Tests/**/*.cs']) {
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
  const includesTestsGlob = /<Compile\s+Include="[^"]*Features\/\*\/Tests\/\*\*\/\*\.cs"[^>]*\/>/.test(normalized)
    || /<Compile\s+Include="[^"]*Features\/\*\*\/\*\.cs"[^>]*\/>/.test(normalized);
  if (!testsText.includes('TemplateApp.csproj') && !includesTestsGlob) {
    fail('backend/Tests/TemplateApp.Tests.csproj no referencia el producto ni incluye Features Tests por glob');
  }
}

async function checkNoWolverine() {
  const files = await productSources();
  const hits = [];
  const generated = new Set(['Infrastructure/Wolverine/Generated']);
  for (const file of files) {
    const rel = toPosix(file);
    if ([...generated].some((prefix) => rel.includes(prefix))) continue;
    const text = await readText(file);
    if (/\bWolverine(Fx)?\b/.test(text) || /\bJasperFx\b/.test(text) || /\bIMessageBus\b/.test(text)) {
      hits.push(rel);
    }
  }
  if (hits.length > 0) fail(`residuo Wolverine/JasperFx en producto: ${hits.join(', ')}`);
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

async function checkOpenspecSingleSource() {
  const active = [];
  await collectFiles(path.join(root, 'openspec', 'specs'), active);
  const legacy = path.join(root, 'backend', 'openspec');
  if (await exists(legacy)) {
    const leftovers = [];
    await collectFiles(legacy, leftovers);
    if (leftovers.length > 0) fail(`openspec duplicado: backend/openspec aún contiene ${leftovers.length} archivo(s)`);
  }
  const specs = active.filter((file) => file.endsWith('.md'));
  if (specs.length === 0) fail('openspec/specs sin especificaciones activas');
}

await checkTestsColocated();
await checkProductExcludesTests();
await checkNoWolverine();
await checkNoToggleAlias();
await checkTrackedArtifacts();
await checkNoNestedGit();
await checkOpenspecSingleSource();

if (failures.length > 0) {
  console.error('check-structure: FAIL');
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log('check-structure: OK');
for (const note of notes) console.log(`- ${note}`);
