import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

export const GENERATED = 'backend/Internal/Generated';

export function verdict(porcelain) {
  const stale = porcelain
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter((line) => line.length > 2 && line[1] !== ' ');
  return stale.length === 0
    ? { ok: true, message: `${GENERATED} coincide con lo que genera codegen write` }
    : {
        ok: false,
        message: `${GENERATED} no coincide con lo que genera codegen write: corré "dotnet run --project backend/TemplateApp.csproj -- codegen write" y versioná el resultado (${stale.join(', ')})`,
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

function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const configuration = process.argv.includes('--release') ? 'Release' : 'Debug';
  run(root, 'dotnet', ['run', '--project', 'backend/TemplateApp.csproj', '--configuration', configuration, '--no-build', '--', 'codegen', 'write'], {
    ConnectionStrings__DefaultConnection: 'Host=localhost;Database=codegen;Username=codegen;Password=codegen',
    Messaging__RabbitMqUri: 'amqp://localhost:5672/',
  });
  const result = verdict(run(root, 'git', ['status', '--porcelain', '--untracked-files=all', '--', GENERATED]));
  console.log(`check-codegen: ${result.ok ? 'PASS' : 'FAIL'}`);
  if (!result.ok) {
    console.log(`- ${result.message}`);
    process.exit(1);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
