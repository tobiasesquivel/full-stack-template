#!/usr/bin/env node
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..');
const USAGE = [
  'Usage: node rehearse-migrations.mjs --run-id <id> --refs <ref0,ref1,...> [--bulk-tasks <n>] [--expect-fail <stage,...>]',
  '  Publishes the API of every git ref and deploys them in order against one PostgreSQL with data.',
  '  Each new release starts while the previous one still runs, like a rolling deploy.',
  '  Per stage: readiness of both releases, cross-release writes and reads, and every task',
  '  read through both releases must match what the rehearsal expects. The previous release',
  '  then deletes an account that both releases wrote to: its account and profile must be gone',
  '  and no task, profile, key or session may point to a missing account. Audit rows stay on purpose.',
  '  --expect-fail names the stages (1 is the second ref) that must fail: a negative control passes',
  '  only when its stage fails.',
].join('\n');

function parseArgs(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i += 1) {
    const name = argv[i];
    if (name === '--help' || name === '-h') {
      console.log(USAGE);
      process.exit(0);
    }
    assert.ok(name.startsWith('--') && i + 1 < argv.length, `bad argument ${name}\n${USAGE}`);
    flags[name.slice(2)] = argv[(i += 1)];
  }
  assert.ok(flags['run-id'] && flags.refs, USAGE);
  const refs = flags.refs.split(',').map(ref => ref.trim()).filter(Boolean);
  const expectFail = (flags['expect-fail'] ?? '').split(',').filter(Boolean).map(Number);
  assert.ok(expectFail.every(stage => Number.isInteger(stage) && stage >= 1 && stage < refs.length), `--expect-fail needs stages between 1 and ${refs.length - 1}\n${USAGE}`);
  return {
    runId: flags['run-id'],
    refs,
    bulkTasks: Number(flags['bulk-tasks'] ?? 20000),
    expectFail,
  };
}

const options = parseArgs(process.argv.slice(2));
const evidenceDir = path.join(REPO_ROOT, 'artifacts', 'verify-template', options.runId);
mkdirSync(evidenceDir, { recursive: true });
const cacheDir = path.join(os.tmpdir(), 'template-rehearsal');
const postgresImage = /image:\s*(postgres:\S+)/.exec(readFileSync(path.join(REPO_ROOT, 'compose.yaml'), 'utf8'))[1];
const container = `rehearse-${options.runId}`.replace(/[^a-zA-Z0-9_.-]/g, '-');
const dbPassword = `Rehearse${crypto.randomUUID().replaceAll('-', '')}`;
const userPassword = `Rehearse!${crypto.randomUUID()}Aa9`;
const summary = { runId: options.runId, refs: options.refs, bulkTasks: options.bulkTasks, expectFail: options.expectFail, postgresImage, stages: [], result: 'FAIL' };
const running = new Map();

function log(event) {
  console.log(JSON.stringify(event));
}

function run(cmd, args, { cwd = REPO_ROOT, allowFailure = false, input } = {}) {
  const result = spawnSync(cmd, args, { cwd, encoding: 'utf8', windowsHide: true, maxBuffer: 64 * 1024 * 1024, input });
  if (!allowFailure && result.status !== 0) {
    throw new Error(`${cmd} ${args.join(' ')} failed with ${result.status}: ${result.stderr || result.stdout}`);
  }
  return result;
}

function psql(sql) {
  return run('docker', ['exec', '-i', container, 'psql', '-U', 'postgres', '-d', 'rehearsal', '-v', 'ON_ERROR_STOP=1', '-At'], { input: sql }).stdout.trim();
}

function migrationHistory() {
  const schemas = psql(`SELECT table_schema FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory' ORDER BY 1`).split('\n').filter(Boolean);
  return schemas.flatMap(schema => psql(`SELECT '${schema}.' || "MigrationId" FROM "${schema}"."__EFMigrationsHistory" ORDER BY 1`).split('\n').filter(Boolean));
}

function table(name) {
  const found = psql(`
    SELECT format('%I.%I', n.nspname, c.relname)
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind = 'r' AND c.relname = '${name}'`).split('\n').filter(Boolean);
  assert.equal(found.length, 1, `table ${name} lives in ${found.length} schemas: ${found.join(', ')}`);
  return found[0];
}

function tables() {
  return psql(`
    SELECT n.nspname || '.' || c.relname
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND c.relname <> '__EFMigrationsHistory'
    ORDER BY n.nspname || '.' || c.relname COLLATE "C"`).split('\n').filter(Boolean);
}

