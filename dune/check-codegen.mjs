import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

export const TARGETS = [
  { project: 'backend/TemplateApp.csproj', generated: 'backend/Internal/Generated' },
  { project: 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj', generated: 'backend/Hosts/Tasks/Internal/Generated' },
];

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
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const configuration = process.argv.includes('--release') ? 'Release' : 'Debug';
  const failures = TARGETS.map((target) => check(root, configuration, target)).filter((result) => !result.ok);
  console.log(`check-codegen: ${failures.length === 0 ? 'PASS' : 'FAIL'}`);
  for (const result of failures) console.log(`- ${result.message}`);
  if (failures.length > 0) process.exit(1);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
