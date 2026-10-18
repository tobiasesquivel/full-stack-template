import { expect, test, type Page } from '@playwright/test';
import {
  isOpen,
  newUser,
  openApp,
  register,
  reloadWithSession,
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

test('the session lives only in an HttpOnly cookie that JavaScript cannot read', async () => {
  const session = (await page.context().cookies()).find((c) => c.name === '__Host-session');
  expect(session).toMatchObject({ httpOnly: true, secure: true, sameSite: 'Strict', path: '/' });
  const browserSide = await page.evaluate(() => ({
    cookies: document.cookie
      .split(';')
      .map((pair) => pair.split('=')[0].trim())
      .filter(Boolean),
    local: Object.keys(localStorage),
    session: Object.keys(sessionStorage),
  }));
  expect(browserSide).toEqual({ cookies: ['XSRF-TOKEN'], local: [], session: [] });
});

test('a reload keeps the session on the same page', async () => {
  await toProfile();
  await reloadWithSession(page);
  await expect(page.getByLabel('Nombre visible')).not.toHaveValue('');
  expect(new URL(page.url()).pathname).toBe('/profile');
  expect(await isOpen(page)).toBe(true);
});

test('a 404 keeps the session and shows the 404 message', async () => {
  await toProfile();
  await page.route(
    isTasks,
    (route) =>
      route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Not Found', status: 404 }),
      }),
    { times: 1 },
  );
  await page.getByRole('link', { name: 'Mis tareas' }).click();
  await expect(page.getByText('El recurso solicitado no existe o ya fue eliminado.')).toBeVisible();
  expect(new URL(page.url()).pathname).toBe('/tasks');
  expect(await isOpen(page)).toBe(true);
});

test('a 401 from the API ends the session and goes to /login with returnUrl', async () => {
  await toProfile();
  await page.route(isTasks, (route) => route.fulfill({ status: 401, body: '' }), { times: 1 });
  await page.getByRole('link', { name: 'Mis tareas' }).click();
  await page.waitForURL(/\/login/);
  expect(new URL(page.url()).searchParams.get('returnUrl')).toBe('/tasks');
  expect(await isOpen(page)).toBe(false);
});

test('closing the same session elsewhere cuts this page at its next request', async ({
  browser,
}) => {
  await toProfile();
  const cookie = (await page.context().cookies()).find((c) => c.name === '__Host-session')!;
  const elsewhere = await browser.newContext();
  try {
    await elsewhere.addCookies([cookie]);
    const other = await elsewhere.newPage();
    await other.goto(new URL('/tasks', page.url()).toString());
    await expect(other.getByRole('button', { name: 'Salir' })).toBeVisible();
    const closed = response(other, 'POST', '/auth/logout', 204);
    await other.getByRole('button', { name: 'Salir' }).click();
    await closed;
  } finally {
    await elsewhere.close();
  }
  expect(await isOpen(page)).toBe(true);
  const denied = response(page, 'GET', '/api/v1/tasks', 401);
  await page.getByRole('link', { name: 'Mis tareas' }).click();
  await denied;
  await page.waitForURL(/\/login/);
  expect(new URL(page.url()).searchParams.get('returnUrl')).toBe('/tasks');
});

test('no request carries an Authorization header', async () => {
  expect(calls.length).toBeGreaterThan(0);
  expect(calls.filter((call) => call.authorization)).toEqual([]);
});
