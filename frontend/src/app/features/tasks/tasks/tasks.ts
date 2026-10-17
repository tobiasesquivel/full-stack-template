import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { FormField, FormRoot, form, maxLength, required } from '@angular/forms/signals';
import { TasksApi } from '../tasks-api';
import { describeHttpError } from '../../../core/http-error';
import type { TaskResponse } from '../tasks-models';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-tasks',
  imports: [FormField, FormRoot],
  template: `
    <section aria-labelledby="tasks-title">
      <h1 id="tasks-title" #tasksHeading tabindex="-1">Mis tareas</h1>

      <form [formRoot]="createForm" aria-label="Crear tarea">
        <label for="new-task-title">Nueva tarea</label>
        <input
          id="new-task-title"
          type="text"
          [formField]="createForm.title"
          placeholder="¿Qué hay que hacer?"
        />
        <button type="submit" [disabled]="createForm().submitting()">Crear</button>
        @if (createForm.title().touched() && createForm.title().invalid()) {
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
        <ul class="task-list">
          @for (task of items(); track task.id) {
            <li class="task-row" [class.task-done]="task.isCompleted">
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
                <button
                  type="button"
                  #deleteButton
                  [disabled]="pendingId() === task.id"
                  (click)="askRemove(task, deleteButton)"
                >
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
    <dialog
      #confirmDialog
      aria-labelledby="delete-confirm-title"
      aria-describedby="delete-confirm-body"
      aria-modal="true"
      (cancel)="cancelRemove($event)"
      style="border: 1px solid #e2e8f0; border-radius: 0.75rem; padding: 1.25rem; width: min(24rem, calc(100vw - 2rem)); max-width: calc(100vw - 2rem); box-sizing: border-box; background: #ffffff; color: #0f172a;"
    >
      <h2 id="delete-confirm-title" style="margin: 0 0 0.5rem; font-size: 1.125rem; line-height: 1.25;">
        Confirmar eliminación
      </h2>
      <p id="delete-confirm-body" style="margin: 0 0 1rem; overflow-wrap: anywhere;">
        ¿Eliminar «{{ pendingDelete()?.title }}»? Esta acción no se puede deshacer.
      </p>
      <div style="display: flex; flex-wrap: wrap; gap: 0.5rem; justify-content: flex-end;">
        <button type="button" autofocus (click)="cancelRemove()">Cancelar</button>
        <button type="button" (click)="confirmRemove()">Confirmar</button>
      </div>
    </dialog>
  `,
  styles: `
    dialog::backdrop {
      background: rgb(15 23 42 / 0.4);
    }
    :host {
      display: block;
    }
    :host button:focus-visible,
    :host input:focus-visible {
      outline: 2px solid #1d4ed8;
      outline-offset: 2px;
    }
    .task-list {
      gap: 0.625rem;
    }
    .task-row {
      border-left-width: 3px;
      border-left-style: solid;
      border-left-color: transparent;
      transition: border-color 120ms ease-in-out;
    }
    .task-row:hover {
      border-color: #93c5fd;
    }
    .task-row.task-done {
      border-left-color: #22c55e;
      background: #f0fdf4;
    }
  `,
})
export class Tasks {
  private readonly tasksApi = inject(TasksApi);

  protected readonly items = signal<TaskResponse[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)),
  );
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly createError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly pendingId = signal<string | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly editTitle = signal('');
  protected readonly pendingDelete = signal<TaskResponse | null>(null);
  private deleteTrigger: HTMLButtonElement | null = null;
  private readonly injector = inject(Injector);
  private readonly confirmDialog = viewChild<ElementRef<HTMLDialogElement>>('confirmDialog');
  private readonly tasksHeading = viewChild<ElementRef<HTMLElement>>('tasksHeading');
  private readonly newTask = signal({ title: '' });
  protected readonly createForm = form(
    this.newTask,
    (path) => {
      required(path.title);
      maxLength(path.title, 200);
    },
    { submission: { action: () => this.create() } },
  );

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

  private async create(): Promise<undefined> {
    this.createError.set(null);
    try {
      await this.tasksApi.create(this.newTask().title.trim());
      this.createForm().reset({ title: '' });
      this.page.set(1);
      await this.load();
    } catch (err) {
      this.createError.set(describeHttpError(err));
    }
    return undefined;
  }

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

  protected askRemove(task: TaskResponse, trigger: HTMLButtonElement): void {
    this.deleteTrigger = trigger;
    this.pendingDelete.set(task);
    const dialog = this.confirmDialog()?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  protected cancelRemove(event?: Event): void {
    if (event) {
      event.preventDefault();
    }
    this.pendingDelete.set(null);
    this.confirmDialog()?.nativeElement.close();
    const trigger = this.deleteTrigger;
    this.deleteTrigger = null;
    trigger?.focus();
  }

  protected async confirmRemove(): Promise<void> {
    const task = this.pendingDelete();
    const trigger = this.deleteTrigger;
    if (!task) {
      return;
    }
    this.pendingDelete.set(null);
    this.deleteTrigger = null;
    this.confirmDialog()?.nativeElement.close();
    await this.remove(task, trigger);
  }

  protected async remove(task: TaskResponse, trigger?: HTMLButtonElement | null): Promise<void> {
    const fallback = trigger ?? null;
    this.actionError.set(null);
    this.pendingId.set(task.id);
    let failed = false;
    try {
      await this.tasksApi.remove(task.id);
      if (this.items().length === 1 && this.page() > 1) {
        this.page.update((p) => p - 1);
      }
      await this.load();
    } catch (err) {
      failed = true;
      this.actionError.set(describeHttpError(err));
    } finally {
      this.pendingId.set(null);
    }
    this.focusAfterRemove(fallback, failed);
  }

  private focusAfterRemove(trigger: HTMLButtonElement | null, failed: boolean): void {
    const active = document.activeElement;
    const dialog = this.confirmDialog()?.nativeElement;
    const inDialog = !!dialog && !!active && dialog.contains(active);
    const onBody = active === null || active === document.body;
    if (!inDialog && !onBody) {
      return;
    }
    afterNextRender(
      () => {
        if (failed && trigger && trigger.isConnected && !trigger.disabled) {
          trigger.focus();
          return;
        }
        this.tasksHeading()?.nativeElement.focus();
      },
      { injector: this.injector },
    );
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
