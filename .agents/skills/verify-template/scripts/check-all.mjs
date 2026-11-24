#!/usr/bin/env node
import { spawn } from 'node:child_process';
import { createWriteStream, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(SCRIPT_DIR, '..', '..', '..', '..');
const FRONTEND_DIR = path.join(REPO_ROOT, 'frontend');
const VERIFY_SCRIPT = path.join(SCRIPT_DIR, 'verify.mjs');
const RUN_ID_RE = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/;
const READY_SENTINEL = 'VERIFY_TEMPLATE_READY';
const RUNTIME_RUN_ID = 'runtime';
const LIMITER_WINDOW_MS = 65_000;
const LAUNCH_TIMEOUT_MS = 30 * 60_000;
const STEP_TIMEOUT_MS = 20 * 60_000;

function usage() {
  return [
    'check-all.mjs — runs the README gates and the verify-template runtime in order, with one summary.',
    '',
    'Usage (cwd = repo root):',
    '  node .agents/skills/verify-template/scripts/check-all.mjs --run-id <id> [--gates-only] [--no-e2e]',
    '',
    '  --gates-only   README gates only; launch, doctor, api, e2e and cleanup stay NOT_RUN',
    '  --no-e2e       skip the Playwright specs (E2E_CHANNEL is forwarded when set)',
    '',
    'Evidence: artifacts/verify-template/<id>/summary.json, one stdout/stderr log per stage,',
    'and the verify.mjs run in artifacts/verify-template/<id>/runtime/.',
    'The first FAIL stops the run: later stages are NOT_RUN. cleanup always runs after launch.',
  ].join('\n');
}

function parseArgs(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (!token.startsWith('--')) throw new Error(`unexpected argument: ${token}`);
    const name = token.slice(2);
    if (name === 'run-id') {
      flags.runId = argv[i + 1];
      i += 1;
    } else if (name === 'gates-only' || name === 'no-e2e' || name === 'help') {
      flags[name] = true;
    } else {
      throw new Error(`unknown option: ${token}`);
    }
  }
  return flags;
}

function npm(...args) {
  return process.platform === 'win32'
    ? { cmd: 'cmd.exe', args: ['/d', '/s', '/c', 'npm', ...args] }
    : { cmd: 'npm', args };
}

function node(...args) {
  return { cmd: process.execPath, args };
}

