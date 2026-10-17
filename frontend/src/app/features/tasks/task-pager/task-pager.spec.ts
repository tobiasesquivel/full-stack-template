import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TaskPager } from './task-pager';

describe('TaskPager', () => {
  let fixture: ComponentFixture<TaskPager>;
  let requested: number[];

  function button(name: string): HTMLButtonElement {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const found = buttons.find((candidate) => candidate.textContent?.trim() === name);
    if (!found) throw new Error(`button ${name} not found`);
    return found;
  }

  function status(): string {
    return fixture.nativeElement.querySelector('[role="status"]').textContent.trim();
  }

  async function show(page: number, totalPages: number, totalCount: number, disabled = false): Promise<void> {
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('totalPages', totalPages);
    fixture.componentRef.setInput('totalCount', totalCount);
    fixture.componentRef.setInput('disabled', disabled);
    await fixture.whenStable();
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(TaskPager);
    requested = [];
    fixture.componentInstance.pageChange.subscribe((page) => requested.push(page));
  });

  it('shows the page, the total pages and the total count inside the Paginación nav', async () => {
    await show(2, 3, 25);
    expect(fixture.nativeElement.querySelector('nav').getAttribute('aria-label')).toBe('Paginación');
    expect(status()).toBe('Página 2 de 3 (25)');
  });

  it('disables Anterior on the first page and Siguiente on the last', async () => {
    await show(1, 2, 11);
    expect(button('Anterior').disabled).toBe(true);
    expect(button('Siguiente').disabled).toBe(false);
    await show(2, 2, 11);
    expect(button('Anterior').disabled).toBe(false);
    expect(button('Siguiente').disabled).toBe(true);
  });

  it('disables both buttons while disabled', async () => {
    await show(2, 3, 25, true);
    expect(button('Anterior').disabled).toBe(true);
    expect(button('Siguiente').disabled).toBe(true);
  });

  it('asks for the previous and the next page', async () => {
    await show(2, 3, 25);
    button('Siguiente').click();
    button('Anterior').click();
    expect(requested).toEqual([3, 1]);
  });
});
