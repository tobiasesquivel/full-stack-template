import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const RULE = 'límite de despliegue';

function ownsFeature(features) {
  return (relative) => {
    const match = /^backend\/Features\/([^/]+)\//.exec(relative);
    return match !== null && features(match[1]);
  };
}

export const HOSTS = [
  {
    project: 'backend/Hosts/Monolith/TemplateApp.csproj',
    scope: 'el monolito compila sus archivos y Features salvo Features/Tasks',
    compiles: ownsFeature((feature) => !/^tasks$/i.test(feature)),
  },
  {
    project: 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
    scope: 'el host de Tasks compila sus archivos, Features/Tasks y Features/Health',
    compiles: ownsFeature((feature) => feature === 'Tasks' || feature === 'Health'),
  },
];

export function violations(host, files) {
  const own = `${path.posix.dirname(host.project)}/`;
  return files
    .map((file) => file.replaceAll('\\', '/'))
    .filter((file) => {
      if (file.toLowerCase().endsWith('tests.cs')) return true;
      return !file.startsWith(own) && !host.compiles(file);
    })
    .map((file) => `${RULE}: ${host.project} compila ${file}; ${host.scope}, sin tests, y el resto llega por backend/Platform/TemplateApp.Platform.csproj`);
}

function compiledFiles(root, project) {
  const result = spawnSync('dotnet', ['msbuild', project, '-nologo', '-getItem:Compile', '-p:Configuration=Release'], {
    cwd: root,
    encoding: 'utf8',
    shell: process.platform === 'win32',
    maxBuffer: 64 * 1024 * 1024,
  });
  if (result.status !== 0) {
    process.stderr.write(result.stdout ?? '');
    process.stderr.write(result.stderr ?? '');
    throw new Error(`dotnet msbuild ${project} -getItem:Compile terminó con ${result.status}`);
  }
  const items = JSON.parse(result.stdout).Items?.Compile ?? [];
  return items.map((item) => path.relative(root, item.FullPath).split(path.sep).join('/'));
}

function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const failures = [];
  const counts = [];
  for (const host of HOSTS) {
    const files = compiledFiles(root, host.project);
    counts.push(`${host.project}: ${files.length} archivos`);
    if (files.length === 0) failures.push(`${RULE}: ${host.project} no compila ningún archivo; el gate no tiene qué revisar`);
    failures.push(...violations(host, files));
  }
  console.log(`check-host-items: ${failures.length === 0 ? 'PASS' : 'FAIL'} (${counts.join(', ')})`);
  for (const failure of failures) console.log(`- ${failure}`);
  if (failures.length > 0) process.exit(1);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