function promtool(...args) {
  const image = readFileSync(path.join(REPO_ROOT, 'compose.yaml'), 'utf8').match(/prom\/prometheus:[^"\s]+/)[0];
  const rules = `${path.join(REPO_ROOT, 'deploy', 'prometheus')}:/etc/prometheus:ro`;
  return { cmd: 'docker', args: ['run', '--rm', '--entrypoint', 'promtool', '--volume', rules, image, ...args] };
}

const GATES = [
  { name: 'dotnet-build', cmd: 'dotnet', args: ['build', 'TemplateApp.slnx', '--configuration', 'Release'] },
  { name: 'check-codegen', ...node('dune/check-codegen.mjs', '--release') },
  { name: 'check-codegen-test', ...node('--test', 'dune/check-codegen.test.mjs') },
  { name: 'check-host-items', ...node('dune/check-host-items.mjs') },
  { name: 'check-host-items-test', ...node('--test', 'dune/check-host-items.test.mjs') },
  { name: 'dotnet-test', cmd: 'dotnet', args: ['test', 'TemplateApp.slnx', '--configuration', 'Release'] },
  { name: 'comment-verifier', cmd: 'dotnet', args: ['run', '--project', 'dune/Dune.CommentVerifier', '--', '--root', '.'] },
  { name: 'check-structure', ...node('dune/check-structure.mjs') },
  { name: 'check-structure-test', ...node('--test', 'dune/check-structure.test.mjs') },
  { name: 'check-secrets', ...node('dune/check-secrets.mjs') },
  { name: 'check-secrets-test', ...node('--test', 'dune/check-secrets.test.mjs') },
  { name: 'check-otel-export-test', ...node('--test', '.agents/skills/verify-template/scripts/check-otel-export.test.mjs') },
  { name: 'rehearsal-verdict-test', ...node('--test', '.agents/skills/verify-template/scripts/rehearsal-verdict.test.mjs') },
  { name: 'nuget-audit', cmd: 'dotnet', args: ['restore', 'TemplateApp.slnx', '--force', '-warnaserror:NU1903;NU1904'] },
  { name: 'npm-ci', ...npm('ci', '--prefix', 'frontend') },
  { name: 'npm-audit', ...npm('audit', '--omit=dev', '--audit-level=high'), cwd: FRONTEND_DIR },
  { name: 'check-comments', ...node('dune/check-comments.mjs', '--root', '.') },
  { name: 'check-comments-test', ...node('--test', 'dune/check-comments.test.mjs') },
  { name: 'check-runbooks', ...node('dune/check-runbooks.mjs') },
  { name: 'check-runbooks-test', ...node('--test', 'dune/check-runbooks.test.mjs') },
  { name: 'promtool-check', ...promtool('check', 'config', '--lint-fatal', '/etc/prometheus/prometheus.yml') },
  { name: 'promtool-test', ...promtool('test', 'rules', '/etc/prometheus/rules/slo-api.test.yml') },
  { name: 'front-build', ...npm('run', 'build'), cwd: FRONTEND_DIR },
  { name: 'format-check', ...npm('run', 'format:check'), cwd: FRONTEND_DIR },
  { name: 'vitest', ...npm('test', '--', '--watch=false'), cwd: FRONTEND_DIR },
  { name: 'check-frontend-test', ...node('--test', '../dune/check-frontend.test.mjs'), cwd: FRONTEND_DIR },
  { name: 'check-frontend', ...node('../dune/check-frontend.mjs', '--root', '..'), cwd: FRONTEND_DIR },
];

const RUNTIME_STAGES = ['launch', 'doctor', 'api', 'e2e', 'cleanup'];

function startProcess({ cmd, args, cwd = REPO_ROOT, env = {}, logBase, onStdout }) {
  const stdoutPath = `${logBase}.stdout.log`;
  const stderrPath = `${logBase}.stderr.log`;
  const out = createWriteStream(stdoutPath);
  const err = createWriteStream(stderrPath);
  const child = spawn(cmd, args, { cwd, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
  child.stdout.on('data', (chunk) => {
    out.write(chunk);
    onStdout?.(chunk.toString('utf8'));
  });
  child.stderr.on('data', (chunk) => err.write(chunk));
  const exited = new Promise((resolve) => {
    child.once('error', (error) => resolve({ exitCode: null, spawnError: String(error) }));
    child.once('close', (code) => resolve({ exitCode: code, spawnError: null }));
  }).then(async (result) => {
    await Promise.all([new Promise((done) => out.end(done)), new Promise((done) => err.end(done))]);
    return result;
  });
  return { child, exited, stdoutPath, stderrPath };
}

function killTree(child) {
  if (child.exitCode !== null || child.pid === undefined) return;
  if (process.platform === 'win32') spawn('taskkill', ['/pid', String(child.pid), '/t', '/f'], { stdio: 'ignore', windowsHide: true });
  else child.kill('SIGKILL');
}

function withTimeout(promise, ms) {
  let timer;
  const timeout = new Promise((resolve) => {
    timer = setTimeout(() => resolve({ timedOut: true }), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

function relative(file) {
  return path.relative(REPO_ROOT, file).split(path.sep).join('/');
}

function commandText(cmd, args) {
  return [cmd === process.execPath ? 'node' : cmd, ...args].join(' ');
}

async function main() {
  let flags;
  try {
    flags = parseArgs(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    console.error(usage());
    process.exit(2);
  }
  if (flags.help) {
    console.log(usage());
    process.exit(0);
  }
  if (typeof flags.runId !== 'string' || !RUN_ID_RE.test(flags.runId)) {
    console.error('missing or invalid --run-id (expected [A-Za-z0-9_-], 1-64 chars, leading alnum)');
    process.exit(2);
  }

  const runId = flags.runId;
  const evidenceRoot = `artifacts/verify-template/${runId}`;
  const evidenceDir = path.join(REPO_ROOT, evidenceRoot);
  const runtimeDir = path.join(evidenceDir, RUNTIME_RUN_ID);
  mkdirSync(evidenceDir, { recursive: true });

  const startedAt = new Date().toISOString();
  const stages = [...GATES.map((gate) => gate.name), ...RUNTIME_STAGES].map((name) => ({ name, status: 'NOT_RUN' }));
  const stage = (name) => stages.find((entry) => entry.name === name);
  let failed = false;

  const writeSummary = (status) => {
    const failure = stages.find((entry) => entry.status === 'FAIL') ?? null;
    writeFileSync(
      path.join(evidenceDir, 'summary.json'),
      `${JSON.stringify({
        runId,
        status,
        startedAt,
        endedAt: new Date().toISOString(),
        options: { gatesOnly: !!flags['gates-only'], e2e: !flags['gates-only'] && !flags['no-e2e'] },
        firstFailure: failure ? failure.name : null,
        stages,
      }, null, 2)}\n`,
      'utf8',
    );
  };

  const record = (name, status, detail) => {
    Object.assign(stage(name), { status, ...detail });
    if (status === 'FAIL') failed = true;
    console.log(`${status.padEnd(7)} ${name}${detail.durationMs === undefined ? '' : ` (${Math.round(detail.durationMs / 1000)}s)`}`);
    writeSummary('RUNNING');
  };

  const runStep = async (name, { cmd, args, cwd = REPO_ROOT, env = {} }) => {
    const began = Date.now();
    const proc = startProcess({ cmd, args, cwd, env, logBase: path.join(evidenceDir, name) });
    const result = await withTimeout(proc.exited, STEP_TIMEOUT_MS);
    if (result.timedOut) {
      killTree(proc.child);
      await proc.exited;
    }
    const ok = !result.timedOut && result.exitCode === 0;
    record(name, ok ? 'PASS' : 'FAIL', {
      command: commandText(cmd, args),
      cwd: relative(cwd) || '.',
      exitCode: result.exitCode ?? null,
      timedOut: !!result.timedOut,
      spawnError: result.spawnError ?? null,
      durationMs: Date.now() - began,
      stdout: relative(proc.stdoutPath),
      stderr: relative(proc.stderrPath),
    });
    return ok;
  };

  console.log(`check-all runId=${runId} evidence=${evidenceRoot}`);
  writeSummary('RUNNING');

  for (const gate of GATES) {
    if (!(await runStep(gate.name, gate))) break;
  }

  if (!failed && !flags['gates-only']) {
    const runtimeFlags = ['--run-id', RUNTIME_RUN_ID, '--evidence-root', evidenceRoot];
    const began = Date.now();
    let readyLine = null;
    let buffered = '';
    let markReady;
    const ready = new Promise((resolve) => {
      markReady = resolve;
    });
    const owner = startProcess({
      ...node(VERIFY_SCRIPT, 'launch', ...runtimeFlags, '--front-build'),
      logBase: path.join(evidenceDir, 'launch'),
      onStdout: (text) => {
        buffered += text;
        const line = buffered.split(/\r?\n/).find((entry) => entry.startsWith(READY_SENTINEL));
        if (line && readyLine === null) {
          readyLine = line;
          markReady({ ready: true });
        }
      },
    });
    const launched = await withTimeout(Promise.race([ready, owner.exited]), LAUNCH_TIMEOUT_MS);
    record('launch', launched.ready ? 'PASS' : 'FAIL', {
      command: commandText('node', [relative(VERIFY_SCRIPT), 'launch', ...runtimeFlags, '--front-build']),
      cwd: '.',
      exitCode: launched.ready ? null : (launched.exitCode ?? null),
      timedOut: !!launched.timedOut,
      durationMs: Date.now() - began,
      stdout: relative(owner.stdoutPath),
      stderr: relative(owner.stderrPath),
      runtimeSummary: relative(path.join(runtimeDir, 'summary.json')),
    });

    if (!failed && (await runStep('doctor', node(VERIFY_SCRIPT, 'doctor', ...runtimeFlags)))
      && (await runStep('api', node(VERIFY_SCRIPT, 'api', ...runtimeFlags)))
      && !flags['no-e2e']) {
      const frontUrl = JSON.parse(readFileSync(path.join(runtimeDir, 'run.json'), 'utf8')).frontUrl;
      console.log(`waiting ${LIMITER_WINDOW_MS / 1000}s for the /auth limiter window before e2e`);
      await new Promise((resolve) => setTimeout(resolve, LIMITER_WINDOW_MS));
      await runStep('e2e', {
        ...npm('run', 'e2e'),
        cwd: FRONTEND_DIR,
        env: { E2E_BASE_URL: frontUrl, E2E_OUTPUT_DIR: path.join(evidenceDir, 'e2e') },
      });
    }

    await runStep('cleanup', node(VERIFY_SCRIPT, 'cleanup', ...runtimeFlags));
    const ownerExit = await withTimeout(owner.exited, 60_000);
    if (ownerExit.timedOut) {
      killTree(owner.child);
      await owner.exited;
    }
  }

  const status = failed ? 'FAIL' : 'PASS';
  writeSummary(status);
  console.log(`check-all ${status} summary=${evidenceRoot}/summary.json`);
  process.exit(failed ? 1 : 0);
}

await main();
