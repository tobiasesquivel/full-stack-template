import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';

const base = new URL(process.env.API_BASE_URL ?? 'http://localhost:8080/api/v1/');
if (!base.pathname.endsWith('/')) base.pathname += '/';
const evidence = [];
const secrets = [];
const password = `Smoke!${crypto.randomUUID()}Aa9`;
secrets.push(password);

const SESSION_COOKIE = '__Host-session';
const XSRF_COOKIE = 'XSRF-TOKEN';

class ApiClient {
  cookies = new Map();
  lastText = '';

  async request(method, route, body, { expected = 200, csrf = true } = {}) {
    const url = new URL(route, base);
    const headers = { accept: 'application/json' };
    const cookies = [...this.cookies.values()].filter(cookie => url.pathname.startsWith(cookie.path));
    if (cookies.length) headers.cookie = cookies.map(cookie => `${cookie.name}=${cookie.value}`).join('; ');
    if (csrf && method !== 'GET') {
      const token = this.cookies.get('XSRF-TOKEN')?.value;
      if (token) headers['X-XSRF-TOKEN'] = decodeURIComponent(token);
    }
    if (body !== undefined) headers['content-type'] = 'application/json';
    const response = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(15000),
      redirect: 'manual',
    });
    const setCookies = response.headers.getSetCookie();
    for (const raw of setCookies) {
      const [pair, ...attributes] = raw.split(';').map(value => value.trim());
      const equals = pair.indexOf('=');
      const name = pair.slice(0, equals);
      const value = pair.slice(equals + 1);
      const path = attributes.find(value => value.toLowerCase().startsWith('path='))?.slice(5) ?? '/';
      const expired = attributes.some(value => value.toLowerCase() === 'max-age=0')
        || attributes.some(value => value.toLowerCase().startsWith('expires=') && Date.parse(value.slice(8)) <= Date.now());
      if (expired || !value) this.cookies.delete(name);
      else {
        const httpOnly = attributes.some(value => value.toLowerCase() === 'httponly');
        const secure = attributes.some(value => value.toLowerCase() === 'secure');
        const sameSite = attributes.find(value => value.toLowerCase().startsWith('samesite='))?.slice(9).toLowerCase();
        this.cookies.set(name, { name, value, path, httpOnly, secure, sameSite });
        if (httpOnly) secrets.push(value);
      }
    }
    const text = await response.text();
    this.lastText = text;
    let payload;
    try {
      payload = text ? JSON.parse(text) : undefined;
    } catch {
      console.log(JSON.stringify({ method, route, status: response.status, responseFormat: response.headers.get('content-type') }));
      throw new Error(`${method} ${route} devolvió una respuesta no JSON con status ${response.status}`);
    }
    const observed = {};
    for (const key of ['id', 'title', 'isCompleted', 'totalCount', 'page', 'pageSize', 'displayName', 'timezone', 'authId', 'status', 'code']) {
      if (payload && Object.hasOwn(payload, key)) observed[key] = payload[key];
    }
    if (Array.isArray(payload?.items)) {
      observed.items = payload.items.map(({ id, title, isCompleted }) => ({ id, title, isCompleted }));
    }
    const record = { method, route, status: response.status, observed };
    evidence.push(record);
    console.log(JSON.stringify(record));
    assert.equal(response.status, expected, `${method} ${route} devolvió ${response.status}, se esperaba ${expected}`);
    return { payload, setCookies, headers: response.headers };
  }

  async csrf() {
    await this.request('GET', 'auth/csrf');
    assert.ok(this.cookies.get(XSRF_COOKIE)?.value, 'El endpoint CSRF debe entregar XSRF-TOKEN');
  }

  async register(userName) {
    await this.csrf();
    const { payload } = await this.request('POST', 'auth/register', {
      userName, email: `${userName}@example.test`, password, displayName: userName,
    }, { expected: 201 });
    this.acceptSession();
    assert.equal(payload.userName, userName);
    assert.equal(payload.timezone, 'UTC');
    return payload;
  }

  async open(userName) {
    await this.csrf();
    const { payload } = await this.request('POST', 'auth/login', { userName, password });
    this.acceptSession();
    assert.equal(payload.userName, userName);
    return payload;
  }

  acceptSession() {
    assert.ok(!/token/i.test(this.lastText), 'Ninguna respuesta de sesión lleva un token en el body');
    const session = this.cookies.get(SESSION_COOKIE);
    assert.ok(session, 'La sesión debe usar la cookie __Host-session');
    assert.equal(session.httpOnly, true, 'La cookie de sesión debe ser HttpOnly');
    assert.equal(session.secure, true, 'La cookie de sesión debe ser Secure');
    assert.equal(session.sameSite, 'strict', 'La cookie de sesión debe ser SameSite=Strict');
    assert.equal(session.path, '/', 'El prefijo __Host- exige Path=/');
    const readable = [...this.cookies.values()].filter(cookie => !cookie.httpOnly).map(cookie => cookie.name);
    assert.deepEqual(readable, [XSRF_COOKIE], 'La única cookie legible por JavaScript es XSRF-TOKEN');
  }

  onlySession() {
    const copy = new ApiClient();
    copy.cookies.set(SESSION_COOKIE, { ...this.cookies.get(SESSION_COOKIE) });
    return copy;
  }
}

