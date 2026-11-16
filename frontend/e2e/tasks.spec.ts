import { expect, test, type Locator, type Page } from '@playwright/test';
import {
  count,
  listPage,
  newUser,
  openApp,
  register,
  reloadWithSession,
  response,
  waitForAuthRoom,
  type ApiCall,
} from './support/app';

test.describe.configure({ mode: 'serial' });

const user = newUser('tasks');
const REQUIRED = 'El título es obligatorio (máximo 200 caracteres).';
let page: Page;
let calls: ApiCall[];
const bodies: { title: string; isCompleted: boolean; version: number }[] = [];

const rows = () => page.getByRole('region', { name: 'Mis tareas' }).getByRole('listitem');
const row = (title: string) => rows().filter({ hasText: title });
const pager = () => page.getByRole('navigation', { name: 'Paginación' });
const editField = () => page.getByRole('textbox', { name: 'Editar título' });
const dialog = () => page.getByRole('dialog');

async function create(title: string): Promise<void> {
  await page.getByLabel('Nueva tarea').fill(title);
  const created = response(page, 'POST', '/api/v1/tasks', 201);
  await page
    .getByRole('form', { name: 'Crear tarea' })
    .getByRole('button', { name: 'Crear' })
    .click();
  await created;
  await expect(page.getByLabel('Nueva tarea')).toHaveValue('');
}

async function focused(): Promise<{ id: string; text: string; row: string }> {
  return page.evaluate(() => {
    const active = document.activeElement;
    return {
      id: active?.id ?? '',
      text: (active?.textContent ?? '').trim(),
      row: (active?.closest('li')?.querySelector('span')?.textContent ?? '').trim(),
    };
  });
}

async function closeEdit(): Promise<void> {
  if ((await editField().count()) === 0) return;
  await rows().getByRole('button', { name: 'Cancelar' }).click();
  await expect(editField()).toHaveCount(0);
}

test.beforeAll(async ({ browser }) => {
  ({ page, calls } = await openApp(browser));
  page.on('request', (request) => {
    if (request.method() === 'PUT' && new URL(request.url()).pathname.startsWith('/api/v1/tasks/'))
      bodies.push(request.postDataJSON());
  });
  await waitForAuthRoom();
  await register(page, user);
});

test.afterAll(async () => {
  await page.context().close();
});

test.beforeEach(async () => {
  await waitForAuthRoom();
});

test('an empty title shows the message and sends no POST; the field keeps maxlength 200', async () => {
  await expect(page.getByLabel('Nueva tarea')).toHaveAttribute('maxlength', '200');
  const before = count(calls, 'POST', '/api/v1/tasks');
  await page
    .getByRole('form', { name: 'Crear tarea' })
    .getByRole('button', { name: 'Crear' })
    .click();
  await expect(page.getByText(REQUIRED)).toBeVisible();
  await page.waitForTimeout(500);
  expect(count(calls, 'POST', '/api/v1/tasks')).toBe(before);
});

test('a created task clears the form and survives a reload', async () => {
  await create('Prueba de runtime');
  await expect(page.getByText(REQUIRED)).toHaveCount(0);
  await reloadWithSession(page);
  await expect(row('Prueba de runtime')).toBeVisible();
});

test('a done row gets the green border and line-through; the dialog deletes it', async () => {
  const task = row('Prueba de runtime');
  await expect(task).toHaveCSS('border-left-width', '3px');
  const saved = response(page, 'PUT', '/api/v1/tasks/', 200);
  await task.getByRole('checkbox').check();
  await saved;
  await expect(task).not.toHaveCSS('border-left-color', 'rgba(0, 0, 0, 0)');
  await expect(task.locator('span')).toHaveCSS('text-decoration-line', 'line-through');
  await task.getByRole('button', { name: 'Eliminar' }).click();
  await expect(dialog().getByText('Confirmar eliminación')).toBeVisible();
  const backdrop = await page.evaluate(
    () => getComputedStyle(document.querySelector('dialog')!, '::backdrop').backgroundColor,
  );
  expect(backdrop).toContain('0.4');
  const deleted = response(page, 'DELETE', '/api/v1/tasks/', 204);
  await dialog().getByRole('button', { name: 'Confirmar' }).click();
  await deleted;
  await expect(page.getByText('No tienes tareas todavía. Crea la primera arriba.')).toBeVisible();
});

