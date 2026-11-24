import { createReadStream } from 'node:fs';
import { writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createInterface } from 'node:readline';

const SPAN_KIND_SERVER = 2;
const METRIC_DATA = ['sum', 'gauge', 'histogram', 'exponentialHistogram', 'summary'];
const SAMPLE_LIMIT = 5;
const REPORT_FILE = 'otel-check.json';

async function readSignal(dir, file, resourceKey, scopeKey, itemKey, brokenLines, visit) {
  const stream = createReadStream(path.join(dir, file), { encoding: 'utf8' });
  const opened = await new Promise((resolve, reject) => {
    stream.once('ready', () => resolve(true));
    stream.once('error', error => (error.code === 'ENOENT' ? resolve(false) : reject(error)));
  });
  if (!opened) return;
  let index = 0;
  for await (const line of createInterface({ input: stream, crlfDelay: Infinity })) {
    index += 1;
    if (!line.trim()) continue;
    let request;
    try {
      request = JSON.parse(line);
    } catch {
      brokenLines.push({ file, line: index });
      continue;
    }
    for (const resource of request[resourceKey] ?? []) {
      for (const scope of resource[scopeKey] ?? []) {
        for (const item of scope[itemKey] ?? []) visit(item);
      }
    }
  }
}

const validId = (value, length) => typeof value === 'string'
  && new RegExp(`^[0-9a-f]{${length}}$`, 'i').test(value)
  && /[1-9a-f]/i.test(value);

const isRequestLog = record => (record.attributes ?? []).some(attribute => attribute.key === 'RequestId');

const describeLog = record => ({
  requestPath: record.attributes?.find(attribute => attribute.key === 'RequestPath')?.value?.stringValue,
  traceId: record.traceId,
  spanId: record.spanId,
});

const RULES = [
  {
    rule: 'export-json-valid',
    message: 'Cada línea del export debe ser un JSON completo',
    violations: ({ brokenLines }) => brokenLines,
  },
  {
    rule: 'request-logs-present',
    message: 'El collector debe recibir logs de request (con RequestId)',
    violations: ({ requestLogs }) => requestLogs.length ? [] : [{ requestLogs: 0 }],
  },
  {
    rule: 'request-log-trace-context',
    message: 'Cada log de request debe llevar traceId y spanId válidos',
    violations: ({ requestLogs }) => requestLogs
      .filter(record => !validId(record.traceId, 32) || !validId(record.spanId, 16))
      .map(describeLog),
  },
  {
    rule: 'request-log-span-exported',
    message: 'Cada log de request debe apuntar a un span exportado de la misma traza',
    violations: ({ requestLogs, traceBySpan }) => requestLogs
      .filter(record => validId(record.traceId, 32) && validId(record.spanId, 16))
      .filter(record => traceBySpan.get(record.spanId.toLowerCase()) !== record.traceId.toLowerCase())
      .map(describeLog),
  },
  {
    rule: 'server-spans-present',
    message: 'El collector debe recibir spans SERVER',
    violations: ({ serverSpans }) => serverSpans.length ? [] : [{ serverSpans: 0 }],
  },
  {
    rule: 'server-span-logged',
    message: 'Cada span SERVER debe tener al menos un log de request',
    violations: ({ serverSpans, loggedSpans }) => serverSpans
      .filter(span => !loggedSpans.has(`${span.traceId}/${span.spanId}`.toLowerCase()))
      .map(({ name, traceId, spanId }) => ({ name, traceId, spanId })),
  },
  {
    rule: 'metrics-not-empty',
    message: 'El collector debe recibir métricas, y cada una con al menos un punto',
    violations: ({ metrics }) => metrics.length
      ? metrics.filter(metric => !METRIC_DATA.some(kind => metric[kind]?.dataPoints?.length)).map(({ name }) => ({ name }))
      : [{ metrics: 0 }],
  },
];

async function checkOtelExport(dir) {
  const brokenLines = [];
  const requestLogs = [];
  const serverSpans = [];
  const traceBySpan = new Map();
  const metrics = [];
  let logRecords = 0;
  let spans = 0;
  await readSignal(dir, 'logs.json', 'resourceLogs', 'scopeLogs', 'logRecords', brokenLines, record => {
    logRecords += 1;
    if (isRequestLog(record)) requestLogs.push({ traceId: record.traceId, spanId: record.spanId, attributes: record.attributes });
  });
  await readSignal(dir, 'traces.json', 'resourceSpans', 'scopeSpans', 'spans', brokenLines, span => {
    spans += 1;
    traceBySpan.set(span.spanId?.toLowerCase(), span.traceId?.toLowerCase());
    if (span.kind === SPAN_KIND_SERVER) serverSpans.push({ name: span.name, traceId: span.traceId, spanId: span.spanId });
  });
  await readSignal(dir, 'metrics.json', 'resourceMetrics', 'scopeMetrics', 'metrics', brokenLines, metric => metrics.push(metric));
  const signals = {
    brokenLines,
    requestLogs,
    metrics,
    serverSpans,
    traceBySpan,
    loggedSpans: new Set(requestLogs.map(record => `${record.traceId}/${record.spanId}`.toLowerCase())),
  };
  const failures = RULES.flatMap(({ rule, message, violations }) => {
    const found = violations(signals);
    return found.length ? [{ rule, message, count: found.length, samples: found.slice(0, SAMPLE_LIMIT) }] : [];
  });
  return {
    result: failures.length ? 'FAIL' : 'PASS',
    logRecords,
    requestLogs: requestLogs.length,
    spans,
    serverSpans: serverSpans.length,
    metrics: metrics.length,
    rules: RULES.map(({ rule }) => rule),
    failures,
  };
}

const [dir] = process.argv.slice(2);
if (!dir) {
  console.error('usage: node check-otel-export.mjs <otel-export-dir>');
  process.exit(2);
}
const report = await checkOtelExport(dir);
await writeFile(path.join(dir, REPORT_FILE), `${JSON.stringify(report, null, 2)}\n`);
console.log(JSON.stringify(report, null, 2));
process.exitCode = report.failures.length ? 1 : 0;
