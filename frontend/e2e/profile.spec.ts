import { expect, test, type Page } from '@playwright/test';
import {
  count,
  newUser,
  openApp,
  register,
  reloadWithSession,
  response,
  waitForAuthRoom,
  type ApiCall,
} from './support/app';

test.describe.configure({ mode: 'serial' });

const user = newUser('profile');
const ZONE = 'America/Argentina/Buenos_Aires';
let page: Page;
let calls: ApiCall[];

test.beforeAll(async ({ browser }) => {
  ({ page, calls } = await openApp(browser));
  await waitForAuthRoom();
  await register(page, user);
  await page.getByRole('link', { name: 'Mi perfil' }).click();
  await page.waitForURL('**/profile');
  await expect(page.getByRole('heading', { name: 'Mi perfil' })).toBeVisible();
  await expect(page.getByLabel('Nombre visible')).toHaveValue(user.userName);
});

test.afterAll(async () => {
  await page.context().close();
});

test.beforeEach(async () => {
  await waitForAuthRoom();
});

test('an empty display name shows the message and sends no PUT', async () => {
  const before = count(calls, 'PUT', '/users/me');
  await page.getByLabel('Nombre visible').fill('');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('El nombre visible es obligatorio.')).toBeVisible();
  await page.waitForTimeout(500);
  expect(count(calls, 'PUT', '/users/me')).toBe(before);
});

test('saved values persist after a reload', async () => {
  await page.getByLabel('Nombre visible').fill('Perfil verificado');
  await page.getByLabel('Zona horaria (IANA)').fill(ZONE);
  const saved = response(page, 'PUT', '/users/me', 200);
  await page.getByRole('button', { name: 'Guardar' }).click();
  await saved;
  await expect(page.getByText('Perfil actualizado.')).toBeVisible();
  await reloadWithSession(page);
  await expect(page.getByLabel('Nombre visible')).toHaveValue('Perfil verificado');
  await expect(page.getByLabel('Zona horaria (IANA)')).toHaveValue(ZONE);
});

test('an unknown time zone gets 400 with an alert and keeps the saved zone', async () => {
  await page.getByLabel('Zona horaria (IANA)').fill('No/Existe');
  const rejected = response(page, 'PUT', '/users/me', 400);
  await page.getByRole('button', { name: 'Guardar' }).click();
  await rejected;
  await expect(page.getByRole('alert')).toBeVisible();
  await reloadWithSession(page);
  await expect(page.getByLabel('Zona horaria (IANA)')).toHaveValue(ZONE);
});
