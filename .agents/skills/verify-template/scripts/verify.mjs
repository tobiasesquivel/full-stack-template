#!/usr/bin/env node





import { spawn, spawnSync } from 'node:child_process';
import { promises as fs, mkdirSync, writeFileSync, appendFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import crypto from 'node:crypto';
import net from 'node:net';
import http from 'node:http';
import { StringDecoder } from 'node:string_decoder';

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(SCRIPT_DIR, '..', '..', '..', '..');
const SMOKE_SCRIPT = path.join(SCRIPT_DIR, 'smoke-api.mjs');
const BACKEND_CSPROJ = path.join(REPO_ROOT, 'backend', 'TemplateApp.csproj');
const TASKS_CSPROJ = path.join(REPO_ROOT, 'backend', 'Hosts', 'Tasks', 'TemplateApp.Tasks.csproj');
const GATEWAY_CSPROJ = path.join(REPO_ROOT, 'gateway', 'TemplateApp.Gateway.csproj');
const SOLUTION = path.join(REPO_ROOT, 'TemplateApp.slnx');
const TESTS_CSPROJ = path.join(REPO_ROOT, 'backend', 'Tests', 'TemplateApp.Tests.csproj');
const FRONTEND_DIR = path.join(REPO_ROOT, 'frontend');
const FRONTEND_PKG = path.join(FRONTEND_DIR, 'package.json');

const RUN_ID_RE = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/;
const DEFAULT_PG_IMAGE = 'postgres:18';
const DEFAULT_DB_NAME = 'template_verify';
const READY_SENTINEL = 'VERIFY_TEMPLATE_READY';
const LABEL_RUN = 'verify-template-run';
const LABEL_NONCE = 'verify-template-nonce';

let commandLogPath = null;
async function logCommand(record) {
  if (!commandLogPath) return;
  await fs.appendFile(commandLogPath, `${JSON.stringify(record)}\n`, 'utf8');
}

const secrets = [];
function addSecret(value) {
  if (value !== undefined && value !== null && String(value) !== '') secrets.push(String(value));
}
function redactHeaders(headers) {
  const out = {};
  for (const [key, value] of Object.entries(headers ?? {})) {
    const lower = key.toLowerCase();
    out[key] = (lower === 'cookie' || lower === 'set-cookie' || lower === 'authorization' || lower.includes('xsrf'))
      ? '***REDACTED***'
      : value;
  }
  return out;
}
function redact(input) {
  let out = String(input ?? '');
  for (const secret of secrets) {
    if (secret.length >= 4) out = out.split(secret).join('***REDACTED***');
  }
  return out
    .replace(/(password=)[^;\s"']+/gi, '$1***REDACTED***')
    .replace(/((?:POSTGRES_PASSWORD|Jwt__Key|JWT_KEY)[=:]\s*)[^\s"']+/gi, '$1***REDACTED***')
    .replace(/("accessToken"\s*:\s*")[^"]+(")/gi, '$1***REDACTED***$2')
    .replace(/((?:Cookie|Set-Cookie|X-XSRF-TOKEN)[^\r\n:]*:\s*)[^\r\n]+/gi, '$1***REDACTED***')
    .replace(/(Bearer\s+)[A-Za-z0-9\-._~+/=]+/g, '$1***REDACTED***');
}

function parseArgs(argv) {
  const result = { command: null, flags: {} };
  const rest = [...argv];
  if (rest.length > 0 && !rest[0].startsWith('-')) result.command = rest.shift() ?? null;
  for (let i = 0; i < rest.length; i += 1) {
    const token = rest[i];
    if (!token.startsWith('--')) throw new Error(`unexpected argument: ${token}`);
    const name = token.slice(2);
    if (name === '') throw new Error('unexpected argument: --');
    const eq = name.indexOf('=');
    if (eq > -1) {
      result.flags[name.slice(0, eq)] = name.slice(eq + 1);
    } else if (i + 1 < rest.length && !rest[i + 1].startsWith('--')) {
      result.flags[name] = rest[i + 1];
      i += 1;
    } else {
      result.flags[name] = true;
    }
  }
  return result;
}

function usage() {
  return [
    'verify.mjs — template verification owner/helper (Node builtins only).',
    '',
    'Usage (cwd = repo root):',
    '  node .agents/skills/verify-template/scripts/verify.mjs launch   --run-id <id> [options]',
    '  node .agents/skills/verify-template/scripts/verify.mjs doctor   --run-id <id>',
    '  node .agents/skills/verify-template/scripts/verify.mjs api      --run-id <id> [--timeout-smoke-ms N]',
    '  node .agents/skills/verify-template/scripts/verify.mjs cleanup --run-id <id> [--timeout-cleanup-ms N]',
    '',
    'launch options:',
    '  --evidence-root <dir>      relative to repo root (default artifacts/verify-template)',
    '  --api-port/--front-port/--pg-port <n>   0 = free port (default 0)',
    '  --pg-image <ref>           default postgres:18',
    '  --front-build              run `npm run build` before serve (only if package.json defines it)',
    '  --timeout-<build|test|npm|front-build|docker|pg|ready-api|ready-front|smoke|cleanup|prereq>-ms N',
    '  Same timeouts readable from VERIFY_TIMEOUT_<NAME>_MS env vars.',
    '',
    'Evidence per run: run.json (urls, owned container/volume ids, no secrets),',
    'summary.json (stage results), commands.jsonl (argv, cwd, stdout/stderr paths, exit, timestamps).',
    'launch ends READY (servers up; api-smoke + browser NOT_RUN) — READY is not a template PASS.',
    'api updates only the api-smoke scope (never a UI PASS). cleanup is idempotent and never',
    'deletes by bare name: without ownership proof it reports UNKNOWN/FAIL and deletes nothing.',
    'Zero executed tests is FAIL, never PASS. Repro-first: a template that does not come up',
    'fails with evidence; this helper never repairs the template for green.',
  ].join('\n');
}

function timeoutMs(flags, name, envName, def) {
  const raw = flags[name] ?? process.env[envName];
  if (raw === undefined || raw === null || raw === '' || raw === true) return def;
  const value = Number(raw);
  if (!Number.isFinite(value) || value <= 0) throw new Error(`invalid timeout ${name}: ${raw}`);
  return Math.floor(value);
}

function portFlag(flags, name, def) {
  const raw = flags[name];
  if (raw === undefined || raw === null || raw === '' || raw === true) return def;
  const value = Number(raw);
  if (!Number.isInteger(value) || value < 0 || value > 65535) throw new Error(`invalid port ${name}: ${raw}`);
  return value;
}

function validatedRunId(flags) {
  const runId = flags['run-id'];
  if (typeof runId !== 'string' || !RUN_ID_RE.test(runId)) {
    throw new Error('missing or invalid --run-id (expected [A-Za-z0-9_-], 1-64 chars, leading alnum)');
  }
  return runId;
}

function evidenceRootOf(flags) {
  const raw = typeof flags['evidence-root'] === 'string' && flags['evidence-root'] !== ''
    ? flags['evidence-root']
    : 'artifacts/verify-template';
  
  
  if (raw.includes('\0') || path.isAbsolute(raw) || /^[A-Za-z]:[\\/]/.test(raw)) {
    throw new Error(`invalid --evidence-root (must be a repo-relative path): ${raw}`);
  }
  const segments = raw.split(/[\\/]+/);
  if (segments.some((seg) => seg === '' || seg === '.' || seg === '..')) {
    throw new Error(`invalid --evidence-root (no empty/dot/dotdot segments): ${raw}`);
  }
  return raw;
}

async function pathExists(target) {
  try {
    await fs.access(target);
    return true;
  } catch {
    return false;
  }
}

async function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      const port = typeof address === 'object' && address !== null ? address.port : 0;
      server.close((err) => {
        if (err) reject(err);
        else resolve(port);
      });
    });
  });
}

function tcpConnectable(host, port, timeout = 3000) {
  return new Promise((resolve) => {
    const socket = new net.Socket();
    const done = (ok) => {
      socket.destroy();
      resolve(ok);
    };
    socket.setTimeout(timeout);
    socket.once('connect', () => done(true));
    socket.once('timeout', () => done(false));
    socket.once('error', () => done(false));
    socket.connect(port, host);
  });
}

async function waitFor({ timeoutMs, intervalMs, label, check }) {
  const deadline = Date.now() + timeoutMs;
  let lastError = null;
  for (;;) {
    try {
      const value = await check();
      if (value) return value;
    } catch (err) {
      lastError = err;
    }
    if (Date.now() >= deadline) {
      const suffix = lastError ? `: ${lastError.message}` : '';
      throw new Error(`timeout waiting for ${label} after ${timeoutMs}ms${suffix}`);
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
}

function httpGet(urlText, { timeoutMs = 10000, headers = {} } = {}) {
  return new Promise((resolve, reject) => {
    const req = http.get(urlText, { headers, timeout: timeoutMs }, (res) => {
      let body = '';
      res.on('data', (chunk) => {
        body += chunk.toString('utf8');
        if (body.length > 2_000_000) res.destroy(new Error('response body exceeded 2MB cap'));
      });
      res.on('end', () => {
        resolve({ status: res.statusCode ?? 0, headers: redactHeaders(res.headers), body });
      });
      res.on('error', reject);
    });
    req.on('timeout', () => req.destroy(new Error(`GET ${urlText} timed out after ${timeoutMs}ms`)));
    req.on('error', reject);
  });
}





function healthBodySays(body, expected) {
  try {
    const parsed = typeof body === 'string' ? JSON.parse(body) : body;
    const status = parsed?.status ?? parsed?.Status;
    return typeof status === 'string' && status.toLowerCase() === expected;
  } catch {
    return false;
  }
}
function trackedStartProblem(tracked) {
  
  
  if (tracked.spawnError) return tracked.spawnError;
  if (!tracked.child || tracked.child.pid === undefined || tracked.child.exitCode !== null) {
    return `process never started (pid ${tracked.child?.pid ?? 'none'}, exit ${tracked.child?.exitCode ?? 'none'})`;
  }
  return null;
}
async function failFastSpawn(tracked, label) {
  await new Promise((resolve) => setImmediate(resolve));
  const problem = trackedStartProblem(tracked);
  if (problem) throw new Error(`failed to spawn ${label}: ${problem}`);
}

function httpJson(method, urlText, { timeoutMs = 30000, headers = {}, body = null } = {}) {
  return new Promise((resolve, reject) => {
    const payload = body === null ? null : JSON.stringify(body);
    const req = http.request(
      urlText,
      {
        method,
        timeout: timeoutMs,
        headers: {
          'content-type': 'application/json',
          ...(payload === null ? {} : { 'content-length': String(Buffer.byteLength(payload)) }),
          ...headers,
        },
      },
      (res) => {
        let text = '';
        res.on('data', (chunk) => {
          text += chunk.toString('utf8');
        });
        res.on('end', () => {
          let parsed = null;
          try {
            parsed = text === '' ? null : JSON.parse(text);
          } catch {
            parsed = { raw: text };
          }
          resolve({ status: res.statusCode ?? 0, body: parsed });
        });
        res.on('error', reject);
      },
    );
    req.on('timeout', () => req.destroy(new Error(`${method} ${urlText} timed out after ${timeoutMs}ms`)));
    req.on('error', reject);
    if (payload !== null) req.write(payload);
    req.end();
  });
}

function childAlive(child) {
  return !!child && child.exitCode === null && child.signalCode === null;
}

function waitForChildExit(child, timeout) {
  if (!childAlive(child)) return Promise.resolve(true);
  return new Promise((resolve) => {
    const timer = setTimeout(() => resolve(false), timeout);
    child.once('exit', () => {
      clearTimeout(timer);
      resolve(true);
    });
  });
}

async function killTree(child, timeout = 10000) {
  if (!childAlive(child)) return { alreadyExited: true, pid: child?.pid ?? null };
  const pid = child.pid;
  try {
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore', timeout: 15000 });
    } else {
      child.kill('SIGTERM');
    }
  } catch {
    // Escalation below reports the outcome.
  }
  if (await waitForChildExit(child, timeout)) return { killed: true, pid, graceful: true };
  try {
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore', timeout: 15000 });
    } else {
      child.kill('SIGKILL');
    }
  } catch {
    // Reported below.
  }
  const exited = await waitForChildExit(child, 10000);
  return { killed: exited, pid, graceful: false };
}

function killTreeSyncFire(child) {
  if (!childAlive(child)) return;
  try {
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore', timeout: 15000 });
    } else {
      child.kill('SIGTERM');
    }
  } catch {
    // The close handler reports the real outcome.
  }
}

const MEM_CAP = 512 * 1024;
function capAppend(current, text, truncated) {
  if (current.length >= MEM_CAP) return { text: current, truncated: true };
  const room = MEM_CAP - current.length;
  return { text: current + text.slice(0, room), truncated: truncated || text.length > room };
}



function lineWriter(handle, onText) {
  const decoder = new StringDecoder('utf8');
  let pending = '';
  const emit = (text) => {
    const redacted = redact(text);
    handle.write(redacted).catch(() => {});
    if (onText) onText(redacted);
  };
  return {
    push(chunk) {
      pending += decoder.write(chunk);
      let nl;
      while ((nl = pending.indexOf('\n')) !== -1) {
        emit(pending.slice(0, nl + 1));
        pending = pending.slice(nl + 1);
      }
      if (pending.length > 1048576) {
        
        
        
        
        
        const redacted = redact(pending);
        const keep = 8192;
        emit(redacted.slice(0, Math.max(0, redacted.length - keep)));
        pending = redacted.slice(-keep);
      }
    },
    flush() {
      emit(decoder.end() + pending);
      pending = '';
    },
  };
}


async function runCommand({ cmd, args, cwd, env, timeoutMs, stdoutPath, stderrPath }) {
  const startedAt = new Date().toISOString();
  const started = Date.now();
  await fs.mkdir(path.dirname(stdoutPath), { recursive: true });
  const outHandle = await fs.open(stdoutPath, 'w');
  const errHandle = await fs.open(stderrPath, 'w');
  let stdoutMem = '';
  let stderrMem = '';
  let stdoutTruncated = false;
  let stderrTruncated = false;

  const child = spawn(cmd, args, {
    cwd,
    env: { ...process.env, ...env },
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  });
  const outWriter = lineWriter(outHandle, (text) => {
    const next = capAppend(stdoutMem, text, stdoutTruncated);
    stdoutMem = next.text;
    stdoutTruncated = next.truncated;
  });
  const errWriter = lineWriter(errHandle, (text) => {
    const next = capAppend(stderrMem, text, stderrTruncated);
    stderrMem = next.text;
    stderrTruncated = next.truncated;
  });
  child.stdout.on('data', (chunk) => outWriter.push(chunk));
  child.stderr.on('data', (chunk) => errWriter.push(chunk));

  let timedOut = false;
  let spawnError = null;
  const timer = setTimeout(() => {
    timedOut = true;
    killTreeSyncFire(child);
  }, timeoutMs);
  if (typeof timer.unref === 'function') timer.unref();
  const exitCode = await new Promise((resolve) => {
    child.once('error', (err) => {
      spawnError = err;
      resolve(null);
    });
    child.once('close', (code) => resolve(code));
  });
  clearTimeout(timer);
  outWriter.flush();
  errWriter.flush();
  await new Promise((resolve) => setImmediate(resolve));
  await outHandle.close().catch(() => {});
  await errHandle.close().catch(() => {});
  const record = {
    at: new Date().toISOString(),
    startedAt,
    endedAt: new Date().toISOString(),
    durationMs: Date.now() - started,
    cmd,
    argv: args.map((arg) => redact(arg)),
    command: redact([cmd, ...args].join(' ')),
    cwd,
    stdoutPath,
    stderrPath,
    exitCode: exitCode ?? 1,
    signal: child.signalCode ?? null,
    timedOut,
    spawnError: spawnError ? String(spawnError) : null,
  };
  await logCommand(record);
  return { ...record, stdout: stdoutMem, stdoutTruncated, stderr: stderrMem, stderrTruncated };
}


function runSync({ cmd, args, cwd, timeoutMs, stdoutPath, stderrPath }) {
  const startedAt = new Date().toISOString();
  const started = Date.now();
  let result;
  try {
    result = spawnSync(cmd, args, { cwd, timeout: timeoutMs, encoding: 'utf8', windowsHide: true });
  } catch (err) {
    result = { status: null, stdout: '', stderr: String(err?.message ?? err), error: err };
  }
  const timedOut = result?.error?.code === 'ETIMEDOUT';
  const stdout = redact(String(result?.stdout ?? ''));
  const stderr = redact(String(result?.stderr ?? ''));
  try {
    mkdirSync(path.dirname(stdoutPath), { recursive: true });
    writeFileSync(stdoutPath, stdout);
    writeFileSync(stderrPath, stderr);
  } catch {
    // Evidence write failure surfaces via the returned record.
  }
  const record = {
    at: new Date().toISOString(),
    startedAt,
    endedAt: new Date().toISOString(),
    durationMs: Date.now() - started,
    cmd,
    argv: args.map((arg) => redact(arg)),
    command: redact([cmd, ...args].join(' ')),
    cwd,
    stdoutPath,
    stderrPath,
    exitCode: result?.status ?? 1,
    signal: result?.signal ?? null,
    timedOut,
    spawnError: result?.error && !timedOut ? String(result.error?.message ?? result.error) : null,
  };
  if (commandLogPath) {
    try {
      appendFileSync(commandLogPath, `${JSON.stringify(record)}\n`, 'utf8');
    } catch {
      // Ledger append is best effort for sync paths.
    }
  }
  const cap = (text) => (text.length > MEM_CAP ? { text: text.slice(0, MEM_CAP), truncated: true } : { text, truncated: false });
  const out = cap(stdout);
  const err = cap(stderr);
  return { ...record, stdout: out.text, stdoutTruncated: out.truncated, stderr: err.text, stderrTruncated: err.truncated };
}






async function spawnTracked({ cmd, args, cwd, env, stdoutPath, stderrPath }) {
  let outHandle;
  let errHandle;
  try {
    await fs.mkdir(path.dirname(stdoutPath), { recursive: true });
    outHandle = await fs.open(stdoutPath, 'w');
    errHandle = await fs.open(stderrPath, 'w');
  } catch (err) {
    throw new Error(`cannot open tracked logs ${stdoutPath} / ${stderrPath}: ${err?.message ?? err}`);
  }
  let child;
  try {
    child = spawn(cmd, args, {
      cwd,
      env: { ...process.env, ...env },
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
  } catch (err) {
    await outHandle.close().catch(() => {});
    await errHandle.close().catch(() => {});
    throw new Error(`failed to spawn ${cmd}: ${err?.message ?? err}`);
  }
  const state = {
    child,
    pid: child.pid,
    command: redact([cmd, ...args].join(' ')),
    cwd,
    startedAt: new Date().toISOString(),
    exitCode: null,
    signal: null,
    spawnError: null,
  };
  const outWriter = lineWriter(outHandle);
  const errWriter = lineWriter(errHandle);
  child.stdout?.on('data', (chunk) => outWriter.push(chunk));
  child.stderr?.on('data', (chunk) => errWriter.push(chunk));
  child.once('error', (err) => {
    state.spawnError = String(err);
  });
  child.once('exit', (code, signal) => {
    state.exitCode = code;
    state.signal = signal ?? null;
    state.endedAt = new Date().toISOString();
  });
  child.once('close', async () => {
    outWriter.flush();
    errWriter.flush();
    await new Promise((resolve) => setImmediate(resolve));
    await outHandle.close().catch(() => {});
    await errHandle.close().catch(() => {});
  });
  return state;
}

const STAGE_NAMES = [
  'prereqs',
  'dotnet-build',
  'dotnet-test',
  'postgres-start',
  'api-start',
  'frontend-setup',
  'frontend-serve',
  'ready',
  'api-smoke',
  'browser',
];

function blankStages() {
  return STAGE_NAMES.map((name) => ({ name, status: 'NOT_RUN', durationMs: 0, detail: null }));
}

async function checkPrereqs(evidenceDir, timeouts) {
  const notes = [];
  const versions = { node: process.version, platform: `${process.platform}-${process.arch}` };

  for (const file of [SOLUTION, BACKEND_CSPROJ, TASKS_CSPROJ, GATEWAY_CSPROJ, TESTS_CSPROJ, FRONTEND_PKG, SMOKE_SCRIPT]) {
    if (!(await pathExists(file))) {
      throw Object.assign(new Error(`required file missing: ${path.relative(REPO_ROOT, file)}`), {
        blocked: true,
      });
    }
  }
  notes.push('required project files present (backend csproj, tests csproj, frontend package.json, smoke-api.mjs)');

  const sdks = await runCommand({
    cmd: 'dotnet',
    args: ['--list-sdks'],
    cwd: REPO_ROOT,
    timeoutMs: timeouts.prereq,
    stdoutPath: path.join(evidenceDir, 'prereqs-dotnet-sdks.stdout.log'),
    stderrPath: path.join(evidenceDir, 'prereqs-dotnet-sdks.stderr.log'),
  });
  if (sdks.exitCode !== 0 || sdks.timedOut || sdks.spawnError) {
    throw Object.assign(new Error(`dotnet --list-sdks failed: ${sdks.spawnError ?? `exit ${sdks.exitCode}`}`), {
      blocked: true,
      detail: sdks,
    });
  }
  versions.dotnetSdks = sdks.stdout.trim().split(/\r?\n/).filter(Boolean);
  if (!versions.dotnetSdks.some((line) => /^10\./.test(line.trim()))) {
    throw Object.assign(
      new Error(`no .NET 10 SDK listed by dotnet --list-sdks: ${versions.dotnetSdks.join(', ') || '(none)'}`),
      { blocked: true, detail: sdks },
    );
  }
  notes.push(`dotnet SDK 10 present (${versions.dotnetSdks.join(', ')})`);

  const dockerVersion = await runCommand({
    cmd: 'docker',
    args: ['version', '--format', '{{json .}}'],
    cwd: REPO_ROOT,
    timeoutMs: timeouts.prereq,
    stdoutPath: path.join(evidenceDir, 'prereqs-docker-version.stdout.log'),
    stderrPath: path.join(evidenceDir, 'prereqs-docker-version.stderr.log'),
  });
  if (dockerVersion.exitCode !== 0 || dockerVersion.timedOut || dockerVersion.spawnError) {
    throw Object.assign(new Error('docker is not usable (docker version failed)'), {
      blocked: true,
      detail: dockerVersion,
    });
  }
  try {
    versions.docker = JSON.parse(dockerVersion.stdout).Server?.Version ?? dockerVersion.stdout.trim().slice(0, 200);
  } catch {
    versions.docker = dockerVersion.stdout.trim().slice(0, 200);
  }
  notes.push(`docker reachable (server ${versions.docker})`);

  return { notes, versions };
}

function parseDotnetSummary(stdout) {
  const text = String(stdout);
  let match = text.match(/Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)/i);
  if (match) {
    return { failed: Number(match[1]), passed: Number(match[2]), skipped: Number(match[3]), total: Number(match[4]), source: 'console' };
  }
  match = text.match(/Passed:\s*(\d+),\s*Failed:\s*(\d+)/i);
  if (match) return { passed: Number(match[1]), failed: Number(match[2]), skipped: 0, total: Number(match[1]) + Number(match[2]), source: 'console' };
  const total = text.match(/^\s*total:\s*(\d+)\s*$/im);
  const failed = text.match(/^\s*(?:failed|error):\s*(\d+)\s*$/im);
  const passed = text.match(/^\s*(?:passed|correcto):\s*(\d+)\s*$/im);
  const skipped = text.match(/^\s*(?:skipped|omitido):\s*(\d+)\s*$/im);
  if (total && failed && passed && skipped) {
    return { total: Number(total[1]), failed: Number(failed[1]), passed: Number(passed[1]), skipped: Number(skipped[1]), source: 'console' };
  }
  return null;
}


function firstInspectEntry(text) {
  const parsed = JSON.parse(String(text));
  return Array.isArray(parsed) ? parsed[0] : parsed;
}



async function verifyOwnership(runRecord, timeout, evidenceDir, tag) {
  const verdict = {
    container: { state: 'unknown' },
    volume: { state: 'unknown' },
    ownedContainer: false,
    ownedVolume: false,
  };
  const containerInspect = runSync({
    cmd: 'docker',
    args: ['inspect', runRecord.containerName],
    cwd: REPO_ROOT,
    timeoutMs: timeout,
    stdoutPath: path.join(evidenceDir, `${tag}-inspect-container.stdout.log`),
    stderrPath: path.join(evidenceDir, `${tag}-inspect-container.stderr.log`),
  });
  if (containerInspect.exitCode === 0) {
    try {
      const entry = firstInspectEntry(containerInspect.stdout);
      const labels = entry?.Config?.Labels ?? entry?.Labels ?? {};
      const storedId = runRecord.containerId ?? '';
      const liveId = entry?.Id ?? '';
      const idMatch = !!storedId && !!liveId && (liveId === storedId || liveId.startsWith(storedId) || storedId.startsWith(liveId));
      const labelMatch = labels[LABEL_RUN] === runRecord.runId
        && !!runRecord.ownership?.nonce
        && labels[LABEL_NONCE] === runRecord.ownership.nonce;
      verdict.container = { state: 'present', idMatch, labelMatch };
      verdict.ownedContainer = idMatch && labelMatch;
    } catch (err) {
      verdict.container = { state: 'unparseable', error: String(err?.message ?? err) };
    }
  } else if (/no such object/i.test(`${containerInspect.stdout} ${containerInspect.stderr}`)) {
    verdict.container = { state: 'absent' };
  } else {
    verdict.container = { state: 'error', exitCode: containerInspect.exitCode };
  }
  const volumeInspect = runSync({
    cmd: 'docker',
    args: ['volume', 'inspect', runRecord.volumeName],
    cwd: REPO_ROOT,
    timeoutMs: timeout,
    stdoutPath: path.join(evidenceDir, `${tag}-inspect-volume.stdout.log`),
    stderrPath: path.join(evidenceDir, `${tag}-inspect-volume.stderr.log`),
  });
  if (volumeInspect.exitCode === 0) {
    try {
      const entry = firstInspectEntry(volumeInspect.stdout);
      const labels = entry?.Labels ?? {};
      const labelMatch = labels[LABEL_RUN] === runRecord.runId
        && !!runRecord.ownership?.nonce
        && labels[LABEL_NONCE] === runRecord.ownership.nonce;
      verdict.volume = { state: 'present', labelMatch };
      verdict.ownedVolume = labelMatch;
    } catch (err) {
      verdict.volume = { state: 'unparseable', error: String(err?.message ?? err) };
    }
  } else if (/no such volume/i.test(`${volumeInspect.stdout} ${volumeInspect.stderr}`)) {
    verdict.volume = { state: 'absent' };
  } else {
    verdict.volume = { state: 'error', exitCode: volumeInspect.exitCode };
  }
  return verdict;
}




async function teardownOwned({
  children = null,
  containerName = null,
  volumeName = null,
  dockerResources = false,
  verify = false,
  runRecord = null,
  evidenceDir = null,
  runId = '?',
  reason = 'cleanup',
  timeout = 120000,
  tag = 'cleanup',
}) {
  const actions = [];
  let ok = true;
  let unknownChildren = false;

  if (children && children.size > 0) {
    for (const [key, tracked] of children) {
      try {
        const result = await killTree(tracked.child, 10000);
        actions.push({ what: `child:${key}`, pid: result.pid ?? tracked.pid ?? null, ...result });
        if (result.killed === false && !result.alreadyExited) ok = false;
      } catch (err) {
        ok = false;
        actions.push({ what: `child:${key}`, error: String(err?.message ?? err) });
      }
    }
    children.clear();
  } else {
    unknownChildren = true;
    actions.push({ what: 'children', note: 'no live handles in this process; child state unknown, nothing signaled', unknown: true });
  }

  if ((dockerResources || verify) && containerName && volumeName) {
    let verdict = null;
    if (verify && runRecord) {
      verdict = await verifyOwnership(runRecord, timeout, evidenceDir ?? REPO_ROOT, tag);
      actions.push({ what: 'ownership-verify', ...verdict });
    }
    const remove = async (kind, args, goneRe) => {
      if (verify && verdict) {
        const owned = kind === 'container' ? verdict.ownedContainer : verdict.ownedVolume;
        const state = kind === 'container' ? verdict.container.state : verdict.volume.state;
        if (state === 'absent') {
          actions.push({ what: `${kind}:absent`, note: 'already gone; nothing removed' });
          return;
        }
        if (!owned) {
          ok = false;
          actions.push({ what: `${kind}:skip`, note: 'ownership unproven (id or labels mismatch); NOT removed by name' });
          return;
        }
      }
      const res = runSync({
        cmd: 'docker',
        args,
        cwd: REPO_ROOT,
        timeoutMs: timeout,
        stdoutPath: path.join(evidenceDir ?? REPO_ROOT, `${tag}-${kind}-rm.stdout.log`),
        stderrPath: path.join(evidenceDir ?? REPO_ROOT, `${tag}-${kind}-rm.stderr.log`),
      });
      const gone = res.exitCode === 0 || goneRe.test(`${res.stdout} ${res.stderr}`);
      if (!gone) ok = false;
      actions.push({ what: `${kind}:rm`, exitCode: gone ? 0 : res.exitCode, note: redact(`${res.stdout} ${res.stderr}`.trim().slice(0, 2000)) });
    };
    await remove('container', ['rm', '-f', containerName], /no such container/i);
    await remove('volume', ['volume', 'rm', volumeName], /no such volume/i);
  }

  if (unknownChildren) ok = false;
  const status = ok ? 'PASS' : (unknownChildren || (verify && verdictUnknown(verdictOf(actions))) ? 'UNKNOWN' : 'FAIL');
  const payload = { runId, at: new Date().toISOString(), reason, ok, status, actions, evidenceRetained: true };
  if (evidenceDir) {
    await fs.writeFile(path.join(evidenceDir, 'cleanup.json'), `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
  }
  return { ok, status, actions };
}

function verdictOf(actions) {
  return actions.find((action) => action.what === 'ownership-verify') ?? null;
}
function verdictUnknown(verdict) {
  if (!verdict) return false;
  return !verdict.ownedContainer || !verdict.ownedVolume;
}

async function launch(flags) {
  const runId = validatedRunId(flags);
  const evidenceDir = path.join(REPO_ROOT, evidenceRootOf(flags), runId);
  const startedAt = new Date().toISOString();
  const timeouts = {
    prereq: timeoutMs(flags, 'timeout-prereq-ms', 'VERIFY_TIMEOUT_PREREQ_MS', 60000),
    build: timeoutMs(flags, 'timeout-build-ms', 'VERIFY_TIMEOUT_BUILD_MS', 600000),
    test: timeoutMs(flags, 'timeout-test-ms', 'VERIFY_TIMEOUT_TEST_MS', 900000),
    npm: timeoutMs(flags, 'timeout-npm-ms', 'VERIFY_TIMEOUT_NPM_MS', 600000),
    frontBuild: timeoutMs(flags, 'timeout-front-build-ms', 'VERIFY_TIMEOUT_FRONT_BUILD_MS', 600000),
    docker: timeoutMs(flags, 'timeout-docker-ms', 'VERIFY_TIMEOUT_DOCKER_MS', 300000),
    pg: timeoutMs(flags, 'timeout-pg-ms', 'VERIFY_TIMEOUT_PG_MS', 180000),
    apiReady: timeoutMs(flags, 'timeout-ready-api-ms', 'VERIFY_TIMEOUT_READY_API_MS', 180000),
    frontReady: timeoutMs(flags, 'timeout-ready-front-ms', 'VERIFY_TIMEOUT_READY_FRONT_MS', 300000),
    smoke: timeoutMs(flags, 'timeout-smoke-ms', 'VERIFY_TIMEOUT_SMOKE_MS', 300000),
    cleanup: timeoutMs(flags, 'timeout-cleanup-ms', 'VERIFY_TIMEOUT_CLEANUP_MS', 120000),
  };
  const wantApiPort = portFlag(flags, 'api-port', 0);
  const wantFrontPort = portFlag(flags, 'front-port', 0);
  const wantPgPort = portFlag(flags, 'pg-port', 0);
  const pgImage = typeof flags['pg-image'] === 'string' && flags['pg-image'] !== '' ? flags['pg-image'] : DEFAULT_PG_IMAGE;
  const wantFrontBuild = flags['front-build'] === true || flags['front-build'] === 'true';

  
  
  if (await pathExists(evidenceDir)) {
    const entries = await fs.readdir(evidenceDir).catch(() => ['?']);
    if (entries.length > 0) {
      console.error(
        `refusing run-id ${runId}: ${evidenceDir} already holds ${entries.length} entr${entries.length === 1 ? 'y' : 'ies'} `
        + `(${entries.slice(0, 5).join(', ')}${entries.length > 5 ? ', …' : ''}); `
        + 'previous evidence is untouched — pick a fresh --run-id',
      );
      process.exit(2);
    }
  } else {
    await fs.mkdir(evidenceDir, { recursive: true });
  }
  commandLogPath = path.join(evidenceDir, 'commands.jsonl');

  const stages = blankStages();
  const byName = Object.fromEntries(stages.map((stage) => [stage.name, stage]));
  const mark = (name, status, detail, durationMs) => {
    byName[name].status = status;
    if (detail !== undefined) byName[name].detail = detail;
    if (durationMs !== undefined) byName[name].durationMs = durationMs;
  };
  const notRun = { apiSmoke: 'NOT_RUN', browser: 'NOT_RUN' };

  const state = {
    runId,
    children: new Map(),
    containerName: `verify-pg-${runId}`.slice(0, 120),
    volumeName: `verify-pgdata-${runId}`.slice(0, 120),
    containerCreated: false,
    containerId: null,
    apiBase: null,
    frontUrl: null,
  };

  const writeJson = async (name, value) => {
    await fs.writeFile(path.join(evidenceDir, name), `${JSON.stringify(value, null, 2)}\n`, 'utf8');
  };

  const failStage = async (name, message, extra = {}) => {
    byName[name].status = extra.blocked ? 'BLOCKED' : 'FAIL';
    byName[name].detail = { message: redact(message), ...(extra.detail ? { command: extra.detail } : {}) };
    await writeJson('summary.json', {
      runId,
      status: extra.blocked ? 'BLOCKED' : 'FAIL',
      startedAt,
      endedAt: new Date().toISOString(),
      repoRoot: REPO_ROOT,
      evidenceDir,
      urls: { apiBase: state.apiBase, front: state.frontUrl },
      failure: { stage: name, message: redact(message) },
      verification: { ...notRun },
      stages,
    });
  };

  const launchCleanup = (reason) => teardownOwned({
    children: state.children,
    containerName: state.containerName,
    volumeName: state.volumeName,
    dockerResources: state.containerCreated,
    evidenceDir,
    runId,
    reason,
    timeout: timeouts.cleanup,
    tag: 'cleanup',
  });

  process.once('SIGINT', () => {
    launchCleanup('SIGINT during launch').then(() => process.exit(130)).catch(() => process.exit(130));
  });
  process.once('SIGTERM', () => {
    launchCleanup('SIGTERM during launch').then(() => process.exit(143)).catch(() => process.exit(143));
  });

  try {
    let t0 = Date.now();
    let prereqInfo;
    try {
      prereqInfo = await checkPrereqs(evidenceDir, timeouts);
    } catch (err) {
      mark('prereqs', 'FAIL', { message: err.message, command: err.detail ?? null }, Date.now() - t0);
      await failStage('prereqs', err.message, { blocked: !!err.blocked, detail: err.detail ?? null });
      await launchCleanup('prereqs failed');
      process.exit(1);
    }
    mark('prereqs', 'PASS', prereqInfo, Date.now() - t0);

    t0 = Date.now();
    const build = await runCommand({
      cmd: 'dotnet',
      args: ['build', SOLUTION, '-c', 'Release'],
      cwd: REPO_ROOT,
      timeoutMs: timeouts.build,
      stdoutPath: path.join(evidenceDir, 'dotnet-build.stdout.log'),
      stderrPath: path.join(evidenceDir, 'dotnet-build.stderr.log'),
    });
    if (build.exitCode !== 0 || build.timedOut || build.spawnError) {
      mark('dotnet-build', 'FAIL', build, Date.now() - t0);
      await failStage(
        'dotnet-build',
        build.timedOut
          ? `dotnet build timed out after ${timeouts.build}ms`
          : `dotnet build failed with exit ${build.exitCode}${build.spawnError ? ` (${build.spawnError})` : ''}`,
        { detail: build },
      );
      await launchCleanup('dotnet build failed');
      process.exit(1);
    }
    mark('dotnet-build', 'PASS', build, Date.now() - t0);

    t0 = Date.now();
    const test = await runCommand({
      cmd: 'dotnet',
      args: [
        'test', TESTS_CSPROJ, '-c', 'Release',
      ],
      cwd: REPO_ROOT,
      env: { TEST_POSTGRES_CONNECTION: '' },
      timeoutMs: timeouts.test,
      stdoutPath: path.join(evidenceDir, 'dotnet-test.stdout.log'),
      stderrPath: path.join(evidenceDir, 'dotnet-test.stderr.log'),
    });
    const counts = parseDotnetSummary(test.stdout);
    if (test.exitCode !== 0 || test.timedOut || test.spawnError) {
      mark('dotnet-test', 'FAIL', { ...test, counts }, Date.now() - t0);
      await failStage(
        'dotnet-test',
        test.timedOut
          ? `dotnet test timed out after ${timeouts.test}ms`
          : `dotnet test failed with exit ${test.exitCode}${test.spawnError ? ` (${test.spawnError})` : ''}`,
        { detail: { ...test, counts } },
      );
      await launchCleanup('dotnet test failed');
      process.exit(1);
    }
    
    
    
    const executed = (counts?.passed ?? NaN) + (counts?.failed ?? NaN);
    if (!counts || !Number.isFinite(executed) || executed <= 0 || (counts.failed ?? 0) !== 0) {
      mark('dotnet-test', 'FAIL', { ...test, counts: counts ?? null, executed: Number.isFinite(executed) ? executed : 0 }, Date.now() - t0);
      await failStage(
        'dotnet-test',
        `dotnet test executed zero tests (passed=${counts?.passed ?? '?'}, failed=${counts?.failed ?? '?'}, skipped=${counts?.skipped ?? '?'}, total=${counts?.total ?? '?'}${counts?.source ? ` via ${counts.source}` : ''}); skipped-only is not PASS`,
        { detail: { ...test, counts: counts ?? null } },
      );
      await launchCleanup('dotnet test ran zero tests');
      process.exit(1);
    }
    mark('dotnet-test', 'PASS', { ...test, counts, executed }, Date.now() - t0);

    t0 = Date.now();
    const pgPort = wantPgPort === 0 ? await freePort() : wantPgPort;
    const pgPassword = crypto.randomBytes(24).toString('base64url');
    addSecret(pgPassword);
    const ownershipNonce = crypto.randomBytes(16).toString('hex');
    const labelRun = `${LABEL_RUN}=${runId}`;
    const labelNonce = `${LABEL_NONCE}=${ownershipNonce}`;
    const pgDetail = { image: pgImage, containerName: state.containerName, volumeName: state.volumeName, pgPort };
    try {
      const volCreate = await runCommand({
        cmd: 'docker',
        args: ['volume', 'create', '--label', labelRun, '--label', labelNonce, state.volumeName],
        cwd: REPO_ROOT,
        timeoutMs: timeouts.docker,
        stdoutPath: path.join(evidenceDir, 'docker-volume-create.stdout.log'),
        stderrPath: path.join(evidenceDir, 'docker-volume-create.stderr.log'),
      });
      if (volCreate.exitCode !== 0 || volCreate.timedOut || volCreate.spawnError) {
        throw new Error(`docker volume create failed (exit ${volCreate.exitCode})`);
      }
      const dockerRun = await runCommand({
        cmd: 'docker',
        args: [
          'run',
          '-d',
          '--name',
          state.containerName,
          '--label',
          labelRun,
          '--label',
          labelNonce,
          '-e',
          `POSTGRES_PASSWORD=${pgPassword}`,
          '-e',
          `POSTGRES_DB=${DEFAULT_DB_NAME}`,
          '-v',
          `${state.volumeName}:/var/lib/postgresql`,
          '-p',
          `127.0.0.1:${pgPort}:5432`,
          pgImage,
        ],
        cwd: REPO_ROOT,
        timeoutMs: timeouts.docker,
        stdoutPath: path.join(evidenceDir, 'docker-run.stdout.log'),
        stderrPath: path.join(evidenceDir, 'docker-run.stderr.log'),
      });
      if (dockerRun.exitCode !== 0 || dockerRun.timedOut || dockerRun.spawnError) {
        throw new Error(`docker run ${pgImage} failed (exit ${dockerRun.exitCode})`);
      }
      state.containerCreated = true;
      state.containerId = dockerRun.stdout.trim().split(/\s+/)[0] || null;
      
      
      
      
      
      await waitFor({
        timeoutMs: timeouts.pg,
        intervalMs: 1000,
        label: `postgres port bind on 127.0.0.1:${pgPort}`,
        check: () => tcpConnectable('127.0.0.1', pgPort, 2000),
      });
      
      
      
      
      
      const pgReadyDeadline = Date.now() + timeouts.pg;
      let pgAttempts = 0;
      for (;;) {
        pgAttempts += 1;
        const ps = runSync({
          cmd: 'docker',
          args: ['inspect', '-f', '{{.State.Status}}|{{.State.ExitCode}}|{{.State.Error}}', state.containerName],
          cwd: REPO_ROOT,
          timeoutMs: 15000,
          stdoutPath: path.join(evidenceDir, 'docker-pg-inspect.stdout.log'),
          stderrPath: path.join(evidenceDir, 'docker-pg-inspect.stderr.log'),
        });
        const psOut = `${ps.stdout ?? ''}${ps.stderr ?? ''}`.trim();
        const psStatus = (psOut.split('|')[0] ?? '').trim();
        if (ps.exitCode !== 0 || psStatus === 'exited' || psStatus === 'dead' || psStatus === 'removing' || psStatus === '') {
          const logs = runSync({
            cmd: 'docker',
            args: ['logs', '--tail', '200', state.containerName],
            cwd: REPO_ROOT,
            timeoutMs: 30000,
            stdoutPath: path.join(evidenceDir, 'docker-pg-logs.stdout.log'),
            stderrPath: path.join(evidenceDir, 'docker-pg-logs.stderr.log'),
          });
          pgDetail.containerState = psOut.slice(0, 500) || `(inspect exit ${ps.exitCode ?? '?'})`;
          pgDetail.containerLogsTail = redact(`${logs.stdout} ${logs.stderr}`.trim().slice(-2000));
          pgDetail.pgAttempts = pgAttempts;
          throw new Error(`postgres container ${psStatus || 'uninspectable'} before readiness (not a template NOT_READY: container failure); exit/logs in docker-pg-logs.*.log`);
        }
        const isReady = runSync({
          cmd: 'docker',
          args: ['exec', state.containerName, 'pg_isready', '-U', 'postgres'],
          cwd: REPO_ROOT,
          timeoutMs: 30000,
          stdoutPath: path.join(evidenceDir, 'docker-pg-isready.stdout.log'),
          stderrPath: path.join(evidenceDir, 'docker-pg-isready.stderr.log'),
        });
        pgDetail.pgAttempts = pgAttempts;
        pgDetail.pgIsReady = redact(`${isReady.stdout} ${isReady.stderr}`.trim().slice(0, 1000));
        pgDetail.pgIsReadyExit = isReady.exitCode;
        if (isReady.exitCode === 0 && !isReady.timedOut && !isReady.spawnError) break;
        if (Date.now() >= pgReadyDeadline) {
          const logs = runSync({
            cmd: 'docker',
            args: ['logs', '--tail', '200', state.containerName],
            cwd: REPO_ROOT,
            timeoutMs: 30000,
            stdoutPath: path.join(evidenceDir, 'docker-pg-logs.stdout.log'),
            stderrPath: path.join(evidenceDir, 'docker-pg-logs.stderr.log'),
          });
          pgDetail.containerLogsTail = redact(`${logs.stdout} ${logs.stderr}`.trim().slice(-2000));
          throw new Error(`pg_isready never reached exit 0 after ${timeouts.pg}ms (${pgAttempts} probes, last exit ${isReady.exitCode}); container stayed up, logs in docker-pg-logs.*.log`);
        }
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
    } catch (err) {
      mark('postgres-start', 'FAIL', { ...pgDetail, message: err.message }, Date.now() - t0);
      await failStage('postgres-start', err.message, { detail: { ...pgDetail } });
      await launchCleanup('postgres start failed');
      process.exit(1);
    }
    mark('postgres-start', 'PASS', pgDetail, Date.now() - t0);

    t0 = Date.now();
    const apiPort = wantApiPort === 0 ? await freePort() : wantApiPort;
    const monolithPort = await freePort();
    const tasksPort = await freePort();
    const apiBase = `http://127.0.0.1:${apiPort}/api/v1`;
    state.apiBase = apiBase;
    const connectionString = `Host=127.0.0.1;Port=${pgPort};Database=${DEFAULT_DB_NAME};Username=postgres;Password=${pgPassword}`;
    addSecret(connectionString);
    const processes = [
      {
        name: 'api',
        project: BACKEND_CSPROJ,
        readyUrl: `http://127.0.0.1:${monolithPort}/api/v1/health/ready`,
        readyBody: 'ready',
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: `http://127.0.0.1:${monolithPort}`,
          ConnectionStrings__DefaultConnection: connectionString,
          Services__Tasks__BaseAddress: `http://127.0.0.1:${tasksPort}/`,
        },
      },
      {
        name: 'tasks',
        project: TASKS_CSPROJ,
        readyUrl: `http://127.0.0.1:${tasksPort}/api/v1/health/ready`,
        readyBody: 'ready',
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: `http://127.0.0.1:${tasksPort}`,
          ConnectionStrings__DefaultConnection: connectionString,
          Services__Monolith__BaseAddress: `http://127.0.0.1:${monolithPort}/`,
        },
      },
      {
        name: 'gateway',
        project: GATEWAY_CSPROJ,
        readyUrl: `http://127.0.0.1:${apiPort}/health/ready`,
        readyBody: null,
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
          ReverseProxy__Clusters__monolith__Destinations__api__Address: `http://127.0.0.1:${monolithPort}/`,
          ReverseProxy__Clusters__tasks__Destinations__tasks__Address: `http://127.0.0.1:${tasksPort}/`,
        },
      },
    ];
    const apiDetail = { apiBase, processes: [] };
    try {
      for (const spec of processes) {
        const stdoutPath = path.join(evidenceDir, `${spec.name}.stdout.log`);
        const stderrPath = path.join(evidenceDir, `${spec.name}.stderr.log`);
        const tracked = await spawnTracked({
          cmd: 'dotnet',
          args: ['run', '--project', spec.project, '-c', 'Release', '--no-build', '--no-launch-profile'],
          cwd: REPO_ROOT,
          env: { ...spec.env, DOTNET_NOLOGO: '1' },
          stdoutPath,
          stderrPath,
        });
        state.children.set(spec.name, tracked);
        apiDetail.processes.push({
          name: spec.name,
          command: tracked.command,
          pid: tracked.pid ?? null,
          readyUrl: spec.readyUrl,
          stdoutPath,
          stderrPath,
        });
        await failFastSpawn(tracked, `dotnet run ${spec.name}`);
        await waitFor({
          timeoutMs: timeouts.apiReady,
          intervalMs: 1000,
          label: `${spec.name} readiness at ${spec.readyUrl}`,
          check: async () => {
            const early = trackedStartProblem(tracked);
            if (early) {
              throw new Error(`dotnet run ${spec.name} failed to start during readiness wait: ${early}`);
            }
            if (!childAlive(tracked.child)) {
              throw new Error(
                `dotnet run ${spec.name} exited during startup (exit ${tracked.exitCode}, signal ${tracked.signal}); see ${spec.name}.stdout.log / ${spec.name}.stderr.log`,
              );
            }
            try {
              const res = await httpGet(spec.readyUrl, { timeoutMs: 5000 });
              return res.status === 200 && (spec.readyBody === null || healthBodySays(res.body, spec.readyBody)) ? true : null;
            } catch {
              return null;
            }
          },
        });
      }
      await waitFor({
        timeoutMs: timeouts.apiReady,
        intervalMs: 1000,
        label: `API readiness through the gateway at ${apiBase}/health/ready`,
        check: async () => {
          try {
            const res = await httpGet(`${apiBase}/health/ready`, { timeoutMs: 5000 });
            return res.status === 200 && healthBodySays(res.body, 'ready') ? true : null;
          } catch {
            return null;
          }
        },
      });
    } catch (err) {
      mark('api-start', 'FAIL', { ...apiDetail, message: err.message }, Date.now() - t0);
      await failStage('api-start', err.message, { detail: { ...apiDetail } });
      await launchCleanup('api start failed');
      process.exit(1);
    }
    mark('api-start', 'PASS', apiDetail, Date.now() - t0);

    t0 = Date.now();
    const setupDetail = { steps: [] };
    try {
      const ci = await runCommand({
        cmd: process.platform === 'win32' ? 'cmd.exe' : 'npm',
        args: process.platform === 'win32'
          ? ['/d', '/s', '/c', 'npm', 'ci', '--no-audit', '--no-fund']
          : ['ci', '--no-audit', '--no-fund'],
        cwd: FRONTEND_DIR,
        env: { NG_CLI_ANALYTICS: 'false' },
        timeoutMs: timeouts.npm,
        stdoutPath: path.join(evidenceDir, 'npm-ci.stdout.log'),
        stderrPath: path.join(evidenceDir, 'npm-ci.stderr.log'),
      });
      setupDetail.steps.push({ step: 'npm ci', ...ci });
      if (ci.exitCode !== 0 || ci.timedOut || ci.spawnError) {
        throw new Error(`npm ci failed (exit ${ci.exitCode})`);
      }
      if (wantFrontBuild) {
        let hasBuildScript = false;
        try {
          const pkg = JSON.parse(await fs.readFile(FRONTEND_PKG, 'utf8'));
          hasBuildScript = typeof pkg?.scripts?.build === 'string' && pkg.scripts.build.trim() !== '';
        } catch {
          hasBuildScript = false;
        }
        if (hasBuildScript) {
          const frontBuild = await runCommand({
            cmd: process.platform === 'win32' ? 'cmd.exe' : 'npm',
            args: process.platform === 'win32' ? ['/d', '/s', '/c', 'npm', 'run', 'build'] : ['run', 'build'],
            cwd: FRONTEND_DIR,
            env: { NG_CLI_ANALYTICS: 'false' },
            timeoutMs: timeouts.frontBuild,
            stdoutPath: path.join(evidenceDir, 'npm-build.stdout.log'),
            stderrPath: path.join(evidenceDir, 'npm-build.stderr.log'),
          });
          setupDetail.steps.push({ step: 'npm run build', ...frontBuild });
          if (frontBuild.exitCode !== 0 || frontBuild.timedOut || frontBuild.spawnError) {
            throw new Error(`npm run build failed (exit ${frontBuild.exitCode})`);
          }
        } else {
          setupDetail.steps.push({ step: 'npm run build', skipped: true, reason: 'no scripts.build in frontend/package.json' });
        }
      }
    } catch (err) {
      mark('frontend-setup', 'FAIL', { ...setupDetail, message: err.message }, Date.now() - t0);
      await failStage('frontend-setup', err.message, { detail: { ...setupDetail } });
      await launchCleanup('frontend setup failed');
      process.exit(1);
    }
    mark('frontend-setup', 'PASS', setupDetail, Date.now() - t0);

    t0 = Date.now();
    const frontPort = wantFrontPort === 0 ? await freePort() : wantFrontPort;
    const frontUrl = `http://127.0.0.1:${frontPort}/`;
    state.frontUrl = frontUrl;
    const proxyFile = path.join(evidenceDir, 'proxy.verify.json');
    await fs.writeFile(
      proxyFile,
      `${JSON.stringify({ '/api/v1': { target: `http://127.0.0.1:${apiPort}`, secure: false, changeOrigin: true, logLevel: 'warn' } }, null, 2)}\n`,
      'utf8',
    );
    const frontStdout = path.join(evidenceDir, 'frontend.stdout.log');
    const frontStderr = path.join(evidenceDir, 'frontend.stderr.log');
    
    
    
    
    const ngEntry = path.join(FRONTEND_DIR, 'node_modules', '@angular', 'cli', 'bin', 'ng.js');
    const frontTracked = await spawnTracked({
      cmd: process.execPath,
      args: [ngEntry, 'serve', '--host=127.0.0.1', `--port=${frontPort}`, '--proxy-config', proxyFile],
      cwd: FRONTEND_DIR,
      env: { NG_CLI_ANALYTICS: 'false' },
      stdoutPath: frontStdout,
      stderrPath: frontStderr,
    });
    state.children.set('frontend', frontTracked);
    const serveDetail = {
      command: frontTracked.command,
      cwd: FRONTEND_DIR,
      pid: frontTracked.pid ?? null,
      frontUrl,
      proxyFile,
      ngEntry,
      stdoutPath: frontStdout,
      stderrPath: frontStderr,
    };
    try {
      await failFastSpawn(frontTracked, 'ng serve');
      await waitFor({
        timeoutMs: timeouts.frontReady,
        intervalMs: 1500,
        label: `frontend at ${frontUrl}`,
        check: async () => {
          const early = trackedStartProblem(frontTracked);
          if (early) {
            throw new Error(`ng serve failed to start during readiness wait: ${early}`);
          }
          if (!childAlive(frontTracked.child)) {
            throw new Error(
              `ng serve exited during startup (exit ${frontTracked.exitCode}, signal ${frontTracked.signal}); see frontend.stdout.log / frontend.stderr.log`,
            );
          }
          try {
            const res = await httpGet(frontUrl, { timeoutMs: 5000 });
            if (res.status !== 200) return null;
            return /<!doctype html|<app-root/i.test(res.body) ? { status: res.status, htmlBytes: res.body.length } : null;
          } catch {
            return null;
          }
        },
      });
    } catch (err) {
      mark('frontend-serve', 'FAIL', { ...serveDetail, message: err.message }, Date.now() - t0);
      await failStage('frontend-serve', err.message, { detail: { ...serveDetail } });
      await launchCleanup('frontend serve failed');
      process.exit(1);
    }
    mark('frontend-serve', 'PASS', serveDetail, Date.now() - t0);

    t0 = Date.now();
    const nonce = crypto.randomBytes(32).toString('hex');
    const nonceFile = path.join(evidenceDir, 'control-nonce.txt');
    await fs.writeFile(nonceFile, `${nonce}\n`, { encoding: 'utf8', mode: 0o600 });
    try {
      await fs.chmod(nonceFile, 0o600);
    } catch {
      // Windows ACLs: best effort, file already holds a random nonce.
    }
    const runRecord = {
      runId,
      repoRoot: REPO_ROOT,
      evidenceDir,
      createdAt: startedAt,
      readyAt: new Date().toISOString(),
      apiBase,
      frontUrl,
      pg: { host: '127.0.0.1', port: pgPort, database: DEFAULT_DB_NAME },
      containerName: state.containerName,
      containerId: state.containerId,
      volumeName: state.volumeName,
      ownership: { runId, nonce: ownershipNonce, labels: [labelRun, `${LABEL_NONCE}=<nonce>`] },
      image: pgImage,
      control: { host: '127.0.0.1', port: 0, nonceFile: 'control-nonce.txt' },
      children: {
        ...Object.fromEntries(['api', 'tasks', 'gateway'].map((name) => {
          const tracked = state.children.get(name);
          return [name, { pid: tracked.pid ?? null, startedAt: tracked.startedAt }];
        })),
        frontend: { pid: frontTracked.pid ?? null, startedAt: frontTracked.startedAt },
      },
      versions: prereqInfo.versions,
      timeouts,
    };

    const server = http.createServer((req, res) => {
      const presented = req.headers['x-verify-nonce'];
      if (presented !== nonce) {
        res.writeHead(403, { 'content-type': 'application/json' });
        res.end(JSON.stringify({ ok: false, error: 'forbidden' }));
        return;
      }
      if (req.method === 'GET' && req.url === '/status') {
        res.writeHead(200, { 'content-type': 'application/json' });
        res.end(
          JSON.stringify({
            ok: true,
            runId,
            apiBase,
            frontUrl,
            children: {
              ...Object.fromEntries(['api', 'tasks', 'gateway'].map((name) => {
                const tracked = state.children.get(name);
                return [name, { pid: tracked.pid ?? null, alive: childAlive(tracked.child) }];
              })),
              frontend: { pid: frontTracked.pid ?? null, alive: childAlive(frontTracked.child) },
            },
          }),
        );
        return;
      }
      if (req.method === 'POST' && req.url === '/cleanup') {
        req.resume();
        req.on('end', () => {
          teardownOwned({
            children: state.children,
            containerName: state.containerName,
            volumeName: state.volumeName,
            dockerResources: state.containerCreated,
            evidenceDir,
            runId,
            reason: 'control cleanup',
            timeout: timeouts.cleanup,
            tag: 'cleanup',
          })
            .then((result) => {
              res.writeHead(200, { 'content-type': 'application/json' });
              res.end(JSON.stringify({ ok: result.ok, status: result.status, runId, actions: result.actions, ownerWillExit: true }));
              setTimeout(() => process.exit(result.ok ? 0 : 1), 300);
            })
            .catch((err) => {
              res.writeHead(500, { 'content-type': 'application/json' });
              res.end(JSON.stringify({ ok: false, runId, error: String(err?.message ?? err) }));
            });
        });
        return;
      }
      res.writeHead(404, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ ok: false, error: 'not found' }));
    });
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(0, '127.0.0.1', () => resolve());
    });
    runRecord.control.port = server.address()?.port ?? 0;
    await writeJson('run.json', runRecord);
    mark('ready', 'PASS', { controlPort: runRecord.control.port, apiBase, frontUrl }, Date.now() - t0);

    
    await writeJson('summary.json', {
      runId,
      status: 'READY',
      startedAt,
      endedAt: new Date().toISOString(),
      repoRoot: REPO_ROOT,
      evidenceDir,
      urls: { apiBase, front: frontUrl },
      failure: null,
      verification: { ...notRun },
      note: 'Servers up. API smoke and browser verification pending; READY is not a template PASS.',
      stages,
    });

    console.log(`${READY_SENTINEL} runId=${runId} api=${apiBase}/ front=${frontUrl}`);
    console.log(`evidence=${evidenceDir}`);
    console.log('owner parked in foreground: doctor/api/cleanup attach via --run-id; Ctrl+C triggers owned cleanup.');
    await new Promise(() => {});
  } catch (err) {
    try {
      await failStage('ready', err?.message ?? String(err), {});
    } catch {
      // Evidence write itself failed; report plainly.
    }
    try {
      await launchCleanup('launch crashed');
    } catch {
      // Best effort only.
    }
    console.error(redact(`launch failed: ${err?.message ?? err}`));
    process.exit(1);
  }
}

