import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);
const script = path.join(path.dirname(fileURLToPath(import.meta.url)), 'check-runbooks.mjs');
const URL_PREFIX = 'https://github.com/tobiasesquivel/full-stack-template/blob/main/docs/operacion/runbooks/';
const SECTIONS = ['Significado', 'Impacto', 'Diagnóstico', 'Mitigación', 'Cómo probarla'];

function runbook(alert, sections = SECTIONS) {
  return [`# ${alert}`, '', ...sections.flatMap(section => [`## ${section}`, '', 'Texto.', ''])].join('\n');
}

function rule(alert, annotations = { runbook_url: `${URL_PREFIX}${alert}.md` }) {
  const lines = [`      - alert: ${alert}`, '        expr: vector(1)', '        labels:', '          severity: page'];
  const entries = Object.entries(annotations);
  if (entries.length) lines.push('        annotations:', ...entries.map(([key, value]) => `          ${key}: ${value}`));
  return lines.join('\n');
}

async function makeRepo({ rules = [rule('ApiDown')], runbooks = { ApiDown: runbook('ApiDown') }, ruleFiles = ['rules/api.rules.yml'] } = {}) {
  const dir = await mkdtemp(path.join(tmpdir(), 'dune-runbooks-'));
  const write = async (rel, content) => {
    const full = path.join(dir, rel);
    await mkdir(path.dirname(full), { recursive: true });
    await writeFile(full, content);
  };
  await write('deploy/prometheus/prometheus.yml', ['rule_files:', ...ruleFiles.map(file => `  - ${file}`), ''].join('\n'));
  await write('deploy/prometheus/rules/api.rules.yml', ['groups:', '  - name: api', '    rules:', ...rules, ''].join('\n'));
  await mkdir(path.join(dir, 'docs/operacion/runbooks'), { recursive: true });
  for (const [name, content] of Object.entries(runbooks)) await write(`docs/operacion/runbooks/${name}.md`, content);
  return { dir, cleanup: () => rm(dir, { recursive: true, force: true }) };
}

async function check(options) {
  const repo = await makeRepo(options);
  try {
    const { stdout, stderr } = await run(process.execPath, [script, '--root', repo.dir]);
    return { code: 0, output: stdout + stderr };
  } catch (error) {
    return { code: error.code, output: (error.stdout ?? '') + (error.stderr ?? '') };
  } finally {
    await repo.cleanup();
  }
}

async function assertViolation(options, ruleId) {
  const result = await check(options);
  assert.equal(result.code, 1, result.output);
  assert.match(result.output, new RegExp(`\\b${ruleId}\\b`), result.output);
}

describe('check-runbooks', () => {
  it('passes when every alert links its runbook with every section', async () => {
    const result = await check();
    assert.equal(result.code, 0, result.output);
    assert.match(result.output, /check-runbooks: OK \(1 alertas, 1 runbooks\)/);
  });

  it('passes when two rules share an alert name and its runbook', async () => {
    const result = await check({ rules: [rule('ApiDown'), rule('ApiDown')] });
    assert.equal(result.code, 0, result.output);
  });

  it('reads the rule files a glob in prometheus.yml names', async () => {
    const result = await check({ ruleFiles: ['rules/*.rules.yml'] });
    assert.equal(result.code, 0, result.output);
    assert.match(result.output, /check-runbooks: OK \(1 alertas, 1 runbooks\)/);
  });

  it('fails when a glob in prometheus.yml matches no rule file', async () => {
    await assertViolation({ ruleFiles: ['rules/api.rules.yml', 'rules/*.missing.yml'] }, 'rule-file-missing');
  });

  it('fails when an alert has no runbook_url', async () => {
    await assertViolation({ rules: [rule('ApiDown', { summary: 'sin runbook' })] }, 'runbook-url-missing');
  });

  it('fails when the runbook_url points outside the runbook folder', async () => {
    await assertViolation({ rules: [rule('ApiDown', { runbook_url: 'https://example.test/ApiDown.md' })] }, 'runbook-url-mismatch');
  });

  it('fails when the runbook_url names another alert', async () => {
    await assertViolation({
      rules: [rule('ApiDown', { runbook_url: `${URL_PREFIX}ApiSlow.md` })],
      runbooks: { ApiDown: runbook('ApiDown'), ApiSlow: runbook('ApiSlow') },
    }, 'runbook-url-mismatch');
  });

  it('fails when the runbook file does not exist', async () => {
    await assertViolation({ runbooks: {} }, 'runbook-missing');
  });

  it('fails when the runbook title is not the alert name', async () => {
    await assertViolation({ runbooks: { ApiDown: runbook('ApiCaida') } }, 'runbook-title');
  });

  it('fails when the runbook lacks a section', async () => {
    await assertViolation({ runbooks: { ApiDown: runbook('ApiDown', SECTIONS.filter(section => section !== 'Mitigación')) } }, 'runbook-section-missing');
  });

  it('fails on a runbook without an alert', async () => {
    await assertViolation({ runbooks: { ApiDown: runbook('ApiDown'), ApiOld: runbook('ApiOld') } }, 'runbook-orphan');
  });

  it('fails when the rule files hold no alert', async () => {
    await assertViolation({ rules: ['      - record: job:up:sum', '        expr: vector(1)'], runbooks: {} }, 'no-alerts');
  });

  it('fails when prometheus.yml names a rule file that does not exist', async () => {
    await assertViolation({ ruleFiles: ['rules/api.rules.yml', 'rules/missing.rules.yml'] }, 'rule-file-missing');
  });
});
