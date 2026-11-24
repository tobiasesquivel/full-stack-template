import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { copyFile, mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { randomInt } from 'node:crypto';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);
const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const script = path.join(scriptDir, 'check-secrets.mjs');
const repoConfig = path.join(scriptDir, '..', '..', '.gitleaks.toml');
const BASE32 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

function fakeAwsAccessKey() {
  let key = 'AKIA';
  for (let i = 0; i < 16; i += 1) key += BASE32[randomInt(BASE32.length)];
  return key;
}

async function makeRepo() {
  const dir = await mkdtemp(path.join(tmpdir(), 'dune-secrets-'));
  const git = (...args) => run('git', ['-c', 'user.name=dune', '-c', 'user.email=dune@example.test', '-c', 'commit.gpgsign=false', ...args], { cwd: dir });
  async function write(rel, content) {
    const full = path.join(dir, rel);
    await mkdir(path.dirname(full), { recursive: true });
    await writeFile(full, content);
  }
  async function commit(message) {
    await git('add', '--all');
    await git('commit', '--quiet', '--allow-empty', '-m', message);
  }
  await git('init', '--quiet');
  await copyFile(repoConfig, path.join(dir, '.gitleaks.toml'));
  await write('README.md', '# fixture\n');
  await commit('seed');
  return { dir, git, write, commit, cleanup: () => rm(dir, { recursive: true, force: true }) };
}

async function check(repo) {
  try {
    const { stdout, stderr } = await run(process.execPath, [script, '--root', repo.dir], { maxBuffer: 16 * 1024 * 1024 });
    return { code: 0, output: stdout + stderr };
  } catch (error) {
    return { code: error.code, output: (error.stdout ?? '') + (error.stderr ?? '') };
  }
}

describe('check-secrets', () => {
  it('passes on a clean history', async () => {
    const repo = await makeRepo();
    try {
      await repo.write('backend/appsettings.json', '{ "Logging": { "LogLevel": { "Default": "Information" } } }\n');
      await repo.commit('clean');
      const result = await check(repo);
      assert.equal(result.code, 0, result.output);
      assert.match(result.output, /check-secrets: OK/);
    } finally {
      await repo.cleanup();
    }
  });

  it('fails on a fake AWS key committed in a test fixture, and redacts it', async () => {
    const repo = await makeRepo();
    const key = fakeAwsAccessKey();
    try {
      await repo.write('backend/Tests/Fixtures/planted-secret.txt', `aws_access_key_id = ${key}\n`);
      await repo.commit('plant');
      const result = await check(repo);
      assert.equal(result.code, 1, result.output);
      assert.match(result.output, /aws-access-token/);
      assert.match(result.output, /historial alcanzable desde HEAD: FAIL/);
      assert.ok(!result.output.includes(key), 'la salida no muestra el secreto');
    } finally {
      await repo.cleanup();
    }
  });

  it('still fails after the plant is removed, because it stays in history', async () => {
    const repo = await makeRepo();
    try {
      await repo.write('backend/Tests/Fixtures/planted-secret.txt', `aws_access_key_id = ${fakeAwsAccessKey()}\n`);
      await repo.commit('plant');
      await repo.git('rm', '--quiet', 'backend/Tests/Fixtures/planted-secret.txt');
      await repo.commit('remove');
      const result = await check(repo);
      assert.equal(result.code, 1, result.output);
      assert.match(result.output, /historial alcanzable desde HEAD: FAIL/);
    } finally {
      await repo.cleanup();
    }
  });

  it('fails on an uncommitted fake key in a tracked file', async () => {
    const repo = await makeRepo();
    try {
      await repo.write('README.md', `# fixture\naws_access_key_id = ${fakeAwsAccessKey()}\n`);
      const result = await check(repo);
      assert.equal(result.code, 1, result.output);
      assert.match(result.output, /cambios sin preparar: FAIL/);
      assert.match(result.output, /historial alcanzable desde HEAD: OK/);
    } finally {
      await repo.cleanup();
    }
  });

  it('allows the Stripe documentation example listed in .gitleaks.toml', async () => {
    const repo = await makeRepo();
    try {
      await repo.write('docs/example.md', 'Stripe lo muestra sin comillas (`Idempotency-Key: KG5LxwFBepaKHyUD`).\n');
      await repo.commit('docs');
      const result = await check(repo);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('fails with an install hint when gitleaks is missing', async () => {
    const repo = await makeRepo();
    try {
      const { code, output } = await run(process.execPath, [script, '--root', repo.dir], {
        env: { ...process.env, GITLEAKS_BIN: path.join(repo.dir, 'no-gitleaks') },
      }).then(
        ({ stdout, stderr }) => ({ code: 0, output: stdout + stderr }),
        (error) => ({ code: error.code, output: (error.stdout ?? '') + (error.stderr ?? '') }),
      );
      assert.equal(code, 1, output);
      assert.match(output, /instalá gitleaks 8\.30\.1/);
    } finally {
      await repo.cleanup();
    }
  });
});
