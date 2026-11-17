import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const api = new URL(process.env.API_BASE_URL ?? 'http://localhost:8080/api/v1/');
if (!api.pathname.endsWith('/')) api.pathname += '/';
const prometheus = new URL(process.env.PROMETHEUS_URL ?? 'http://localhost:9090/');
const outDir = process.argv[2];
const RUNBOOK_URL_PREFIX = 'https://github.com/tobiasesquivel/full-stack-template/blob/main/docs/operacion/runbooks/';
const SLIS = ['job:slo_api_availability_errors:ratio_rate5m', 'job:slo_api_latency_errors:ratio_rate5m'];
const ALERTS = ['ApiAvailabilityBudgetBurn', 'ApiLatencyBudgetBurn'];
const FIRE_TIMEOUT_MS = 300_000;
const report = { startedAt: new Date().toISOString(), slis: {}, phases: [] };

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function compose(...args) {
  const result = spawnSync('docker', ['compose', ...args], { stdio: 'inherit', shell: false });
  assert.equal(result.status, 0, `docker compose ${args.join(' ')} debe terminar con éxito`);
}

async function prom(route, params = {}) {
  const url = new URL(`api/v1/${route}`, prometheus);
  for (const [key, value] of Object.entries(params)) url.searchParams.set(key, value);
  const response = await fetch(url, { signal: AbortSignal.timeout(10_000) });
  const body = await response.json();
  assert.equal(body.status, 'success', `Prometheus ${route}: ${JSON.stringify(body)}`);
  return body.data;
}

async function until(label, probe, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await probe();
      if (last) return last;
    } catch (error) {
      last = error;
    }
    await sleep(2_000);
  }
  throw new Error(`${label}: no ocurrió en ${timeoutMs / 1000} s (último resultado: ${last instanceof Error ? last.message : JSON.stringify(last)})`);
}

class Session {
  cookies = new Map();

  async send(method, route, body, timeoutMs = 15_000) {
    const headers = { accept: 'application/json' };
    if (this.cookies.size) headers.cookie = [...this.cookies].map(([name, value]) => `${name}=${value}`).join('; ');
    const xsrf = this.cookies.get('XSRF-TOKEN');
    if (method !== 'GET' && xsrf) headers['X-XSRF-TOKEN'] = decodeURIComponent(xsrf);
    if (body !== undefined) headers['content-type'] = 'application/json';
    const response = await fetch(new URL(route, api), {
      method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(timeoutMs),
    });
    for (const raw of response.headers.getSetCookie()) {
      const [pair] = raw.split(';');
      const equals = pair.indexOf('=');
      this.cookies.set(pair.slice(0, equals).trim(), pair.slice(equals + 1).trim());
    }
    await response.arrayBuffer();
    return response.status;
  }

  async register() {
    assert.equal(await this.send('GET', 'auth/csrf'), 200);
    const userName = `slo${crypto.randomUUID().replaceAll('-', '').slice(0, 16)}`;
    const password = `Slo!${crypto.randomUUID()}Aa9`;
    assert.equal(await this.send('POST', 'auth/register', { userName, email: `${userName}@example.test`, password, displayName: userName }), 201);
  }
}

async function firing() {
  const { alerts } = await prom('alerts');
  return alerts.filter(alert => alert.state === 'firing' && ALERTS.includes(alert.labels.alertname));
}

async function waitReady() {
  await until('readiness 200', async () => (await fetch(new URL('health/ready', api), { signal: AbortSignal.timeout(5_000) })).status === 200, 120_000);
}

async function checkRules() {
  const { groups } = await until('reglas de SLO cargadas', async () => {
    const data = await prom('rules');
    return data.groups.some(group => group.name === 'slo-api-alerts') ? data : null;
  }, 60_000);
  const rules = groups.flatMap(group => group.rules);
  const unhealthy = rules.filter(rule => rule.health !== 'ok');
  assert.deepEqual(unhealthy.map(rule => rule.name), [], 'Toda regla de SLO se evalúa sin error');
  const alerting = rules.filter(rule => rule.type === 'alerting');
  assert.deepEqual([...new Set(alerting.map(rule => rule.name))].sort(), [...ALERTS].sort(), 'Prometheus carga las dos alertas de SLO');
  for (const rule of alerting) {
    assert.equal(rule.annotations.runbook_url, `${RUNBOOK_URL_PREFIX}${rule.name}.md`, `${rule.name} enlaza su runbook`);
  }
  report.rules = { recording: rules.length - alerting.length, alerting: alerting.length };
}

