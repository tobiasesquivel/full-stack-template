import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);
const script = path.join(path.dirname(fileURLToPath(import.meta.url)), 'check-otel-export.mjs');

const TRACE_A = '04f4ef2d5ae9e7313a01eabed358276d';
const TRACE_B = '9a1c3f0e7b2d4c6a8e0f1a2b3c4d5e6f';
const SERVER_A = 'e246b23fd4abf8e9';
const SERVER_B = '1b2c3d4e5f6a7b8c';
const NPGSQL_A = 'aa11bb22cc33dd44';
const SERVER = 2;
const CLIENT = 3;

const text = value => ({ stringValue: value });

function requestLog(traceId, spanId, path = '/api/v1/tasks') {
  const record = {
    severityText: 'Information',
    body: text(`HTTP GET ${path} responded 200`),
    attributes: [
      { key: 'RequestId', value: text('0HNQ4C2JPKRO1:00000001') },
      { key: 'RequestPath', value: text(path) },
    ],
  };
  if (traceId !== undefined) record.traceId = traceId;
  if (spanId !== undefined) record.spanId = spanId;
  return record;
}

const startupLog = { severityText: 'Information', body: text('Now listening on: http://[::]:8080'), attributes: [] };

const span = (traceId, spanId, kind, name = 'GET /api/v1/tasks') => ({ traceId, spanId, kind, name });

const sum = (name, dataPoints = [{ asInt: '1' }]) => ({ name, sum: { dataPoints } });

function validExport() {
  return {
    logs: [startupLog, requestLog(TRACE_A, SERVER_A), requestLog(TRACE_B.toUpperCase(), SERVER_B.toUpperCase())],
    spans: [
      span(TRACE_A, SERVER_A, SERVER),
      span(TRACE_A, NPGSQL_A, CLIENT, 'templateapp'),
      span(TRACE_B, SERVER_B, SERVER),
    ],
    metrics: [
      sum('templateapp.tasks.created'),
      { name: 'http.server.request.duration', histogram: { dataPoints: [{ count: '3' }] } },
    ],
  };
}

function exportLines(resourceKey, scopeKey, itemKey, items) {
  const resource = item => ({ resource: {}, [scopeKey]: [{ scope: { name: 'fixture' }, [itemKey]: [item] }, { scope: { name: 'empty' } }] });
  const [first, ...rest] = items;
  const lines = [{ [resourceKey]: first === undefined ? [] : [resource(first)] }];
  if (rest.length) lines.push({ [resourceKey]: rest.map(resource) });
  return lines.map(line => JSON.stringify(line)).join('\n');
}

async function writeExport(dir, { logs, spans, metrics }, suffix = '') {
  const files = {
    'logs.json': logs && exportLines('resourceLogs', 'scopeLogs', 'logRecords', logs),
    'traces.json': spans && exportLines('resourceSpans', 'scopeSpans', 'spans', spans),
    'metrics.json': metrics && exportLines('resourceMetrics', 'scopeMetrics', 'metrics', metrics),
  };
  for (const [name, content] of Object.entries(files)) {
    if (content !== undefined) await writeFile(path.join(dir, name), `${content}\n${name === 'logs.json' ? suffix : ''}`);
  }
}

async function check(signals, suffix) {
  const dir = await mkdtemp(path.join(tmpdir(), 'otel-export-'));
  try {
    await writeExport(dir, signals, suffix);
    let code = 0;
    try {
      await run(process.execPath, [script, dir]);
    } catch (error) {
      code = error.code;
    }
    const report = JSON.parse(await readFile(path.join(dir, 'otel-check.json'), 'utf8'));
    return { code, report, failed: report.failures.map(failure => failure.rule).sort() };
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
}

function withChanges(change) {
  const signals = validExport();
  change(signals);
  return signals;
}

describe('check-otel-export', () => {
  it('pasa con logs de request correlacionados, logs de arranque sin trace y spans CLIENT sin log', async () => {
    const { code, report, failed } = await check(validExport());
    assert.deepEqual(failed, []);
    assert.equal(code, 0);
    assert.equal(report.result, 'PASS');
    assert.equal(report.requestLogs, 2);
    assert.equal(report.serverSpans, 2);
    assert.equal(report.metrics, 2);
  });

  it('falla con informe si una línea del export quedó cortada', async () => {
    const { code, report, failed } = await check(validExport(), '{"resourceLogs":[{"resource":');
    assert.equal(code, 1);
    assert.deepEqual(failed, ['export-json-valid']);
    assert.deepEqual(report.failures[0].samples, [{ file: 'logs.json', line: 3 }]);
  });

  it('falla si un log de request no trae traceId ni spanId', async () => {
    const { code, failed } = await check(withChanges(signals => { signals.logs.push(requestLog(undefined, undefined)); }));
    assert.equal(code, 1);
    assert.deepEqual(failed, ['request-log-trace-context']);
  });

  it('falla si un log de request trae un traceId vacío o un spanId en ceros', async () => {
    const { failed } = await check(withChanges(signals => {
      signals.logs.push(requestLog('', SERVER_A), requestLog(TRACE_A, '0000000000000000'));
    }));
    assert.deepEqual(failed, ['request-log-trace-context']);
  });

  it('falla si un log de request apunta a un span que el collector no recibió', async () => {
    const { code, report, failed } = await check(withChanges(signals => { signals.logs.push(requestLog(TRACE_A, 'ffffffffffffffff')); }));
    assert.equal(code, 1);
    assert.deepEqual(failed, ['request-log-span-exported']);
    assert.equal(report.failures[0].samples[0].spanId, 'ffffffffffffffff');
  });

  it('falla si el span existe pero pertenece a otra traza', async () => {
    const { failed } = await check(withChanges(signals => { signals.logs.push(requestLog(TRACE_B, SERVER_A)); }));
    assert.deepEqual(failed, ['request-log-span-exported']);
  });

  it('falla si un span SERVER no tiene ningún log de request', async () => {
    const { failed } = await check(withChanges(signals => { signals.spans.push(span(TRACE_B, '9999999999999999', SERVER)); }));
    assert.deepEqual(failed, ['server-span-logged']);
  });

  it('falla si el log del span SERVER perdió los atributos del request', async () => {
    const { failed } = await check(withChanges(signals => {
      signals.logs = signals.logs.map(record => ({ ...record, attributes: record.attributes.filter(({ key }) => key !== 'RequestId') }));
    }));
    assert.deepEqual(failed, ['request-logs-present', 'server-span-logged']);
  });

  it('falla si no hay ningún span SERVER', async () => {
    const { failed } = await check(withChanges(signals => { signals.spans = signals.spans.filter(({ kind }) => kind !== SERVER); }));
    assert.deepEqual(failed, ['request-log-span-exported', 'server-spans-present']);
  });

  it('falla si faltan los archivos del export', async () => {
    const { failed } = await check({});
    assert.deepEqual(failed, ['metrics-not-empty', 'request-logs-present', 'server-spans-present']);
  });

  it('falla si el collector no recibió métricas', async () => {
    const { failed } = await check(withChanges(signals => { signals.metrics = []; }));
    assert.deepEqual(failed, ['metrics-not-empty']);
  });

  it('falla si una métrica llega sin puntos o sin tipo de dato', async () => {
    const { report, failed } = await check(withChanges(signals => {
      signals.metrics.push(sum('templateapp.auth.login.attempts', []), { name: 'process.cpu.time' });
    }));
    assert.deepEqual(failed, ['metrics-not-empty']);
    assert.deepEqual(report.failures[0].samples.map(sample => sample.name), ['templateapp.auth.login.attempts', 'process.cpu.time']);
  });
});
