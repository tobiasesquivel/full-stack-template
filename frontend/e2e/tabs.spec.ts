import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import {
  isOpen,
  newUser,
  openApp,
  register,
  signIn,
  signOut,
  track,
  waitForAuthRoom,
  type ApiCall,
} from './support/app';

test.describe.configure({ mode: 'serial' });

const user = newUser('tabs');
let context: BrowserContext;
let first: Page;
let second: Page;
let calls: ApiCall[];
let otherCalls: ApiCall[];

const refreshes = () =>
  [...calls, ...otherCalls].filter(
    (call) => call.method === 'POST' && call.path === '/api/v1/auth/refresh',
  );
const shownUser = (page: Page) =>
  page.getByRole('navigation', { name: 'Principal' }).getByRole('status');

async function bothOpen(): Promise<void> {
  await signIn(first, user);
  if (!(await isOpen(second))) {
    await second.goto('/tasks');
    await expect(second.getByRole('button', { name: 'Salir' })).toBeVisible();
  }
}

test.beforeAll(async ({ browser }) => {
  ({ context, page: first, calls } = await openApp(browser));
  second = await context.newPage();
  otherCalls = track(second);
  await waitForAuthRoom();
  await register(first, user);
});

test.afterAll(async () => {
  await context.close();
});

test.beforeEach(async () => {
  await waitForAuthRoom();
});

test('two tabs that reload at the same time both keep the session', async () => {
  await bothOpen();
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => (release = resolve));
  const timer = setTimeout(() => release(), 1500);
  let arrived = 0;
  await context.route(
    (url) => url.pathname === '/api/v1/auth/refresh',
    async (route) => {
      arrived += 1;
      if (arrived === 2) release();
      await gate;
      clearTimeout(timer);
      await route.continue();
    },
    { times: 2 },
  );
  const before = refreshes().length;
  await Promise.all([first.reload(), second.reload()]);
  await expect.poll(() => refreshes().length - before).toBe(2);
  await context.unrouteAll({ behavior: 'ignoreErrors' });
  expect(
    refreshes()
      .slice(before)
      .map((call) => call.status),
  ).toEqual([200, 200]);
  await expect(first.getByRole('button', { name: 'Salir' })).toBeVisible();
  await expect(second.getByRole('button', { name: 'Salir' })).toBeVisible();
});

test('closing the session in one tab sends the other tab to /login', async () => {
  await bothOpen();
  await signOut(first);
  await second.waitForURL(/\/login/);
  expect(await isOpen(second)).toBe(false);
});

test('opening a session in one tab opens it in the other with that user', async () => {
  const other = newUser('tabsb');
  await expect(second).toHaveURL(/\/login/);
  await register(first, other);
  await expect(second.getByRole('button', { name: 'Salir' })).toBeVisible();
  await expect(shownUser(second)).toHaveText(other.userName);
  await second.waitForURL(/\/tasks$/);
});