async function waitReady() {
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(new URL('health/ready', base), { signal: AbortSignal.timeout(3000) });
      if (response.status === 200) return;
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error('Readiness no respondió 200 después del reinicio');
}

function compose(...args) {
  const result = spawnSync('docker', ['compose', ...args], { stdio: 'inherit', shell: false });
  assert.equal(result.status, 0, `docker compose ${args.join(' ')} debe terminar con éxito`);
}

const suffix = crypto.randomUUID().replaceAll('-', '').slice(0, 16);
const alice = new ApiClient();
const bob = new ApiClient();
const anonymous = new ApiClient();
await anonymous.request('GET', 'health/live');
await anonymous.request('GET', 'health/ready');
await anonymous.request('GET', 'tasks', undefined, { expected: 401 });
const withoutCsrf = await anonymous.request('POST', 'auth/login', { userName: 'nobody', password },
  { expected: 400, csrf: false });
assert.equal(withoutCsrf.payload.code, 'Auth.AntiforgeryFailed');
const aliceName = `alice${suffix}`;
const aliceUser = await alice.register(aliceName);
await bob.register(`bob${suffix}`);
await alice.request('POST', 'auth/register', {
  userName: aliceName, email: `${aliceName}@example.test`, password, displayName: aliceName,
}, { expected: 409 });
const writeWithoutCsrf = await alice.request('POST', 'tasks', { title: 'Sin token antiforgery' },
  { expected: 400, csrf: false });
assert.equal(writeWithoutCsrf.payload.code, 'Auth.AntiforgeryFailed', 'Toda escritura de la API exige antiforgery');

const profile = (await alice.request('GET', 'users/me')).payload;
assert.equal(profile.authId, aliceUser.authId);
assert.equal(profile.displayName, aliceName);
assert.deepEqual([...profile.permissions].sort(), ['Permissions.Tasks.Create']);
const changedProfile = (await alice.request('PUT', 'users/me', {
  displayName: 'Perfil de smoke', timezone: 'America/Argentina/Buenos_Aires',
})).payload;
assert.equal(changedProfile.displayName, 'Perfil de smoke');
assert.equal(changedProfile.timezone, 'America/Argentina/Buenos_Aires');
await alice.request('PUT', 'users/me', { displayName: 'Perfil', timezone: 'No/Existe' }, { expected: 400 });
await bob.request('GET', `users/${aliceUser.authId}`, undefined, { expected: 403 });

await alice.request('POST', 'tasks', { title: '   ' }, { expected: 400 });
const created = (await alice.request('POST', 'tasks', { title: '  Primera tarea  ' }, { expected: 201 })).payload;
assert.equal(created.title, 'Primera tarea');
assert.equal(created.isCompleted, false);
assert.ok(created.id);
await alice.request('POST', 'tasks', { title: 'Segunda tarea' }, { expected: 201 });
await alice.request('POST', 'tasks', { title: 'Tercera tarea' }, { expected: 201 });
const firstPage = (await alice.request('GET', 'tasks?page=1&pageSize=2')).payload;
assert.equal(firstPage.totalCount, 3);
assert.equal(firstPage.page, 1);
assert.equal(firstPage.pageSize, 2);
assert.equal(firstPage.items.length, 2);
const secondPage = (await alice.request('GET', 'tasks?page=2&pageSize=2')).payload;
assert.equal(secondPage.items.length, 1);
assert.equal(new Set([...firstPage.items, ...secondPage.items].map(item => item.id)).size, 3);
const bobPage = (await bob.request('GET', 'tasks')).payload;
assert.deepEqual(bobPage.items, []);
assert.equal(bobPage.totalCount, 0);
await alice.request('GET', 'tasks?pageSize=101', undefined, { expected: 400 });
for (const method of ['GET', 'PUT', 'DELETE']) {
  await bob.request(method, `tasks/${created.id}`, method === 'PUT' ? { title: 'Ajena', isCompleted: true } : undefined,
    { expected: 403 });
}
await alice.request('GET', `tasks/${crypto.randomUUID()}`, undefined, { expected: 404 });
const updated = (await alice.request('PUT', `tasks/${created.id}`, { title: 'Estado final', isCompleted: true })).payload;
assert.equal(updated.title, 'Estado final');
assert.equal(updated.isCompleted, true);
await alice.request('PUT', `tasks/${created.id}`, { title: 'Estado final', isCompleted: true });
assert.equal((await alice.request('GET', `tasks/${created.id}`)).payload.isCompleted, true);

