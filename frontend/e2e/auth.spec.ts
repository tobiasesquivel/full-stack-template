import { expect, test, type Page } from '@playwright/test';
import {
  count,
  newUser,
  openApp,
  register,
  reloadWithSession,
  response,
  signIn,
  signOut,
  waitForAuthRoom,
  type ApiCall,
} from './support/app';

test.describe.configure({ mode: 'serial' });

const user = newUser('auth');
let page: Page;
let calls: ApiCall[];

test.beforeAll(async ({ browser }) => {
  ({ page, calls } = await openApp(browser));
});

test.afterAll(async () => {
  await page.context().close();
});

test.beforeEach(async () => {
  await waitForAuthRoom();
});

test('register rejects empty and malformed fields without a request', async () => {
  await page.goto('/register');
  await expect(page.getByRole('heading', { name: 'Crear cuenta' })).toBeVisible();
  const before = count(calls, 'POST', '/auth/register');
  const submit = page.locator('form').getByRole('button', { name: 'Crear cuenta' });
  await submit.click();
  await expect(page.getByText('El usuario es obligatorio (máximo 50 caracteres).')).toBeVisible();
  await expect(page.getByText('Escribe un correo válido.')).toBeVisible();
  await expect(page.getByText('La contraseña necesita entre 6 y 128 caracteres.')).toBeVisible();
  await page.getByLabel('Correo').fill('nope');
  await page.getByLabel('Contraseña').fill('abc');
  await submit.click();
  await expect(page.getByText('Escribe un correo válido.')).toBeVisible();
  await expect(page.getByText('La contraseña necesita entre 6 y 128 caracteres.')).toBeVisible();
  await page.waitForTimeout(500);
  expect(count(calls, 'POST', '/auth/register')).toBe(before);
});

test('register opens the session and shows the user name when the display name is blank', async () => {
  await register(page, user);
  await expect(page.getByRole('navigation', { name: 'Principal' }).getByRole('status')).toHaveText(
    user.userName,
  );
});

test('a reload restores the session on the same page', async () => {
  const me = response(page, 'GET', '/users/me', 200);
  await reloadWithSession(page);
  await me;
  await expect(page.getByRole('heading', { name: 'Mis tareas' })).toBeVisible();
  expect(new URL(page.url()).pathname).toBe('/tasks');
});

test('logout closes the session and goes to /login', async () => {
  await signOut(page);
  expect(new URL(page.url()).pathname).toBe('/login');
});

test('login rejects empty fields without a request', async () => {
  const before = count(calls, 'POST', '/auth/login');
  await page.locator('form').getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByText('Escribe tu nombre de usuario.')).toBeVisible();
  await expect(page.getByText('Escribe tu contraseña.')).toBeVisible();
  await page.waitForTimeout(500);
  expect(count(calls, 'POST', '/auth/login')).toBe(before);
});

test('a protected page without a session goes to /login with returnUrl and the login returns there', async () => {
  await page.goto('/profile');
  await page.waitForURL(/\/login/);
  const url = new URL(page.url());
  expect(url.pathname).toBe('/login');
  expect(url.searchParams.get('returnUrl')).toBe('/profile');
  await signIn(page, user);
  await page.waitForURL('**/profile');
});

test('a wrong password shows the message and does not try a refresh', async () => {
  await signOut(page);
  await page.getByLabel('Usuario').fill(user.userName);
  await page.getByLabel('Contraseña').fill('Wrong!password1');
  const refreshes = count(calls, 'POST', '/auth/refresh');
  const rejected = response(page, 'POST', '/auth/login', 401);
  await page.locator('form').getByRole('button', { name: 'Entrar' }).click();
  await rejected;
  await expect(page.getByText('Usuario o contraseña incorrectos.')).toBeVisible();
  await page.waitForTimeout(500);
  expect(count(calls, 'POST', '/auth/refresh')).toBe(refreshes);
});

test('an external returnUrl is ignored and the login goes to /tasks', async () => {
  await page.goto('/login?returnUrl=//example.com');
  await signIn(page, user);
  await page.waitForURL(/\/tasks$/);
  expect(new URL(page.url()).pathname).toBe('/tasks');
});

test('the browser storage stays empty', async () => {
  const storage = await page.evaluate(() => ({
    local: Object.keys(localStorage),
    session: Object.keys(sessionStorage),
  }));
  expect(storage).toEqual({ local: [], session: [] });
});