test('with 11 tasks the pager moves between page 1 and page 2', async () => {
  for (let index = 1; index <= 11; index += 1)
    await create(`Paginada ${String(index).padStart(2, '0')}`);
  await expect(pager().getByRole('status')).toHaveText('Página 1');
  await expect(rows()).toHaveCount(10);
  await expect(pager().getByRole('button', { name: 'Anterior' })).toBeDisabled();
  await expect(pager().getByRole('button', { name: 'Siguiente' })).toBeEnabled();
  const second = listPage(page, 'next');
  await pager().getByRole('button', { name: 'Siguiente' }).click();
  await second;
  await expect(pager().getByRole('status')).toHaveText('Página 2');
  await expect(rows()).toHaveCount(1);
  await expect(pager().getByRole('button', { name: 'Siguiente' })).toBeDisabled();
  const first = listPage(page, 'first');
  await pager().getByRole('button', { name: 'Anterior' }).click();
  await first;
  await expect(pager().getByRole('status')).toHaveText('Página 1');
  await expect(rows()).toHaveCount(10);
});

test('renaming a row sends PUT 200 with the new title', async () => {
  const first = rows().first();
  const original = (await first.locator('span').textContent())?.trim() ?? '';
  await first.getByRole('button', { name: 'Editar' }).click();
  await expect(editField()).toHaveValue(original);
  await expect(editField()).toHaveAttribute('maxlength', '200');
  await editField().fill('Renombrada en linea');
  const saved = response(page, 'PUT', '/api/v1/tasks/', 200);
  await first.getByRole('button', { name: 'Guardar' }).click();
  await saved;
  await expect(row('Renombrada en linea')).toBeVisible();
  await expect(editField()).toHaveCount(0);
  expect(bodies.at(-1)).toEqual({
    title: 'Renombrada en linea',
    isCompleted: false,
    version: expect.any(Number),
  });
});

test('an empty or blank title shows its message under the field and sends no PUT', async () => {
  const first = rows().first();
  const before = count(calls, 'PUT', '/api/v1/tasks/');
  await first.getByRole('button', { name: 'Editar' }).click();
  for (const value of ['', '   ']) {
    await editField().fill(value);
    await first.getByRole('button', { name: 'Guardar' }).click();
    const message = first.getByText(REQUIRED);
    await expect(message).toBeVisible();
    const id = await message.getAttribute('id');
    expect(id).toBeTruthy();
    await expect(editField()).toHaveAttribute('aria-describedby', id!);
    expect(await editField().evaluate((input) => input.nextElementSibling?.id)).toBe(id);
    await expect(page.getByRole('alert')).toHaveCount(0);
  }
  await page.waitForTimeout(500);
  expect(count(calls, 'PUT', '/api/v1/tasks/')).toBe(before);
  await first.getByRole('button', { name: 'Cancelar' }).click();
  await expect(row('Renombrada en linea')).toBeVisible();
  await first.getByRole('button', { name: 'Editar' }).click();
  await expect(editField()).toHaveValue('Renombrada en linea');
  await closeEdit();
});

test('opening Editar on a second row closes the first', async () => {
  await rows().nth(0).getByRole('button', { name: 'Editar' }).click();
  await editField().fill('borrador sin guardar');
  await rows().nth(1).getByRole('button', { name: 'Editar' }).click();
  await expect(editField()).toHaveCount(1);
  await expect(rows().nth(1).getByRole('textbox', { name: 'Editar título' })).toBeVisible();
  await expect(rows().nth(0)).toContainText('Renombrada en linea');
  await closeEdit();
});

test('the checkbox sends PUT with isCompleted true and the row stays done after a reload', async () => {
  const saved = response(page, 'PUT', '/api/v1/tasks/', 200);
  await row('Renombrada en linea').getByRole('checkbox').check();
  await saved;
  expect(bodies.at(-1)).toEqual({
    title: 'Renombrada en linea',
    isCompleted: true,
    version: expect.any(Number),
  });
  await reloadWithSession(page);
  await expect(row('Renombrada en linea').getByRole('checkbox')).toBeChecked();
});