const reloaded = alice.onlySession();
assert.equal((await reloaded.request('GET', 'users/me')).payload.authId, aliceUser.authId,
  'La cookie de sesión sola restaura la sesión');
const laptop = new ApiClient();
await laptop.open(aliceName);
const closedCookie = laptop.onlySession();
await laptop.request('POST', 'auth/logout', undefined, { expected: 204 });
assert.equal(laptop.cookies.has(SESSION_COOKIE), false, 'Cerrar la sesión borra la cookie');
await closedCookie.request('GET', 'users/me', undefined, { expected: 401 });
assert.equal((await alice.request('GET', `tasks/${created.id}`)).payload.title, 'Estado final',
  'Cerrar otra sesión del mismo usuario no corta esta');

if (process.argv.includes('--compose')) {
  compose('stop', 'db');
  try {
    await anonymous.request('GET', 'health/live');
    await anonymous.request('GET', 'health/ready', undefined, { expected: 503 });
  } finally {
    compose('start', 'db');
    await waitReady();
  }
  compose('up', '--detach', '--no-deps', '--force-recreate', 'api');
  await waitReady();
  assert.equal((await alice.request('GET', `tasks/${created.id}`)).payload.title, 'Estado final',
    'Los datos y la sesión deben sobrevivir a reiniciar la DB y a recrear el contenedor de la API');
}

await alice.request('DELETE', `tasks/${created.id}`, undefined, { expected: 204 });
await alice.request('GET', `tasks/${created.id}`, undefined, { expected: 404 });
const revoked = alice.onlySession();
await alice.request('POST', 'auth/logout', undefined, { expected: 204 });
await revoked.request('GET', 'users/me', undefined, { expected: 401 });
await alice.request('GET', 'users/me', undefined, { expected: 401 });
await alice.open(aliceName);
await alice.request('DELETE', 'users/me', undefined, { expected: 204 });
await alice.request('GET', 'users/me', undefined, { expected: 401 });
await bob.request('DELETE', 'users/me', undefined, { expected: 204 });

if (process.env.OTEL_EXPORT_DIR) {
  const deadline = Date.now() + 30000;
  let observed;
  while (Date.now() < deadline) {
    try {
      const signals = {};
      for (const signal of ['logs', 'metrics', 'traces']) {
        signals[signal] = await readFile(`${process.env.OTEL_EXPORT_DIR}/${signal}.json`, 'utf8');
      }
      for (const secret of secrets) {
        for (const content of Object.values(signals)) assert.ok(!content.includes(secret), 'OTLP no debe exportar secretos');
      }
      const logs = signals.logs.split('\n').filter(Boolean).flatMap(line => JSON.parse(line).resourceLogs ?? []);
      const spans = signals.traces.split('\n').filter(Boolean).flatMap(line => JSON.parse(line).resourceSpans ?? []);
      const metrics = signals.metrics.split('\n').filter(Boolean).flatMap(line => JSON.parse(line).resourceMetrics ?? []);
      const records = logs.flatMap(resource => (resource.scopeLogs ?? []).flatMap(scope => scope.logRecords ?? []));
      const traceIds = new Set(spans.flatMap(resource => (resource.scopeSpans ?? []).flatMap(scope => (scope.spans ?? []).map(span => span.traceId))));
      assert.ok(records.some(record => record.traceId && traceIds.has(record.traceId)), 'Debe existir correlación real de log y span');
      const metricNames = metrics.flatMap(resource => (resource.scopeMetrics ?? []).flatMap(scope => (scope.metrics ?? []).map(metric => metric.name)));
      assert.ok(metricNames.includes('http.server.request.duration'), 'Debe exportarse la duración HTTP real');
      observed = { correlatedLogs: true, httpDurationMetric: true };
      break;
    } catch (error) {
      if (Date.now() + 500 >= deadline) throw error;
      await new Promise(resolve => setTimeout(resolve, 500));
    }
  }
  assert.ok(observed, 'El collector debe exportar las tres señales');
  evidence.push({ otlp: observed });
}

console.log(JSON.stringify({ result: 'PASS', scenarios: evidence }, null, 2));
