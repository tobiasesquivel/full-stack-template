import { expect, test, type Page, type Route } from '@playwright/test';
import {
  count,
  isOpen,
  newUser,
  openApp,
  register,
  response,
  signIn,
  waitForAuthRoom,
  type ApiCall,
} from './support/app';

test.describe.configure({ mode: 'serial' });

const user = newUser('session');
let page: Page;
let calls: ApiCall[];
const isTasks = (url: URL) => url.pathname === '/api/v1/tasks';
const isMe = (url: URL) => url.pathname === '/api/v1/users/me';

async function toProfile(): Promise<void> {
  if (new URL(page.url()).pathname === '/profile') return;
  await page.getByRole('link', { name: 'Mi perfil' }).click();
  await page.waitForURL('**/profile');
  await expect(page.getByLabel('Nombre visible')).not.toHaveValue('');
}

test.beforeAll(async ({ browser }) => {
  ({ page, calls } = await openApp(browser));
  await waitForAuthRoom();
  await register(page, user);
});

test.afterAll(async () => {
  await page.context().close();
});

test.beforeEach(async () => {
  await waitForAuthRoom();
  await signIn(page, user);
});

test.afterEach(async () => {
  await page.unrouteAll({ behavior: 'ignoreErrors' });
});

test('a 404 on the retried request keeps the session and shows the 404 message', async () => {
  await toProfile();
  let served = 0;
  await page.route(isTasks, async (route) => {
    served += 1;
    if (served === 1) await route.fulfill({ status: 401, body: '' });
    else
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Not Found', status: 404 }),
      });
  });
  const refreshes = count(calls, 'POST', '/auth/refresh');
  await page.getByRole('link', { name: 'Mis tareas' }).click();
  await expect(page.getByText('El recurso solicitado no existe o ya fue eliminado.')).toBeVisible();
  expect(new URL(page.url()).pathname).toBe('/tasks');
  expect(await isOpen(page)).toBe(true);
  expect(served).toBe(2);
  expect(count(calls, 'POST', '/auth/refresh') - refreshes).toBe(1);
});

test('two 401s at the same time share one refresh and both requests are retried', async () => {
  await toProfile();
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => (release = resolve));
  let held = 0;
  const hold = async (route: Route) => {
    held += 1;
    if (held === 2) release();
    await gate;
    await route.fulfill({ status: 401, body: '' });
  };
  await page.route(isTasks, hold, { times: 1 });
  await page.route(isMe, hold, { times: 1 });
  const refreshes = count(calls, 'POST', '/auth/refresh');
  const lists = count(calls, 'GET', '/api/v1/tasks');
  const profiles = count(calls, 'GET', '/users/me');
  await page.getByRole('link', { name: 'Mis tareas' }).click();
  await page.waitForURL('**/tasks');
  await page.getByRole('link', { name: 'Mi perfil' }).click();
  await page.waitForURL('**/profile');
  await expect.poll(() => count(calls, 'GET', '/users/me') - profiles).toBe(2);
  expect(held).toBe(2);
  expect(count(calls, 'POST', '/auth/refresh') - refreshes).toBe(1);
  expect(count(calls, 'GET', '/api/v1/tasks') - lists).toBe(2);
  expect(await isOpen(page)).toBe(true);
});

test('a failed refresh ends the session and goes to /login with returnUrl', async () => {
  await toProfile();
  await page.route(isTasks, (route) => route.fulfill({ status: 401, body: '' }), { times: 1 });
  await page.route(
    (url) => url.pathname === '/api/v1/auth/refresh',
    (route) => route.fulfill({ status: 401, body: '' }),
    { times: 1 },
  );
  await page.getByRole('link', { name: 'Mis tareas' }).click();
  await page.waitForURL(/\/login/);
  expect(new URL(page.url()).searchParams.get('returnUrl')).toBe('/tasks');
  expect(await isOpen(page)).toBe(false);
});

test('no /api/v1/auth request carries an Authorization header', async () => {
  const withHeader = calls.filter(
    (call) => call.path.startsWith('/api/v1/auth/') && call.authorization,
  );
  expect(calls.filter((call) => call.path.startsWith('/api/v1/auth/')).length).toBeGreaterThan(0);
  expect(withHeader).toEqual([]);
});

test('a session request reaches /api with the access token', async () => {
  const lists = response(page, 'GET', '/api/v1/tasks', 200);
  await page.goto('/tasks');
  await lists;
  const withToken = calls.filter((call) => call.path === '/api/v1/tasks' && call.status === 200);
  expect(withToken.at(-1)?.authorization).toBe(true);
});