async function loadRun(runId, evidenceRoot) {
  const evidenceDir = path.join(REPO_ROOT, evidenceRoot, runId);
  let runRecord;
  try {
    runRecord = JSON.parse(await fs.readFile(path.join(evidenceDir, 'run.json'), 'utf8'));
  } catch {
    throw new Error(`no run.json for run-id ${runId} (expected ${evidenceDir}/run.json; launch first)`);
  }
  if (runRecord.runId !== runId) throw new Error(`run.json runId mismatch (expected ${runId})`);
  let nonce;
  try {
    nonce = (await fs.readFile(path.join(evidenceDir, runRecord.control?.nonceFile ?? 'control-nonce.txt'), 'utf8')).trim();
  } catch {
    throw new Error(`control nonce unreadable for run-id ${runId}; owner may have been cleaned up`);
  }
  return { evidenceDir, runRecord, nonce };
}

async function readJsonIfExists(file) {
  try {
    return JSON.parse(await fs.readFile(file, 'utf8'));
  } catch {
    return null;
  }
}

function emitAndExit(payload, code) {
  console.log(JSON.stringify(payload, null, 2));
  process.exit(code);
}

async function doctor(flags) {
  const runId = validatedRunId(flags);
  const at = new Date().toISOString();
  let loaded;
  try {
    loaded = await loadRun(runId, evidenceRootOf(flags));
  } catch (err) {
    emitAndExit({ runId, status: 'FAIL', at, checks: [{ name: 'run-record', ok: false, error: err.message }] }, 1);
    return;
  }
  const { evidenceDir, runRecord, nonce } = loaded;
  const checks = [];
  try {
    const res = await httpJson('GET', `http://${runRecord.control.host}:${runRecord.control.port}/status`, {
      timeoutMs: 15000,
      headers: { 'x-verify-nonce': nonce },
    });
    const ok = res.status === 200 && res.body?.ok === true && res.body?.runId === runId;
    checks.push({
      name: 'owner-control',
      ok,
      detail: ok ? { children: res.body.children } : { status: res.status, body: res.body },
    });
  } catch (err) {
    checks.push({ name: 'owner-control', ok: false, error: String(err?.message ?? err) });
  }
  for (const [endpoint, expected] of [['health/live', 'live'], ['health/ready', 'ready']]) {
    try {
      const res = await httpGet(`${runRecord.apiBase}/${endpoint}`, { timeoutMs: 15000 });
      const ok = res.status === 200 && healthBodySays(res.body, expected);
      checks.push({ name: `api:${endpoint}`, ok, detail: { status: res.status, bodyMatch: ok } });
    } catch (err) {
      checks.push({ name: `api:${endpoint}`, ok: false, error: String(err?.message ?? err) });
    }
  }
  try {
    const res = await httpGet(runRecord.frontUrl, { timeoutMs: 15000 });
    const html = res.status === 200 && /<!doctype html|<app-root/i.test(res.body);
    checks.push({ name: 'front:served-html', ok: html, detail: { status: res.status, htmlBytes: res.body.length } });
  } catch (err) {
    checks.push({ name: 'front:served-html', ok: false, error: String(err?.message ?? err) });
  }
  const summary = await readJsonIfExists(path.join(evidenceDir, 'summary.json'));
  const payload = {
    runId,
    status: checks.every((check) => check.ok) ? 'PASS' : 'FAIL',
    at,
    apiBase: runRecord.apiBase,
    frontUrl: runRecord.frontUrl,
    verification: summary?.verification ?? { apiSmoke: 'UNKNOWN', browser: 'UNKNOWN' },
    checks,
    note: 'read-only: no processes started, stopped, or modified; serving proven, real user flow not asserted',
  };
  await fs.writeFile(path.join(evidenceDir, 'doctor.json'), `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
  emitAndExit(payload, payload.status === 'PASS' ? 0 : 1);
}

async function api(flags) {
  const runId = validatedRunId(flags);
  const smokeTimeout = timeoutMs(flags, 'timeout-smoke-ms', 'VERIFY_TIMEOUT_SMOKE_MS', 300000);
  const at = new Date().toISOString();
  let loaded;
  try {
    loaded = await loadRun(runId, evidenceRootOf(flags));
  } catch (err) {
    emitAndExit({ runId, status: 'FAIL', at, error: err.message }, 1);
    return;
  }
  const { evidenceDir, runRecord, nonce } = loaded;
  commandLogPath = path.join(evidenceDir, 'commands.jsonl');
  try {
    const res = await httpJson('GET', `http://${runRecord.control.host}:${runRecord.control.port}/status`, {
      timeoutMs: 15000,
      headers: { 'x-verify-nonce': nonce },
    });
    if (!(res.status === 200 && res.body?.ok === true && res.body?.runId === runId)) {
      throw new Error(`owner control rejected this run (status ${res.status})`);
    }
  } catch (err) {
    const payload = { runId, status: 'FAIL', at, error: `owner not verified: ${err.message}` };
    await fs.writeFile(path.join(evidenceDir, 'api-smoke.json'), `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
    emitAndExit(payload, 1);
    return;
  }
  
  const apiBaseUrl = runRecord.apiBase.endsWith('/') ? runRecord.apiBase : `${runRecord.apiBase}/`;
  const result = await runCommand({
    cmd: process.execPath,
    args: [SMOKE_SCRIPT],
    cwd: SCRIPT_DIR,
    env: { API_BASE_URL: apiBaseUrl },
    timeoutMs: smokeTimeout,
    stdoutPath: path.join(evidenceDir, 'api-smoke.stdout.log'),
    stderrPath: path.join(evidenceDir, 'api-smoke.stderr.log'),
  });
  const smokeStatus = result.exitCode === 0 && !result.timedOut && !result.spawnError ? 'PASS' : 'FAIL';
  const payload = {
    runId,
    status: smokeStatus,
    scope: 'api-smoke',
    at,
    endedAt: new Date().toISOString(),
    command: result.command,
    apiBaseUrl,
    exitCode: result.exitCode,
    timedOut: result.timedOut,
    stdoutPath: result.stdoutPath,
    stderrPath: result.stderrPath,
    note: 'API scope only; browser verification still NOT_RUN, no UI PASS claimed',
  };
  await fs.writeFile(path.join(evidenceDir, 'api-smoke.json'), `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
  
  try {
    const summaryPath = path.join(evidenceDir, 'summary.json');
    const summary = await readJsonIfExists(summaryPath);
    if (summary && Array.isArray(summary.stages)) {
      const stage = summary.stages.find((entry) => entry.name === 'api-smoke');
      if (stage) {
        stage.status = smokeStatus;
        stage.detail = {
          exitCode: result.exitCode,
          timedOut: result.timedOut,
          stdoutPath: result.stdoutPath,
          stderrPath: result.stderrPath,
        };
      }
      summary.verification = { apiSmoke: smokeStatus, browser: summary.verification?.browser ?? 'NOT_RUN' };
      if (smokeStatus === 'PASS') {
        summary.status = 'API_VERIFIED';
        summary.note = 'API smoke passed. Browser verification still NOT_RUN; no UI PASS claimed.';
      } else {
        summary.status = 'FAIL';
        summary.failure = { stage: 'api-smoke', message: `smoke-api.mjs exit ${result.exitCode}${result.timedOut ? ' (timed out)' : ''}` };
      }
      summary.endedAt = new Date().toISOString();
      await fs.writeFile(summaryPath, `${JSON.stringify(summary, null, 2)}\n`, 'utf8');
    }
  } catch {
    // api-smoke.json already holds the result; summary refresh is best effort.
  }
  emitAndExit(payload, smokeStatus === 'PASS' ? 0 : 1);
}

async function cleanup(flags) {
  const runId = validatedRunId(flags);
  const evidenceRoot = evidenceRootOf(flags);
  const timeout = timeoutMs(flags, 'timeout-cleanup-ms', 'VERIFY_TIMEOUT_CLEANUP_MS', 120000);
  const evidenceDir = path.join(REPO_ROOT, evidenceRoot, runId);

  
  
  const prior = await readJsonIfExists(path.join(evidenceDir, 'cleanup.json'));
  if (prior && prior.ok === true) {
    console.log(JSON.stringify({ ...prior, replayed: true }, null, 2));
    process.exit(prior.ok ? 0 : 1);
  }

  let loaded;
  try {
    loaded = await loadRun(runId, evidenceRoot);
  } catch (err) {
    if (prior) {
      
      
      console.log(JSON.stringify({ ...prior, replayed: true }, null, 2));
      process.exit(prior.ok ? 0 : 1);
    }
    emitAndExit({ runId, status: 'FAIL', at: new Date().toISOString(), error: err.message }, 1);
    return;
  }
  const { runRecord, nonce } = loaded;
  commandLogPath = path.join(evidenceDir, 'commands.jsonl');

  try {
    const res = await httpJson('POST', `http://${runRecord.control.host}:${runRecord.control.port}/cleanup`, {
      timeoutMs: timeout + 30000,
      headers: { 'x-verify-nonce': nonce },
      body: { runId },
    });
    const payload = {
      runId,
      status: res.status === 200 && res.body?.ok ? 'PASS' : 'FAIL',
      at: new Date().toISOString(),
      via: 'owner-control',
      actions: res.body?.actions ?? [{ status: res.status, body: res.body }],
      evidenceRetained: true,
    };
    console.log(JSON.stringify(payload, null, 2));
    process.exit(payload.status === 'PASS' ? 0 : 1);
  } catch {
    
    
    
    
    const result = await teardownOwned({
      children: null,
      containerName: runRecord.containerName,
      volumeName: runRecord.volumeName,
      verify: true,
      runRecord,
      evidenceDir,
      runId,
      reason: 'detached cleanup: owner unreachable, ownership re-verified via live inspect',
      timeout,
      tag: 'cleanup-fallback',
    });
    const payload = {
      runId,
      status: result.status,
      at: new Date().toISOString(),
      via: 'verified-fallback',
      ownerUnreachable: true,
      note: 'child handles unknown outside the owner process; removed only id+label-proven container/volume, nothing by bare name',
      actions: result.actions,
      evidenceRetained: true,
    };
    console.log(JSON.stringify(payload, null, 2));
    process.exit(result.ok ? 0 : 1);
  }
}

async function main() {
  let parsed;
  try {
    parsed = parseArgs(process.argv.slice(2));
  } catch (err) {
    console.error(err.message);
    console.error(usage());
    process.exit(2);
  }
  const { command, flags } = parsed;
  if (flags.help === true || flags.h === true || command === 'help') {
    console.log(usage());
    process.exit(0);
  }
  try {
    if (command === 'launch') await launch(flags);
    else if (command === 'doctor') await doctor(flags);
    else if (command === 'api') await api(flags);
    else if (command === 'cleanup') await cleanup(flags);
    else {
      console.error(command === null ? 'missing command' : `unknown command: ${command}`);
      console.error(usage());
      process.exit(2);
    }
  } catch (err) {
    console.error(redact(err?.message ?? String(err)));
    process.exit(1);
  }
}

await main();
