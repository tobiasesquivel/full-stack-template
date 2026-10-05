import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

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

// Product sources exclude colocated feature tests; the tests project owns them.
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

async function checkTestsColocated() {
  const features = ['Auth', 'Users', 'Tasks', 'Health'];
  for (const feature of features) {
    const dir = path.join(backend, 'Features', feature, 'Tests');
    if (!(await exists(dir))) {
      fail(`tests de ${feature} fuera de su feature: falta backend/Features/${feature}/Tests/`);
      continue;
    }
    const entries = await readdir(dir);
    if (!entries.some((entry) => entry.endsWith('.cs'))) {
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
  if (!testsText.includes('TemplateApp.csproj') && !testsText.includes('Features/')) {
    fail('backend/Tests/TemplateApp.Tests.csproj no referencia el producto ni incluye Features/*/Tests');
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

async function checkNoNestedGitOrArtifacts() {
  const offenders = [];
  async function scan(dir, depth) {
    if (depth > 6) return;
    let entries = [];
    try {
      entries = await readdir(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      const rel = toPosix(full);
      if (entry.name === 'node_modules') continue;
      if (entry.isDirectory() && entry.name === '.git' && path.resolve(dir) !== path.resolve(root)) {
        offenders.push(`${rel}/ (git anidado)`);
        continue;
      }
      if (entry.isFile()) {
        if (/\.(nupkg|snupkg|trx|db|db-shm|db-wal)$/.test(entry.name)) offenders.push(rel);
        if (/^(bin|obj)$/.test(entry.name)) offenders.push(rel);
      }
      if (entry.isDirectory() && depth < 6) await scan(full, depth + 1);
    }
  }
  for (const dir of [path.join(backend, 'Features'), path.join(backend, 'Infrastructure'), root]) {
    await scan(dir, 0);
  }
  if (offenders.length > 0) fail(`artefactos versionados o git anidado: ${offenders.join(', ')}`);
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
await checkNoNestedGitOrArtifacts();
await checkOpenspecSingleSource();

if (failures.length > 0) {
  console.error('check-structure: FAIL');
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log('check-structure: OK');
for (const note of notes) console.log(`- ${note}`);