function orphans() {
  const missing = column => `NOT EXISTS (SELECT 1 FROM ${table('AspNetUsers')} u WHERE u."Id" = ${column})`;
  const [tasks, profiles, keys, sessions] = psql(`
    SELECT concat_ws(',',
      (SELECT count(*) FROM ${table('TaskItems')} t WHERE ${missing('t."OwnerId"')}),
      (SELECT count(*) FROM ${table('UserProfiles')} p WHERE ${missing('p."AuthId"')}),
      (SELECT count(*) FROM ${table('IdempotencyKeys')} k WHERE ${missing('k."OwnerId"')}),
      (SELECT count(*) FROM ${table('Sessions')} s WHERE ${missing('s."UserId"')}))`).split(',').map(Number);
  return { tasks, profiles, keys, sessions };
}

async function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

async function waitFor(label, check, timeoutMs = 120000) {
  const started = Date.now();
  let last;
  while (Date.now() - started < timeoutMs) {
    try {
      const value = await check();
      if (value) return value;
    } catch (error) {
      last = error;
    }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`${label} not reached in ${timeoutMs} ms${last ? `: ${last.message}` : ''}`);
}

function publish(ref) {
  const sha = run('git', ['rev-parse', '--verify', `${ref}^{commit}`]).stdout.trim();
  const output = path.join(cacheDir, sha, 'bin');
  const published = path.join(cacheDir, sha, 'published');
  if (existsSync(published)) return { ref, sha, output };
  const source = path.join(cacheDir, sha, 'src');
  run('git', ['worktree', 'remove', '--force', source], { allowFailure: true });
  rmSync(source, { recursive: true, force: true });
  rmSync(output, { recursive: true, force: true });
  run('git', ['worktree', 'add', '--force', '--detach', source, sha]);
  try {
    run('dotnet', ['publish', path.join(source, 'backend', 'TemplateApp.csproj'), '-c', 'Release', '-o', output, '--nologo']);
    writeFileSync(published, new Date().toISOString());
  } finally {
    run('git', ['worktree', 'remove', '--force', source], { allowFailure: true });
  }
  return { ref, sha, output };
}

function killTree(pid) {
  if (process.platform === 'win32') spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
  else {
    try {
      process.kill(pid, 'SIGKILL');
    } catch {
      return;
    }
  }
}

async function startApi(release, index, pgPort) {
  const port = await freePort();
  const name = `api-${index}-${release.ref.replace(/[^a-zA-Z0-9]/g, '_')}`;
  const stdout = createWriteStream(path.join(evidenceDir, `${name}.stdout.log`));
  const stderr = createWriteStream(path.join(evidenceDir, `${name}.stderr.log`));
  const child = spawn('dotnet', [path.join(release.output, 'TemplateApp.dll')], {
    cwd: release.output,
    windowsHide: true,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      ConnectionStrings__DefaultConnection: `Host=127.0.0.1;Port=${pgPort};Database=rehearsal;Username=postgres;Password=${dbPassword}`,
      OTEL_EXPORTER_OTLP_ENDPOINT: 'http://127.0.0.1:9',
      RateLimiting__UserReads: '100000',
      RateLimiting__UserWrites: '100000',
      DOTNET_NOLOGO: '1',
    },
  });
  child.stdout.pipe(stdout);
  child.stderr.pipe(stderr);
  const api = { name, release, port, child, base: new URL(`http://127.0.0.1:${port}/api/v1/`) };
  running.set(name, api);
  await waitFor(`${name} ready`, async () => {
    assert.equal(child.exitCode, null, `${name} exited with ${child.exitCode}`);
    return (await fetch(new URL('health/ready', api.base), { signal: AbortSignal.timeout(5000) })).status === 200;
  });
  return api;
}

function stopApi(api) {
  killTree(api.child.pid);
  running.delete(api.name);
}

class Client {
  cookies = new Map();

  constructor(api) {
    this.api = api;
  }

  async request(method, route, body, extraHeaders = {}) {
    const headers = { accept: 'application/json', ...extraHeaders };
    if (this.cookies.size) headers.cookie = [...this.cookies].map(([name, value]) => `${name}=${value}`).join('; ');
    if (method !== 'GET' && this.cookies.has('XSRF-TOKEN')) headers['X-XSRF-TOKEN'] = decodeURIComponent(this.cookies.get('XSRF-TOKEN'));
    if (body !== undefined) headers['content-type'] = 'application/json';
    const response = await fetch(new URL(route, this.api.base), {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(15000),
    });
    for (const raw of response.headers.getSetCookie()) {
      const [pair] = raw.split(';');
      const equals = pair.indexOf('=');
      const value = pair.slice(equals + 1);
      if (value) this.cookies.set(pair.slice(0, equals), value);
      else this.cookies.delete(pair.slice(0, equals));
    }
    const text = await response.text();
    return { status: response.status, payload: text ? JSON.parse(text) : undefined };
  }

  async expect(method, route, body, status, extraHeaders) {
    const response = await this.request(method, route, body, extraHeaders);
    assert.equal(response.status, status, `${this.api.name}: ${method} ${route} returned ${response.status}, expected ${status}`);
    return response.payload;
  }

