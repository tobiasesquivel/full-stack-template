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
const SECURITY_HEADERS = {
  'x-content-type-options': 'nosniff',
  'content-security-policy': "default-src 'none'; frame-ancestors 'none'",
  'x-frame-options': 'DENY',
  'referrer-policy': 'no-referrer',
  'cross-origin-resource-policy': 'same-origin',
  'cache-control': 'no-store',
};

function assertSecurityHeaders(response, label) {
  for (const [name, value] of Object.entries(SECURITY_HEADERS)) {
    assert.equal(response.headers.get(name), value, `${label}: ${name} debe ser ${value}`);
  }
  assert.equal(response.headers.get('server'), null, `${label}: la API no anuncia su servidor`);
  console.log(JSON.stringify({ securityHeaders: label, ok: true }));
}
const XSRF_COOKIE = 'XSRF-TOKEN';

class ApiClient {
  cookies = new Map();
  lastText = '';

  async request(method, route, body, { expected = 200, csrf = true, headers: extra = {} } = {}) {
    const url = new URL(route, base);
    const headers = { accept: 'application/json', ...extra };
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
    for (const key of ['id', 'title', 'isCompleted', 'nextCursor', 'displayName', 'timezone', 'authId', 'status', 'code']) {
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
  throw new Error('Readiness no respondió 200 en 60 segundos');
}

function compose(...args) {
  const result = spawnSync('docker', ['compose', ...args], { stdio: 'inherit', shell: false });
  assert.equal(result.status, 0, `docker compose ${args.join(' ')} debe terminar con éxito`);
}

function composeServices() {
  const result = spawnSync('docker', ['compose', 'ps', '--format', 'json'], { encoding: 'utf8', shell: false });
  assert.equal(result.status, 0, 'docker compose ps debe terminar con éxito');
  const output = result.stdout.trim();
  const services = output.startsWith('[') ? JSON.parse(output) : output.split('\n').filter(Boolean).map(line => JSON.parse(line));
  return new Map(services.map(service => [service.Service, service]));
}

async function waitGatewayUnavailable() {
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(new URL('health/live', base), { signal: AbortSignal.timeout(3000) });
      if (response.status === 503) return response;
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error('El gateway no respondió 503 en 30 segundos con la API caída');
}

const suffix = crypto.randomUUID().replaceAll('-', '').slice(0, 16);
const alice = new ApiClient();
const bob = new ApiClient();
const anonymous = new ApiClient();
await waitReady();
const live = await anonymous.request('GET', 'health/live');
assert.equal(live.headers.get('api-supported-versions'), '1.0', 'La API declara sus versiones soportadas');
const unknownVersion = await anonymous.request('GET', '../v9/health/live', undefined, { expected: 404 });
assert.equal(unknownVersion.payload.code, 'Http.NotFound', 'Una versión inexistente responde ProblemDetails con code');
const ready = await anonymous.request('GET', 'health/ready');
assert.deepEqual(ready.payload.checks, { database: 'healthy', migrations: 'healthy' },
  'Readiness informa cada dependencia sana');
const withoutSession = await anonymous.request('GET', 'tasks', undefined, { expected: 401 });
assert.equal(withoutSession.payload.code, 'Auth.SessionRequired');
const withoutCsrf = await anonymous.request('POST', 'auth/login', { userName: 'nobody', password },
  { expected: 400, csrf: false });
assert.equal(withoutCsrf.payload.code, 'Auth.AntiforgeryFailed');
assertSecurityHeaders(live, 'GET health/live 200');
assertSecurityHeaders(unknownVersion, 'GET v9 404');
assertSecurityHeaders(withoutSession, 'GET tasks 401');
assertSecurityHeaders(withoutCsrf, 'POST auth/login 400');
const aliceName = `alice${suffix}`;
const aliceUser = await alice.register(aliceName);
await bob.register(`bob${suffix}`);
const duplicate = await alice.request('POST', 'auth/register', {
  userName: aliceName, email: `${aliceName}@example.test`, password, displayName: aliceName,
}, { expected: 409 });
assert.equal(duplicate.payload.code, 'Auth.UserNameAlreadyExists');
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
const badProfile = await alice.request('PUT', 'users/me', { displayName: 'Perfil', timezone: 'No/Existe' }, { expected: 400 });
assert.equal(badProfile.payload.code, 'Validation.Failed');
const foreignUser = await bob.request('GET', `users/${aliceUser.authId}`, undefined, { expected: 403 });
assert.equal(foreignUser.payload.code, 'User.Forbidden');

const blankTitle = await alice.request('POST', 'tasks', { title: '   ' }, { expected: 400 });
assert.equal(blankTitle.payload.code, 'Validation.Failed');
assert.ok(blankTitle.payload.errors.Title, 'El 400 de validación conserva los errores por campo');
const created = (await alice.request('POST', 'tasks', { title: '  Primera tarea  ' }, { expected: 201 })).payload;
assert.equal(created.title, 'Primera tarea');
assert.equal(created.isCompleted, false);
assert.ok(created.id);
assert.equal(typeof created.version, 'number');
await alice.request('POST', 'tasks', { title: 'Segunda tarea' }, { expected: 201 });
await alice.request('POST', 'tasks', { title: 'Tercera tarea' }, { expected: 201 });
const firstPage = (await alice.request('GET', 'tasks?pageSize=2')).payload;
assert.equal(firstPage.items.length, 2);
assert.equal(typeof firstPage.nextCursor, 'string');
await alice.request('POST', 'tasks', { title: 'Creada entre páginas' }, { expected: 201 });
const secondPage = (await alice.request('GET', `tasks?pageSize=2&cursor=${encodeURIComponent(firstPage.nextCursor)}`)).payload;
assert.equal(secondPage.items.length, 1);
assert.equal(secondPage.nextCursor, null);
assert.equal(new Set([...firstPage.items, ...secondPage.items].map(item => item.id)).size, 3);
const bobPage = (await bob.request('GET', 'tasks')).payload;
assert.deepEqual(bobPage.items, []);
assert.equal(bobPage.nextCursor, null);
const brokenCursor = await alice.request('GET', 'tasks?cursor=AAAA', undefined, { expected: 400 });
assert.equal(brokenCursor.payload.code, 'Validation.Failed');
const bigPage = await alice.request('GET', 'tasks?pageSize=101', undefined, { expected: 400 });
assert.equal(bigPage.payload.code, 'Validation.Failed');
const idempotencyKey = { 'Idempotency-Key': crypto.randomUUID() };
const keyed = await alice.request('POST', 'tasks', { title: 'Con clave' }, { expected: 201, headers: idempotencyKey });
const replayed = await alice.request('POST', 'tasks', { title: 'Con clave' }, { expected: 201, headers: idempotencyKey });
assert.equal(replayed.payload.id, keyed.payload.id, 'La misma clave repite la tarea creada');
assert.equal(replayed.headers.get('idempotent-replayed'), 'true');
assert.equal(keyed.headers.get('idempotent-replayed'), null);
const reusedKey = await alice.request('POST', 'tasks', { title: 'Otro título' }, { expected: 422, headers: idempotencyKey });
assert.equal(reusedKey.payload.code, 'Idempotency.KeyReused');
for (const method of ['GET', 'PUT', 'DELETE']) {
  const foreignTask = await bob.request(method, `tasks/${created.id}`,
    method === 'PUT' ? { title: 'Ajena', isCompleted: true, version: created.version } : undefined, { expected: 403 });
  assert.equal(foreignTask.payload.code, 'Task.Forbidden');
}
const missingTask = await alice.request('GET', `tasks/${crypto.randomUUID()}`, undefined, { expected: 404 });
assert.equal(missingTask.payload.code, 'Task.NotFound');
const updated = (await alice.request('PUT', `tasks/${created.id}`, { title: 'Estado final', isCompleted: true, version: created.version })).payload;
assert.equal(updated.title, 'Estado final');
assert.equal(updated.isCompleted, true);
await alice.request('PUT', `tasks/${created.id}`, { title: 'Estado final', isCompleted: true, version: created.version });
const staleTask = await alice.request('PUT', `tasks/${created.id}`, { title: 'Primera tarea', isCompleted: false, version: created.version }, { expected: 409 });
assert.equal(staleTask.payload.code, 'Task.Conflict');
assert.equal((await alice.request('GET', `tasks/${created.id}`)).payload.isCompleted, true);

const reloaded = alice.onlySession();
assert.equal((await reloaded.request('GET', 'users/me')).payload.authId, aliceUser.authId,
  'La cookie de sesión sola restaura la sesión');
const laptop = new ApiClient();
await laptop.open(aliceName);
const closedCookie = laptop.onlySession();
await laptop.request('POST', 'auth/logout', undefined, { expected: 204 });
assert.equal(laptop.cookies.has(SESSION_COOKIE), false, 'Cerrar la sesión borra la cookie');
const closed = await closedCookie.request('GET', 'users/me', undefined, { expected: 401 });
assert.equal(closed.payload.code, 'Auth.SessionRequired', 'Una sesión cerrada responde igual que una ausente');
assert.equal((await alice.request('GET', `tasks/${created.id}`)).payload.title, 'Estado final',
  'Cerrar otra sesión del mismo usuario no corta esta');

if (process.argv.includes('--compose')) {
  const services = composeServices();
  for (const service of ['db', 'prometheus', 'api', 'gateway']) {
    assert.equal(services.get(service)?.Health, 'healthy', `${service} responde su healthcheck`);
  }
  assert.ok(!(services.get('api').Publishers ?? []).some(publisher => publisher.PublishedPort),
    'La API no publica puertos: todo entra por el gateway');
  compose('stop', 'api');
  try {
    const unavailable = await waitGatewayUnavailable();
    assertSecurityHeaders(unavailable, 'gateway 503 con la API caída');
    const gatewayReady = await fetch(new URL('/health/ready', base), { signal: AbortSignal.timeout(3000) });
    assert.equal(gatewayReady.status, 200, 'El gateway sigue listo aunque la API esté caída');
  } finally {
    compose('start', 'api');
    await waitReady();
  }
  compose('stop', 'db');
  try {
    await anonymous.request('GET', 'health/live');
    const notReady = await anonymous.request('GET', 'health/ready', undefined, { expected: 503 });
    assert.equal(notReady.payload.status, 'not-ready');
    assert.equal(notReady.payload.checks.database, 'unhealthy', 'Readiness nombra a la base como la dependencia caída');
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
const revokedSession = await revoked.request('GET', 'users/me', undefined, { expected: 401 });
assert.equal(revokedSession.payload.code, 'Auth.SessionRequired');
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
      const metrics = signals.metrics.split('\n').filter(Boolean).flatMap(line => JSON.parse(line).resourceMetrics ?? []);
      const metricNames = metrics.flatMap(resource => (resource.scopeMetrics ?? []).flatMap(scope => (scope.metrics ?? []).map(metric => metric.name)));
      assert.ok(metricNames.includes('http.server.request.duration'), 'Debe exportarse la duración HTTP real');
      const scopes = metrics.flatMap(resource => resource.scopeMetrics ?? []);
      const points = (meter, name, kind) => scopes
        .filter(scope => scope.scope?.name === meter)
        .flatMap(scope => scope.metrics ?? [])
        .filter(metric => metric.name === name && metric[kind])
        .flatMap(metric => metric[kind].dataPoints ?? []);
      assert.ok(points('TemplateApp.Tasks', 'templateapp.tasks.created', 'sum').some(point => Number(point.asInt) >= 1),
        'Debe exportarse el contador de tareas creadas del Meter TemplateApp.Tasks');
      assert.ok(points('TemplateApp.Tasks', 'templateapp.tasks.completion.duration', 'histogram').some(point => Number(point.count) >= 1),
        'Debe exportarse el histograma del tiempo hasta completar una tarea');
      assert.ok(points('TemplateApp.Auth', 'templateapp.auth.login.attempts', 'sum').some(point => point.attributes?.some(
        attribute => attribute.key === 'templateapp.auth.login.outcome' && attribute.value?.stringValue === 'success')),
        'Debe exportarse el contador de logins con outcome=success del Meter TemplateApp.Auth');
      observed = { httpDurationMetric: true, businessMetrics: true };
      break;
    } catch (error) {
      if (Date.now() + 500 >= deadline) throw error;
      await new Promise(resolve => setTimeout(resolve, 500));
    }
  }
  assert.ok(observed, 'El collector debe exportar las métricas esperadas sin secretos');
  evidence.push({ otlp: observed });
}

console.log(JSON.stringify({ result: 'PASS', scenarios: evidence }, null, 2));
