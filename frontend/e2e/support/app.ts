import {
  expect,
  type Browser,
  type BrowserContext,
  type Page,
  type Response,
} from '@playwright/test';

const AUTH_WINDOW_MS = 61_000;
const AUTH_USED_BEFORE_A_TEST = 6;
const authRequests: number[] = [];
const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

export interface ApiCall {
  method: string;
  path: string;
  status: number;
  authorization: boolean;
}

export function newUser(tag: string): { userName: string; password: string } {
  const suffix = `${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;
  return { userName: `e2e${tag}${suffix}`, password: `Verify!${suffix}Aa9xyz` };
}

export function track(page: Page): ApiCall[] {
  const calls: ApiCall[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/auth/')) authRequests.push(Date.now());
  });
  page.on('response', (response) => {
    const url = new URL(response.url());
    if (!url.pathname.startsWith('/api/v1')) return;
    calls.push({
      method: response.request().method(),
      path: url.pathname,
      status: response.status(),
      authorization: Boolean(response.request().headers()['authorization']),
    });
  });
  return calls;
}

export async function openApp(
  browser: Browser,
): Promise<{ context: BrowserContext; page: Page; calls: ApiCall[] }> {
  const context = await browser.newContext();
  const page = await context.newPage();
  return { context, page, calls: track(page) };
}

export async function waitForAuthRoom(): Promise<void> {
  for (;;) {
    const now = Date.now();
    while (authRequests.length > 0 && now - authRequests[0] > AUTH_WINDOW_MS) authRequests.shift();
    if (authRequests.length <= AUTH_USED_BEFORE_A_TEST) return;
    await sleep(authRequests[0] + AUTH_WINDOW_MS - now + 250);
  }
}

export function count(calls: ApiCall[], method: string, path: string): number {
  return calls.filter((call) => call.method === method && call.path.includes(path)).length;
}

export function response(
  page: Page,
  method: string,
  path: string,
  status: number,
): Promise<Response> {
  const waiting = page.waitForResponse(
    (candidate) =>
      candidate.request().method() === method &&
      new URL(candidate.url()).pathname.includes(path) &&
      candidate.status() === status,
  );
  waiting.catch(() => undefined);
  return waiting;
}

export function listPage(page: Page, number: number): Promise<Response> {
  const waiting = page.waitForResponse(
    (candidate) =>
      candidate.request().method() === 'GET' &&
      new URL(candidate.url()).pathname === '/api/v1/tasks' &&
      new URL(candidate.url()).searchParams.get('page') === String(number) &&
      candidate.status() === 200,
  );
  waiting.catch(() => undefined);
  return waiting;
}

export async function register(
  page: Page,
  user: { userName: string; password: string },
): Promise<void> {
  await page.goto('/register');
  await page.getByLabel('Usuario').fill(user.userName);
  await page.getByLabel('Correo').fill(`${user.userName}@example.test`);
  await page.getByLabel('Contraseña').fill(user.password);
  const created = response(page, 'POST', '/auth/register', 201);
  await page.locator('form').getByRole('button', { name: 'Crear cuenta' }).click();
  await created;
  await page.waitForURL('**/tasks');
  await expect(page.getByRole('heading', { name: 'Mis tareas' })).toBeVisible();
}

export async function isOpen(page: Page): Promise<boolean> {
  return (await page.getByRole('button', { name: 'Salir' }).count()) > 0;
}

export async function signIn(
  page: Page,
  user: { userName: string; password: string },
): Promise<void> {
  if (await isOpen(page)) return;
  if (!new URL(page.url()).pathname.startsWith('/login')) await page.goto('/login');
  await page.getByLabel('Usuario').fill(user.userName);
  await page.getByLabel('Contraseña').fill(user.password);
  const opened = response(page, 'POST', '/auth/login', 200);
  await page.locator('form').getByRole('button', { name: 'Entrar' }).click();
  await opened;
  await expect(page.getByRole('button', { name: 'Salir' })).toBeVisible();
}

export async function signOut(page: Page): Promise<void> {
  const closed = response(page, 'POST', '/auth/logout', 204);
  await page.getByRole('button', { name: 'Salir' }).click();
  await closed;
  await page.waitForURL(/\/login/);
}

export async function reloadWithSession(page: Page): Promise<void> {
  const restored = response(page, 'GET', '/users/me', 200);
  await page.reload();
  await restored;
}