  async signIn(userName, register) {
    await this.expect('GET', 'auth/csrf', undefined, 200);
    if (register) {
      await this.expect('POST', 'auth/register', { userName, email: `${userName}@example.test`, password: userPassword, displayName: userName }, 201);
    } else {
      await this.expect('POST', 'auth/login', { userName, password: userPassword }, 200);
    }
    await this.expect('GET', 'auth/csrf', undefined, 200);
  }

  async readAll() {
    const tasks = new Map();
    let cursor = null;
    do {
      const query = cursor ? `tasks?pageSize=100&cursor=${encodeURIComponent(cursor)}` : 'tasks?pageSize=100';
      const page = await this.expect('GET', query, undefined, 200);
      for (const task of page.items) tasks.set(task.id, task.isCompleted);
      cursor = page.nextCursor;
    } while (cursor);
    return tasks;
  }

  async setCompleted(id, completed) {
    const task = await this.expect('GET', `tasks/${id}`, undefined, 200);
    const updated = await this.expect('PUT', `tasks/${id}`, { title: task.title, isCompleted: completed, version: task.version }, 200);
    assert.equal(updated.isCompleted, completed, `${this.api.name}: PUT tasks/${id} answered isCompleted=${updated.isCompleted}`);
  }
}

async function readiness(api) {
  const response = await fetch(new URL('health/ready', api.base), { signal: AbortSignal.timeout(5000) });
  return { status: response.status, body: await response.json() };
}

function compare(label, actual, expected) {
  const wrong = [...expected].filter(([id, completed]) => actual.get(id) !== completed).length;
  const extra = [...actual.keys()].filter(id => !expected.has(id)).length;
  const completed = [...actual.values()].filter(Boolean).length;
  return { label, tasks: actual.size, completed, wrong, extra, ok: wrong === 0 && extra === 0 && actual.size === expected.size };
}

async function crossReleaseWrites(oldClient, newClient, expected, stage) {
  const writes = [];
  for (const [writer, reader] of [[oldClient, newClient], [newClient, oldClient]]) {
    const created = await writer.expect('POST', 'tasks', { title: `stage ${stage} by ${writer.api.name}` }, 201);
    expected.set(created.id, false);
    await writer.setCompleted(created.id, true);
    expected.set(created.id, true);
    const seen = await reader.expect('GET', `tasks/${created.id}`, undefined, 200);
    writes.push({ writer: writer.api.name, reader: reader.api.name, action: 'complete', readerSees: seen.isCompleted, ok: seen.isCompleted === true });
    await reader.setCompleted(created.id, false);
    expected.set(created.id, false);
    const back = await writer.expect('GET', `tasks/${created.id}`, undefined, 200);
    writes.push({ writer: reader.api.name, reader: writer.api.name, action: 'reopen', readerSees: back.isCompleted, ok: back.isCompleted === false });
  }
  return writes;
}

async function deleteAccountWithPreviousRelease(previous, next, stage) {
  const userName = `doomed${stage}x${Date.now()}`;
  const byPrevious = new Client(previous);
  await byPrevious.signIn(userName, true);
  const byNext = new Client(next);
  await byNext.signIn(userName, false);
  await byPrevious.expect('POST', 'tasks', { title: `doomed by ${previous.name}` }, 201, { 'Idempotency-Key': crypto.randomUUID() });
  await byNext.expect('POST', 'tasks', { title: `doomed by ${next.name}` }, 201, { 'Idempotency-Key': crypto.randomUUID() });
  const owned = Number(psql(`SELECT count(*) FROM ${table('TaskItems')} t JOIN ${table('AspNetUsers')} u ON u."Id" = t."OwnerId" WHERE u."UserName" = '${userName}'`));
  const accountId = psql(`SELECT "Id" FROM ${table('AspNetUsers')} WHERE "UserName" = '${userName}'`);
  const response = await byPrevious.request('DELETE', 'users/me');
  const [accounts, profiles] = psql(`
    SELECT concat_ws(',',
      (SELECT count(*) FROM ${table('AspNetUsers')} WHERE "Id" = '${accountId}'),
      (SELECT count(*) FROM ${table('UserProfiles')} WHERE "AuthId" = '${accountId}'))`).split(',').map(Number);
  const left = orphans();
  return {
    deletedBy: previous.name,
    status: response.status,
    tasksBefore: owned,
    remaining: { accounts, profiles },
    orphans: left,
    ok: response.status === 204 && owned === 2 && accountId !== '' && accounts === 0 && profiles === 0 && Object.values(left).every(count => count === 0),
  };
}

