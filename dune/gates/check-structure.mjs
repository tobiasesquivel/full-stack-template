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

const PLATFORM_PROJECT = 'backend/Platform/TemplateApp.Platform.csproj';

async function productSources() {
  const all = [];
  await collectFiles(path.join(backend, 'Features'), all);
  await collectFiles(path.join(backend, 'Shared'), all);
  await collectFiles(path.join(backend, 'Infrastructure'), all);
  await collectFiles(path.join(backend, 'Hosts'), all);
  if (await exists(path.join(root, PLATFORM_PROJECT))) all.push(path.join(root, PLATFORM_PROJECT));
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

const FEATURE_FOLDERS = new Set(['Contracts', 'Domain', 'Persistence', 'Access', 'MessageNames', 'Telemetry']);

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

async function checkTestsReferenceTheProduct() {
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

const CHOSEN_BUS_PACKAGES = [
  'WolverineFx.RabbitMQ',
  'WolverineFx.HealthChecks',
  'WolverineFx.EntityFrameworkCore',
  'WolverineFx.Postgresql',
  'WolverineFx.RuntimeCompilation',
  'WolverineFx.FluentValidation',
];
const CHOSEN_BUS_NAMESPACES = ['Wolverine', 'JasperFx'];
const BUS_HOME = 'backend/Infrastructure/Messaging/';
const ORCHESTRATOR = /:\s*(?:global::)?(?:Wolverine\.)?(?:Saga|TimeoutMessage)\b/;
const SLICE_ENDPOINT = /^backend\/Features\/[^/]+\/([^/]+)\/[^/]+Endpoint\.cs$/;
const MEDIATOR_NAMESPACE = 'Wolverine';

function isSliceEndpoint(rel) {
  const match = SLICE_ENDPOINT.exec(rel);
  return match !== null && !FEATURE_FOLDERS.has(match[1]);
}

function isBusLibrary(name) {
  return BUS_LIBRARIES.some((library) => name === library || name.startsWith(`${library}.`));
}

function isChosenBusNamespace(name) {
  return CHOSEN_BUS_NAMESPACES.some((namespace) => name === namespace || name.startsWith(`${namespace}.`));
}

function allowsBus(file, name, text) {
  const rel = toPosix(file);
  if (file.endsWith('.cs')) {
    const home = rel.startsWith(BUS_HOME) || (rel.startsWith('backend/Features/') && ORCHESTRATOR.test(text));
    return (home && isChosenBusNamespace(name)) || (isSliceEndpoint(rel) && name === MEDIATOR_NAMESPACE);
  }
  return rel === PLATFORM_PROJECT && CHOSEN_BUS_PACKAGES.includes(name);
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
      if (isBusLibrary(match[1]) && !allowsBus(file, match[1], text)) hits.push(`${toPosix(file)} (${match[1]})`);
    }
  }
  if (hits.length > 0) fail(`un solo bus, Wolverine, con sus paquetes solo en ${PLATFORM_PROJECT} y sus usings solo en ${BUS_HOME}, en el archivo de un orquestador de una feature (un tipo que hereda Saga o TimeoutMessage) o, solo using Wolverine, en el *Endpoint.cs de una slice (mediator): ${hits.join(', ')}`);
}

const RESILIENCE_PACKAGE = 'Microsoft.Extensions.Http.Resilience';
const TRANSITIVE_ONLY_PACKAGES = ['Polly', 'Microsoft.Extensions.Http.Polly', 'Microsoft.Extensions.Resilience'];

function isTransitiveOnly(name) {
  return TRANSITIVE_ONLY_PACKAGES.some((library) => name === library || name.startsWith(`${library}.`));
}

async function resilienceProjectFiles() {
  const all = [];
  await collectFiles(backend, all);
  await collectFiles(path.join(root, 'gateway'), all);
  const msbuild = all.filter((file) => /\.(csproj|props|targets)$/.test(file));
  return [...new Set([...msbuild, ...(await buildSources())])];
}

