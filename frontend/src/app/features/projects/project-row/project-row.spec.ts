import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { ProjectResponse } from '../api/project-response';
import { ProjectRow } from './project-row';

const ACTIVE: ProjectResponse = {
  id: 'p-1',
  name: 'Hogar',
  isArchived: false,
  createdAt: '2026-11-18T10:00:00Z',
  updatedAt: null,
  version: 1,
  ownerName: 'ana',
  taskCount: 3,
  completedTaskCount: 1,
};

const ARCHIVED: ProjectResponse = { ...ACTIVE, isArchived: true, version: 2 };

describe('ProjectRow', () => {
  let fixture: ComponentFixture<ProjectRow>;
  let archiveRequests: boolean[];
  let renames: string[];
  let editRequests: number;

  function button(name: string): HTMLButtonElement {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const found = buttons.find((candidate) => candidate.textContent?.trim() === name);
    if (!found) throw new Error(`button ${name} not found`);
    return found;
  }

  function text(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  async function show(project: ProjectResponse, editing = false, busy = false): Promise<void> {
    fixture.componentRef.setInput('project', project);
    fixture.componentRef.setInput('editing', editing);
    fixture.componentRef.setInput('busy', busy);
    await fixture.whenStable();
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(ProjectRow);
    archiveRequests = [];
    renames = [];
    editRequests = 0;
    fixture.componentInstance.archiveChanged.subscribe((value) => archiveRequests.push(value));
    fixture.componentInstance.renamed.subscribe((name) => renames.push(name));
    fixture.componentInstance.editStarted.subscribe(() => (editRequests += 1));
  });

  it('shows the name, the state, the owner and the task counters', async () => {
    await show(ACTIVE);
    expect(text()).toContain('Hogar');
    expect(text()).toContain('Activo');
    expect(text()).toContain('Dueño: ana');
    expect(text().replace(/\s+/g, ' ')).toContain('3 tareas, 1 completada');
  });

  it('offers Archivar on an active project and Restaurar on an archived one', async () => {
    await show(ACTIVE);
    expect(button('Archivar')).toBeTruthy();
    await show(ARCHIVED);
    expect(text()).toContain('Archivado');
    expect(button('Restaurar')).toBeTruthy();
  });

  it('disables Editar while archived and says how to rename it', async () => {
    await show(ARCHIVED);
    expect(button('Editar').disabled).toBe(true);
    expect(text()).toContain('Restaura el proyecto para cambiar su nombre.');
    await show(ACTIVE);
    expect(button('Editar').disabled).toBe(false);
  });

  it('asks to edit an active project', async () => {
    await show(ACTIVE);
    button('Editar').click();
    expect(editRequests).toBe(1);
  });

  it('emits the new archive state from Archivar and Restaurar', async () => {
    await show(ACTIVE);
    button('Archivar').click();
    await show(ARCHIVED);
    button('Restaurar').click();
    expect(archiveRequests).toEqual([true, false]);
  });

  it('disables every action while busy', async () => {
    await show(ACTIVE, false, true);
    expect(button('Editar').disabled).toBe(true);
    expect(button('Archivar').disabled).toBe(true);
  });

  it('emits the trimmed name from Guardar', async () => {
    await show(ACTIVE, true);
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = '  Casa  ';
    input.dispatchEvent(new Event('input'));
    button('Guardar').click();
    await fixture.whenStable();
    expect(renames).toEqual(['Casa']);
  });

  it('keeps a blank name out and shows its message under the field', async () => {
    await show(ACTIVE, true);
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    button('Guardar').click();
    await fixture.whenStable();
    expect(renames).toEqual([]);
    expect(text()).toContain('El nombre es obligatorio (máximo 100 caracteres).');
  });
});