async function main() {
  log({ step: 'publish', refs: options.refs });
  const releases = options.refs.map(publish);
  summary.releases = releases.map(({ ref, sha }) => ({ ref, sha }));

  const pgPort = await freePort();
  run('docker', ['run', '-d', '--name', container, '-e', `POSTGRES_PASSWORD=${dbPassword}`, '-e', 'POSTGRES_DB=rehearsal', '-p', `127.0.0.1:${pgPort}:5432`, postgresImage]);
  await waitFor('postgres ready', () => run('docker', ['exec', container, 'pg_isready', '-U', 'postgres', '-d', 'rehearsal'], { allowFailure: true }).status === 0);
  await waitFor('postgres accepts queries', () => psql('SELECT 1') === '1');

  const userName = `rehearsal${Date.now()}`;
  let current = await startApi(releases[0], 0, pgPort);
  let currentClient = new Client(current);
  await currentClient.signIn(userName, true);
  for (let i = 0; i < 10; i += 1) {
    const created = await currentClient.expect('POST', 'tasks', { title: `seed ${i}` }, 201);
    if (i % 2 === 0) await currentClient.setCompleted(created.id, true);
  }
  const taskItems = table('TaskItems');
  const bulk = psql(`
    DO $$
    DECLARE columns text;
    BEGIN
      SELECT string_agg(quote_ident(attname), ', ' ORDER BY attnum) INTO columns
      FROM pg_attribute WHERE attrelid = '${taskItems}'::regclass AND attnum > 0 AND NOT attisdropped AND attname <> 'Id';
      EXECUTE format(
        'INSERT INTO ${taskItems} ("Id", %s) SELECT gen_random_uuid(), %s FROM ${taskItems}, generate_series(1, %s)',
        columns, columns, ${Math.max(1, Math.ceil(options.bulkTasks / 10))});
    END $$;
    SELECT count(*) FROM ${taskItems};`);
  const expected = await currentClient.readAll();
  summary.seed = { tasks: expected.size, completed: [...expected.values()].filter(Boolean).length, rowsInTable: Number(bulk.split('\n').pop()) };
  log({ step: 'seed', ...summary.seed });
  summary.stages.push({
    stage: 0,
    release: releases[0].ref,
    migrations: migrationHistory(),
    tables: tables(),
    readiness: { [current.name]: await readiness(current) },
    reads: [compare(current.name, expected, expected)],
    expected: 'pass',
    ok: true,
    passed: true,
  });

  for (let index = 1; index < releases.length; index += 1) {
    const before = new Set(migrationHistory());
    const started = Date.now();
    const next = await startApi(releases[index], index, pgPort);
    const startupMs = Date.now() - started;
    const applied = migrationHistory().filter(id => !before.has(id));
    const nextClient = new Client(next);
    await nextClient.signIn(userName, false);
    const stage = {
      stage: index,
      release: releases[index].ref,
      previous: current.release.ref,
      expected: options.expectFail.includes(index) ? 'fail' : 'pass',
      startupMs,
      applied,
      tables: tables(),
    };
    try {
      stage.readiness = { [current.name]: await readiness(current), [next.name]: await readiness(next) };
      stage.writes = await crossReleaseWrites(currentClient, nextClient, expected, index);
      stage.reads = [compare(current.name, await currentClient.readAll(), expected), compare(next.name, await nextClient.readAll(), expected)];
      stage.accountDeletion = await deleteAccountWithPreviousRelease(current, next, index);
    } catch (error) {
      stage.error = error.message;
    }
    stage.ok = !stage.error
      && Object.values(stage.readiness ?? {}).every(result => result.status === 200)
      && stage.writes.every(write => write.ok)
      && stage.reads.every(read => read.ok)
      && stage.accountDeletion.ok;
    stage.passed = stage.ok === (stage.expected === 'pass');
    summary.stages.push(stage);
    log({ step: 'stage', ...stage });
    stopApi(current);
    current = next;
    currentClient = nextClient;
  }

  summary.finalTables = tables();
  summary.finalSchema = psql(`
    SELECT attname FROM pg_attribute
    WHERE attrelid = '${table('TaskItems')}'::regclass AND attnum > 0 AND NOT attisdropped
    ORDER BY attnum`).split('\n');
  summary.result = summary.stages.every(stage => stage.passed) ? 'PASS' : 'FAIL';
}

try {
  await main();
} catch (error) {
  summary.error = error.stack ?? String(error);
  console.error(summary.error);
} finally {
  for (const api of [...running.values()]) stopApi(api);
  run('docker', ['rm', '-f', container], { allowFailure: true });
  writeFileSync(path.join(evidenceDir, 'rehearsal.json'), `${JSON.stringify(summary, null, 2)}\n`);
  log({ result: summary.result, evidence: path.relative(REPO_ROOT, path.join(evidenceDir, 'rehearsal.json')) });
  process.exitCode = summary.result === 'PASS' ? 0 : 1;
}