async function checkResiliencePackages() {
  const hits = [];
  const references = /<(?:PackageReference|PackageVersion|Using)\b[^>]*?\b(?:Include|Update)\s*=\s*["']([^"']+)["']/g;
  const projects = await resilienceProjectFiles();
  for (const file of projects) {
    const text = await readText(file);
    for (const match of text.matchAll(references)) {
      const name = match[1];
      if (isTransitiveOnly(name) || (name === RESILIENCE_PACKAGE && toPosix(file) !== PLATFORM_PROJECT)) {
        hits.push(`${toPosix(file)} (${name})`);
      }
    }
  }
  if (hits.length > 0) fail(`resiliencia HTTP: ${RESILIENCE_PACKAGE} solo en ${PLATFORM_PROJECT}, y Polly, Microsoft.Extensions.Resilience y Microsoft.Extensions.Http.Polly (deprecado) solo como dependencia transitiva: ${hits.join(', ')}`);
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
    if (rel.startsWith('backend/Features/Health/') || rel === 'backend/Infrastructure/Http/Remote/InternalApi.cs') continue;
    if (exemption.test(await readText(file))) hits.push(rel);
  }
  if (hits.length > 0) {
    fail(`limiter global: DisableRateLimiting solo en backend/Features/Health y backend/Infrastructure/Http/Remote/InternalApi.cs: ${hits.join(', ')}`);
  }
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

const GATEWAY_RULE = 'gateway como única entrada';
const PUBLISHED_SERVICES = new Set(['gateway', 'web', 'db', 'otel', 'prometheus']);

function composePublishers(text) {
  const publishes = new Map();
  let inServices = false;
  let service = null;
  let inPorts = false;
  for (const line of text.split(/\r?\n/)) {
    if (!line.trim() || line.trimStart().startsWith('#')) continue;
    if (/^\S/.test(line)) {
      inServices = /^services:\s*$/.test(line);
      service = null;
      continue;
    }
    if (!inServices) continue;
    const serviceMatch = /^ {2}(["']?)([A-Za-z0-9_.-]+)\1:\s*$/.exec(line);
    if (serviceMatch) {
      service = serviceMatch[2];
      publishes.set(service, false);
      inPorts = false;
      continue;
    }
    if (!service) continue;
    const key = /^ {4}(["']?)([A-Za-z0-9_-]+)\1:\s*(.*)$/.exec(line);
    if (key) {
      inPorts = key[2] === 'ports';
      const inline = key[3].trim();
      if (inPorts && inline && !/^\[\s*\]$/.test(inline)) publishes.set(service, true);
      continue;
    }
    if (inPorts && /^ {5,}-/.test(line)) publishes.set(service, true);
  }
  return publishes;
}

async function checkGatewayIsTheOnlyEntrance() {
  const composeFile = path.join(root, 'compose.yaml');
  if (!(await exists(composeFile))) {
    fail(`${GATEWAY_RULE}: falta compose.yaml`);
  } else {
    const publishers = composePublishers(await readText(composeFile));
    if (!publishers.get('gateway')) {
      fail(`${GATEWAY_RULE}: compose.yaml necesita el servicio gateway con el puerto publicado de la API`);
    }
    for (const [name, publishes] of publishers) {
      if (!PUBLISHED_SERVICES.has(name) && publishes) {
        fail(`${GATEWAY_RULE}: el servicio ${name} de compose.yaml publica puertos; un proceso de la API se alcanza solo por el gateway`);
      }
    }
  }
  const nginx = path.join(root, 'frontend', 'nginx.conf');
  if (!(await exists(nginx))) {
    fail(`${GATEWAY_RULE}: falta frontend/nginx.conf`);
  } else {
    const targets = [...(await readText(nginx)).matchAll(/proxy_pass\s+([^;\s]+)/g)].map((match) => match[1]);
    if (targets.length === 0 || targets.some((target) => !target.startsWith('http://gateway:'))) {
      fail(`${GATEWAY_RULE}: frontend/nginx.conf reenvía a ${targets.join(', ') || 'nada'}; tiene que reenviar solo a http://gateway:`);
    }
  }
  const project = path.join(root, 'gateway', 'TemplateApp.Gateway.csproj');
  if (!(await exists(project))) {
    fail(`${GATEWAY_RULE}: falta gateway/TemplateApp.Gateway.csproj`);
  } else if (/<ProjectReference\b/.test(await readText(project))) {
    fail(`${GATEWAY_RULE}: gateway/TemplateApp.Gateway.csproj referencia otro proyecto; el gateway reparte requests y no compila código de features`);
  }
}

await checkSlicesHaveTests();
await checkTestsReferenceTheProduct();
await checkInternalsOnlyForTests();
await checkNoBus();
await checkResiliencePackages();
await checkNoCrossRequestCache();
await checkRateLimitExemptionOnlyInHealth();
await checkEditorconfigSeverities();
await checkNoToggleAlias();
await checkTrackedArtifacts();
await checkNoNestedGit();
await checkGatewayIsTheOnlyEntrance();

if (failures.length > 0) {
  console.error('check-structure: FAIL');
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log('check-structure: OK');
for (const note of notes) console.log(`- ${note}`);