test('deleting the only task on page 2 goes back to page 1', async () => {
  await pager().getByRole('button', { name: 'Siguiente' }).click();
  await expect(pager().getByRole('status')).toHaveText('Página 2');
  const title = (await rows().first().locator('span').textContent())?.trim() ?? '';
  await rows().first().getByRole('button', { name: 'Eliminar' }).click();
  await expect(
    dialog().getByText(`¿Eliminar «${title}»? Esta acción no se puede deshacer.`),
  ).toBeVisible();
  const deleted = response(page, 'DELETE', '/api/v1/tasks/', 204);
  const first = listPage(page, 'first');
  await dialog().getByRole('button', { name: 'Confirmar' }).click();
  await deleted;
  await first;
  await expect(pager().getByRole('status')).toHaveText('Página 1');
  await expect(pager().getByRole('button', { name: 'Siguiente' })).toBeDisabled();
  await expect(rows()).toHaveCount(10);
  await expect(dialog()).toBeHidden();
});

test('the delete dialog opens centered in the viewport', async () => {
  await rows().nth(1).getByRole('button', { name: 'Eliminar' }).click();
  await expect(dialog()).toBeVisible();
  const box = (await dialog().boundingBox())!;
  const view = page.viewportSize()!;
  expect(Math.abs(box.x + box.width / 2 - view.width / 2)).toBeLessThanOrEqual(2);
  expect(Math.abs(box.y + box.height / 2 - view.height / 2)).toBeLessThanOrEqual(2);
  await dialog().getByRole('button', { name: 'Cancelar' }).click();
  await expect(dialog()).toBeHidden();
});

test('Escape closes the dialog and returns focus to that row Eliminar', async () => {
  const target: Locator = rows().nth(2);
  const title = (await target.locator('span').textContent())?.trim() ?? '';
  await target.getByRole('button', { name: 'Eliminar' }).click();
  await expect(
    dialog().getByText(`¿Eliminar «${title}»? Esta acción no se puede deshacer.`),
  ).toBeVisible();
  expect((await focused()).text).toBe('Cancelar');
  const deletes = count(calls, 'DELETE', '/api/v1/tasks/');
  await page.keyboard.press('Escape');
  await expect(dialog()).toBeHidden();
  await expect.poll(focused).toEqual({ id: '', text: 'Eliminar', row: title });
  expect(count(calls, 'DELETE', '/api/v1/tasks/')).toBe(deletes);
  await expect(rows()).toHaveCount(10);
});

test('Confirmar sends DELETE 204 and moves focus to the Mis tareas heading', async () => {
  const target = rows().nth(3);
  const title = (await target.locator('span').textContent())?.trim() ?? '';
  await target.getByRole('button', { name: 'Eliminar' }).click();
  await expect(dialog()).toBeVisible();
  const deleted = response(page, 'DELETE', '/api/v1/tasks/', 204);
  await dialog().getByRole('button', { name: 'Confirmar' }).click();
  await deleted;
  await expect(pager().getByRole('status')).toHaveText('Página 1');
  await expect.poll(async () => (await focused()).id).toBe('tasks-title');
  await expect(row(title)).toHaveCount(0);
});

test('a stale tab that ticks a task renamed in another tab gets 409 and shows the rename', async () => {
  const title = 'Tarea compartida';
  await create(title);
  const target = rows().first();
  await expect(target).toContainText(title);
  const stale = await page.context().newPage();
  await stale.goto('/tasks');
  const staleRow = (text: string) =>
    stale
      .getByRole('region', { name: 'Mis tareas' })
      .getByRole('listitem')
      .filter({ hasText: text });
  await expect(staleRow(title).getByRole('checkbox')).not.toBeChecked();

  await target.getByRole('button', { name: 'Editar' }).click();
  await editField().fill('Renombrada en otra pestaña');
  const renamed = response(page, 'PUT', '/api/v1/tasks/', 200);
  await target.getByRole('button', { name: 'Guardar' }).click();
  await renamed;

  const conflict = response(stale, 'PUT', '/api/v1/tasks/', 409);
  await staleRow(title).getByRole('checkbox').check();
  expect(await (await conflict).json()).toMatchObject({ code: 'Task.Conflict' });
  await expect(stale.getByRole('alert')).toHaveText(
    'Otra pestaña o dispositivo cambió esta tarea. Se muestran los datos actuales.',
  );
  await expect(staleRow('Renombrada en otra pestaña').getByRole('checkbox')).not.toBeChecked();
  await expect(staleRow(title)).toHaveCount(0);
  await stale.close();
});