async function querySlis() {
  for (const sli of SLIS) {
    const sample = await until(`SLI ${sli} con un número`, async () => {
      const [first] = (await prom('query', { query: sli })).result;
      return first && Number.isFinite(Number(first.value[1])) ? first : null;
    }, 90_000);
    const value = Number(sample.value[1]);
    assert.ok(value >= 0 && value <= 1, `${sli} es una proporción: ${value}`);
    report.slis[sli] = { labels: sample.metric, value };
  }
  const raw = await prom('query', { query: 'sum by (job) (http_server_request_duration_seconds_count{job="templateapp-api"})' });
  assert.ok(Number(raw.result[0]?.value[1]) > 0, 'La métrica cruda http_server_request_duration_seconds_count llega a Prometheus');
  report.slis.rawRequests = Number(raw.result[0].value[1]);
}

async function phase({ name, alert, inject }) {
  const before = await firing();
  assert.deepEqual(before.filter(entry => entry.labels.alertname === alert), [], `${alert} no está firing antes de: ${name}`);
  const began = Date.now();
  const statuses = {};
  const count = status => { statuses[status] = (statuses[status] ?? 0) + 1; };
  const stop = { done: false };
  const load = inject(stop, count);
  let fired;
  try {
    fired = await until(`${alert} firing con severity page y ticket`, async () => {
      const active = (await firing()).filter(entry => entry.labels.alertname === alert);
      const severities = new Set(active.map(entry => entry.labels.severity));
      return severities.has('page') && severities.has('ticket') ? active : null;
    }, FIRE_TIMEOUT_MS);
  } finally {
    stop.done = true;
    await load;
    await waitReady();
  }
  for (const entry of fired) {
    assert.equal(entry.annotations.runbook_url, `${RUNBOOK_URL_PREFIX}${alert}.md`, `${alert} llega con su runbook`);
  }
  const others = (await firing()).filter(entry => entry.labels.alertname !== alert).map(entry => entry.labels.alertname);
  report.phases.push({
    name, alert, secondsToFire: Math.round((Date.now() - began) / 1000), statuses,
    fired: fired.map(entry => ({ labels: entry.labels, annotations: entry.annotations, activeAt: entry.activeAt, value: entry.value })),
    otherAlertsFiring: [...new Set(others)],
  });
  return others;
}

async function slowButSuccessful(stop, count) {
  while (!stop.done) {
    compose('pause', 'db');
    let requests;
    try {
      requests = Array.from({ length: 10 }, () => user.send('GET', 'tasks', undefined, 60_000).catch(error => error.name));
      await sleep(3_000);
    } finally {
      compose('unpause', 'db');
    }
    for (const status of await Promise.all(requests)) count(status);
  }
}

async function databaseDown(stop, count) {
  compose('stop', 'db');
  try {
    await Promise.all(Array.from({ length: 8 }, async () => {
      while (!stop.done) count(await user.send('GET', 'tasks', undefined, 60_000).catch(error => error.name));
    }));
  } finally {
    compose('start', 'db');
  }
}

function writeReport() {
  report.endedAt = new Date().toISOString();
  if (!outDir) return;
  mkdirSync(outDir, { recursive: true });
  writeFileSync(path.join(outDir, 'slo-check.json'), `${JSON.stringify(report, null, 2)}\n`);
}

const user = new Session();
try {
  await until('Prometheus listo', async () => (await fetch(new URL('-/ready', prometheus), { signal: AbortSignal.timeout(5_000) })).ok, 60_000);
  await waitReady();
  await checkRules();
  await user.register();
  const healthy = { done: false };
  const traffic = (async () => {
    while (!healthy.done) {
      assert.equal(await user.send('GET', 'tasks'), 200);
      await sleep(250);
    }
  })();
  try {
    await querySlis();
  } finally {
    healthy.done = true;
    await traffic;
  }
  assert.deepEqual((await firing()).map(entry => entry.labels.alertname), [], 'Con tráfico sano no hay alertas de SLO');

  const othersDuringLatency = await phase({ name: 'base congelada 3 s: requests 200 lentos', alert: 'ApiLatencyBudgetBurn', inject: slowButSuccessful });
  assert.ok(!othersDuringLatency.includes('ApiAvailabilityBudgetBurn'), 'Los requests lentos pero exitosos no gastan el presupuesto de disponibilidad');
  await phase({ name: 'base parada: 5xx', alert: 'ApiAvailabilityBudgetBurn', inject: databaseDown });
  report.result = 'PASS';
} catch (error) {
  report.result = 'FAIL';
  report.error = error.message;
  throw error;
} finally {
  writeReport();
  console.log(JSON.stringify(report, null, 2));
}
