import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TasksService } from '../tasks.service';
import { describeHttpError } from '../../../core/http-error';
import type { TaskResponse } from '../tasks-models';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-tasks',
  imports: [ReactiveFormsModule],
  template: `
    <section aria-labelledby="tasks-title">
      <h1 id="tasks-title">Mis tareas</h1>

      <form [formGroup]="createForm" (ngSubmit)="create()" aria-label="Crear tarea" novalidate>
        <label for="new-task-title">Nueva tarea</label>
        <input
          id="new-task-title"
          type="text"
          formControlName="title"
          maxlength="200"
          placeholder="¿Qué hay que hacer?"
          required
        />
        <button type="submit" [disabled]="creating()">Crear</button>
        @if (createForm.controls.title.touched && createForm.controls.title.invalid) {
          <p class="field-error">El título es obligatorio (máximo 200 caracteres).</p>
        }
        @if (createError()) {
          <p role="alert">{{ createError() }}</p>
        }
      </form>

      @if (loading()) {
        <p role="status">Cargando tareas…</p>
      } @else if (error()) {
        <p role="alert">{{ error() }}</p>
        <button type="button" (click)="load()">Reintentar</button>
      } @else if (items().length === 0) {
        <p>No tienes tareas todavía. Crea la primera arriba.</p>
      } @else {
        <ul>
          @for (task of items(); track task.id) {
            <li>
              <input
                type="checkbox"
                [id]="'done-' + task.id"
                [checked]="task.isCompleted"
                [disabled]="pendingId() === task.id"
                (change)="setCompleted(task, !task.isCompleted)"
                [attr.aria-label]="(task.isCompleted ? 'Marcar pendiente: ' : 'Marcar hecha: ') + task.title"
              />
              @if (editingId() === task.id) {
                <input
                  type="text"
                  [id]="'edit-' + task.id"
                  [value]="editTitle()"
                  (input)="editTitle.set($any($event.target).value)"
                  maxlength="200"
                  aria-label="Editar título"
                />
                <button type="button" [disabled]="pendingId() === task.id" (click)="saveTitle(task)">
                  Guardar
                </button>
                <button type="button" (click)="cancelEdit()">Cancelar</button>
              } @else {
                <span [class.done]="task.isCompleted">{{ task.title }}</span>
                <button type="button" [disabled]="pendingId() === task.id" (click)="startEdit(task)">
                  Editar
                </button>
                <button type="button" [disabled]="pendingId() === task.id" (click)="remove(task)">
                  Eliminar
                </button>
              }
            </li>
          }
        </ul>
        @if (actionError()) {
          <p role="alert">{{ actionError() }}</p>
        }
        <nav aria-label="Paginación">
          <button type="button" [disabled]="page() <= 1 || loading()" (click)="goTo(page() - 1)">
            Anterior
          </button>
          <span role="status">Página {{ page() }} de {{ totalPages() }} ({{ totalCount() }})</span>
          <button
            type="button"
            [disabled]="page() >= totalPages() || loading()"
            (click)="goTo(page() + 1)"
          >
            Siguiente
          </button>
        </nav>
      }
    </section>
  `,
})
export class Tasks {
  private readonly tasksApi = inject(TasksService);
  private readonly fb = inject(FormBuilder);

  protected readonly items = signal<TaskResponse[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)),
  );
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly pendingId = signal<string | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly editTitle = signal('');

  protected readonly createForm = this.fb.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await this.tasksApi.list(this.page(), PAGE_SIZE);
      this.items.set(result.items);
      this.totalCount.set(result.totalCount);
      this.page.set(result.page);
    } catch (err) {
      this.error.set(describeHttpError(err));
    } finally {
      this.loading.set(false);
    }
  }

  protected async goTo(page: number): Promise<void> {
    this.page.set(page);
    await this.load();
  }

  protected async create(): Promise<void> {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }
    this.creating.set(true);
    this.createError.set(null);
    try {
      const { title } = this.createForm.getRawValue();
      await this.tasksApi.create((title ?? '').trim());
      this.createForm.reset();
      this.page.set(1);
      await this.load();
    } catch (err) {
      this.createError.set(describeHttpError(err));
    } finally {
      this.creating.set(false);
    }
  }

  /** El checkbox envía el estado final por PUT con el valor deseado. */
  protected async setCompleted(task: TaskResponse, completed: boolean): Promise<void> {
    await this.runRowAction(task.id, () =>
      this.tasksApi.update(task.id, { title: task.title, isCompleted: completed }),
    );
  }

  protected startEdit(task: TaskResponse): void {
    this.editingId.set(task.id);
    this.editTitle.set(task.title);
    this.actionError.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.editTitle.set('');
  }

  protected async saveTitle(task: TaskResponse): Promise<void> {
    const title = this.editTitle().trim();
    if (!title || title.length > 200) {
      this.actionError.set('El título es obligatorio (máximo 200 caracteres).');
      return;
    }
    await this.runRowAction(task.id, () =>
      this.tasksApi.update(task.id, { title, isCompleted: task.isCompleted }),
    );
    if (this.pendingId() === null && !this.actionError()) {
      this.cancelEdit();
    }
  }

  protected async remove(task: TaskResponse): Promise<void> {
    if (!window.confirm(`¿Eliminar «${task.title}»?`)) {
      return;
    }
    this.actionError.set(null);
    this.pendingId.set(task.id);
    try {
      await this.tasksApi.remove(task.id);
      if (this.items().length === 1 && this.page() > 1) {
        this.page.update((p) => p - 1);
      }
      await this.load();
    } catch (err) {
      this.actionError.set(describeHttpError(err));
    } finally {
      this.pendingId.set(null);
    }
  }

  private async runRowAction(id: string, action: () => Promise<TaskResponse>): Promise<void> {
    this.actionError.set(null);
    this.pendingId.set(id);
    try {
      const updated = await action();
      this.items.update((items) => items.map((item) => (item.id === id ? updated : item)));
    } catch (err) {
      this.actionError.set(describeHttpError(err));
    } finally {
      this.pendingId.set(null);
    }
  }
}
