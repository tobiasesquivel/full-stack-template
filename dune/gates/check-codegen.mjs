import { spawnSync } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { discoverHosts } from './hosts.mjs';

export function targetsFor(projects) {
  return projects.map((project) => ({
    project,
    generated: path.posix.join(path.posix.dirname(project), 'Internal/Generated'),
  }));
}

export function solutionProblems(projects, solutions) {
  if (solutions.length !== 1) return [`la raíz del repo tiene ${solutions.length} archivos .slnx; check-codegen necesita uno para saber qué compila el build de la solución`];
  const [{ name, text }] = solutions;
  return projects
    .filter((project) => !text.includes(`Path="${project}"`))
    .map((project) => `${project} no está en ${name}: el build de la solución no lo compila y codegen write corre con --no-build; agregalo con "dotnet sln ${name} add ${project}"`);
}

export function solutionsIn(root) {
  return readdirSync(root)
    .filter((file) => file.endsWith('.slnx'))
    .map((name) => ({ name, text: readFileSync(path.join(root, name), 'utf8') }));
}

export function verdict(target, porcelain) {
  const stale = porcelain
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter((line) => line.length > 2 && line[1] !== ' ');
  return stale.length === 0
    ? { ok: true, message: `${target.generated} coincide con lo que genera codegen write` }
    : {
        ok: false,
        message: `${target.generated} no coincide con lo que genera codegen write: corré "dotnet run --project ${target.project} -- codegen write" y versioná el resultado (${stale.join(', ')})`,
      };
}

export function coverage(target, tracked) {
  return tracked.split(/\r?\n/).some((line) => line.trim().length > 0)
    ? { ok: true, message: `${target.generated} tiene código generado versionado` }
    : { ok: false, message: `${target.generated} no tiene código generado versionado: la ruta de ${target.project} en check-codegen.mjs no es la carpeta donde escribe codegen write` };
}

function run(root, cmd, args, env) {
  const result = spawnSync(cmd, args, { cwd: root, env: { ...process.env, ...env }, encoding: 'utf8', shell: process.platform === 'win32' });
  if (result.status !== 0) {
    process.stderr.write(result.stdout ?? '');
    process.stderr.write(result.stderr ?? '');
    throw new Error(`${cmd} ${args.join(' ')} terminó con ${result.status}`);
  }
  return result.stdout;
}

function check(root, configuration, target) {
  const tracked = coverage(target, run(root, 'git', ['ls-files', '--', target.generated]));
  if (!tracked.ok) return tracked;
  run(root, 'dotnet', ['run', '--project', target.project, '--configuration', configuration, '--no-build', '--', 'codegen', 'write'], {
    ConnectionStrings__DefaultConnection: 'Host=localhost;Database=codegen;Username=codegen;Password=codegen',
    Messaging__RabbitMqUri: 'amqp://localhost:5672/',
  });
  const lines = (args, status) => run(root, 'git', args).split(/\r?\n/).filter(Boolean).map((file) => `${status} ${file}`);
  return verdict(target, [
    ...lines(['diff', '--name-only', '--', target.generated], ' M'),
    ...lines(['ls-files', '--others', '--exclude-standard', '--', target.generated], '??'),
  ].join('\n'));
}

function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
  const configuration = process.argv.includes('--release') ? 'Release' : 'Debug';
  const { projects, problems } = discoverHosts(root);
  const unbuilt = solutionProblems(projects, solutionsIn(root));
  const failures = [
    ...[...problems, ...unbuilt].map((message) => ({ ok: false, message })),
    ...(unbuilt.length > 0 ? [] : targetsFor(projects).map((target) => check(root, configuration, target)).filter((result) => !result.ok)),
  ];
  console.log(`check-codegen: ${failures.length === 0 ? 'PASS' : 'FAIL'} (${projects.join(', ')})`);
  for (const result of failures) console.log(`- ${result.message}`);
  if (failures.length > 0) process.exit(1);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
