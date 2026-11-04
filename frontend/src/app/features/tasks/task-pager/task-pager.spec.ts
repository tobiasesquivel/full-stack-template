import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TaskPager } from './task-pager';

describe('TaskPager', () => {
  let fixture: ComponentFixture<TaskPager>;
  let requested: string[];

  function button(name: string): HTMLButtonElement {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const found = buttons.find((candidate) => candidate.textContent?.trim() === name);
    if (!found) throw new Error(`button ${name} not found`);
    return found;
  }

  function status(): string {
    return fixture.nativeElement.querySelector('[role="status"]').textContent.trim();
  }

  async function show(
    page: number,
    hasPrevious: boolean,
    hasNext: boolean,
    disabled = false,
  ): Promise<void> {
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('hasPrevious', hasPrevious);
    fixture.componentRef.setInput('hasNext', hasNext);
    fixture.componentRef.setInput('disabled', disabled);
    await fixture.whenStable();
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(TaskPager);
    requested = [];
    fixture.componentInstance.previous.subscribe(() => requested.push('previous'));
    fixture.componentInstance.next.subscribe(() => requested.push('next'));
  });

  it('shows the page number inside the Paginación nav', async () => {
    await show(2, true, true);
    expect(fixture.nativeElement.querySelector('nav').getAttribute('aria-label')).toBe(
      'Paginación',
    );
    expect(status()).toBe('Página 2');
  });

  it('disables Anterior without a previous page and Siguiente without a next one', async () => {
    await show(1, false, true);
    expect(button('Anterior').disabled).toBe(true);
    expect(button('Siguiente').disabled).toBe(false);
    await show(2, true, false);
    expect(button('Anterior').disabled).toBe(false);
    expect(button('Siguiente').disabled).toBe(true);
  });

  it('disables both buttons while disabled', async () => {
    await show(2, true, true, true);
    expect(button('Anterior').disabled).toBe(true);
    expect(button('Siguiente').disabled).toBe(true);
  });

  it('asks for the next and the previous page', async () => {
    await show(2, true, true);
    button('Siguiente').click();
    button('Anterior').click();
    expect(requested).toEqual(['next', 'previous']);
  });
});
