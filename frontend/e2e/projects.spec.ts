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

const user = newUser('projects');
const REQUIRED = 'El nombre es obligatorio (máximo 100 caracteres).';
const NAME_TAKEN = 'Ya tienes un proyecto con ese nombre.';
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const HOME = 'Hogar e2e';
let page: Page;
let calls: ApiCall[];
let projectId = '';

const section = () => page.getByRole('region', { name: 'Mis proyectos' });
const rows = () => section().getByRole('listitem');
const row = (name: string) => rows().filter({ hasText: name });
const nameField = () => page.getByLabel('Nombre del proyecto', { exact: true });
const editField = () => page.getByRole('textbox', { name: 'Nuevo nombre del proyecto' });
const taskField = () => page.getByLabel('Nueva tarea', { exact: true });
const projectSelect = () => page.getByLabel('Proyecto', { exact: true });

async function submitCreate(): Promise<void> {
  await page
    .getByRole('form', { name: 'Crear proyecto' })
    .getByRole('button', { name: 'Crear' })
    .click();
}

async function createProject(name: string): Promise<{ id: string }> {
  await nameField().fill(name);
  const created = response(page, 'POST', '/api/v1/projects', 201);
  await submitCreate();
  const reply = await created;
  expect(reply.request().headers()['idempotency-key']).toMatch(UUID);
  await expect(nameField()).toHaveValue('');
  return reply.json();
}

test.beforeAll(async ({ browser }) => {
  ({ page, calls } = await openApp(browser));
  await waitForAuthRoom();
  await register(page, user);
  await page.getByRole('link', { name: 'Mis proyectos' }).click();
  await expect(page.getByRole('heading', { name: 'Mis proyectos' })).toBeVisible();
});

test.afterAll(async () => {
  await page.context().close();
});

test.beforeEach(async () => {
  await waitForAuthRoom();
});

test('an empty name shows the message and sends no POST; the field keeps maxlength 100', async () => {
  await expect(nameField()).toHaveAttribute('maxlength', '100');
  const before = count(calls, 'POST', '/api/v1/projects');
  await submitCreate();
  await expect(page.getByText(REQUIRED)).toBeVisible();
  await page.waitForTimeout(500);
  expect(count(calls, 'POST', '/api/v1/projects')).toBe(before);
});

test('a created project shows with its state and zero counters, and survives a reload', async () => {
  const project = await createProject(HOME);
  projectId = project.id;
  await expect(page.getByText(REQUIRED)).toHaveCount(0);
  await expect(row(HOME)).toContainText('Activo');
  await expect(row(HOME)).toContainText('Dueño:');
  await expect(row(HOME)).toContainText('0 tareas, 0 completadas');
  await reloadWithSession(page);
  await expect(row(HOME)).toBeVisible();
});

test('a duplicate name, in any case, shows the NameTaken message and creates nothing', async () => {
  const conflict = response(page, 'POST', '/api/v1/projects', 409);
  await nameField().fill('hogar E2E');
  await submitCreate();
  expect(await (await conflict).json()).toMatchObject({ code: 'Project.NameTaken' });
  await expect(page.getByRole('alert')).toHaveText(NAME_TAKEN);
  await expect(rows()).toHaveCount(1);
});

test('a task created in this project from the tasks page counts on the projects page', async () => {
  await page.getByRole('link', { name: 'Mis tareas' }).click();
  await expect(page.getByRole('heading', { name: 'Mis tareas' })).toBeVisible();
  await expect(page.getByRole('option', { name: HOME })).toHaveCount(1);
  await expect(projectSelect()).toHaveValue('');

  await taskField().fill('Tarea del proyecto');
  await projectSelect().selectOption({ label: HOME });
  const posted = response(page, 'POST', '/api/v1/tasks', 201);
  await page
    .getByRole('form', { name: 'Crear tarea' })
    .getByRole('button', { name: 'Crear' })
    .click();
  expect((await posted).request().postDataJSON()).toEqual({
    title: 'Tarea del proyecto',
    projectId,
  });
  await expect(taskField()).toHaveValue('');
  await expect(projectSelect()).toHaveValue('');

  await page.getByRole('link', { name: 'Mis proyectos' }).click();
  await expect(row(HOME)).toContainText('1 tarea, 0 completadas');
});

test('archiving a project shows Archivado, disables its rename and offers Restaurar', async () => {
  const saved = response(page, 'PUT', '/api/v1/projects/', 200);
  await row(HOME).getByRole('button', { name: 'Archivar' }).click();
  expect(await (await saved).json()).toMatchObject({ isArchived: true });
  await expect(row(HOME)).toContainText('Archivado');
  await expect(row(HOME)).toContainText('Restaura el proyecto para cambiar su nombre.');
  await expect(row(HOME).getByRole('button', { name: 'Editar' })).toBeDisabled();
  await expect(row(HOME).getByRole('button', { name: 'Restaurar' })).toBeVisible();
});

test('restoring a project enables its rename, and the new name is saved', async () => {
  const restored = response(page, 'PUT', '/api/v1/projects/', 200);
  await row(HOME).getByRole('button', { name: 'Restaurar' }).click();
  expect(await (await restored).json()).toMatchObject({ isArchived: false });
  await expect(row(HOME)).toContainText('Activo');
  await expect(row(HOME).getByRole('button', { name: 'Editar' })).toBeEnabled();

  await row(HOME).getByRole('button', { name: 'Editar' }).click();
  await editField().fill('Casa e2e');
  const renamed = response(page, 'PUT', '/api/v1/projects/', 200);
  await rows().filter({ has: editField() }).getByRole('button', { name: 'Guardar' }).click();
  expect(await (await renamed).json()).toMatchObject({ name: 'Casa e2e' });
  await expect(editField()).toHaveCount(0);
  await expect(rows().filter({ hasText: 'Casa e2e' })).toBeVisible();
});
