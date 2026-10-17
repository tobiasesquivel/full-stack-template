import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { FormField, FormRoot, form, maxLength, required } from '@angular/forms/signals';
import { TasksApi } from '../api/tasks-api';
import { describeHttpError } from '../../../core/http-error';
import type { TaskResponse } from '../api/task-response';
import { TaskPager } from '../task-pager/task-pager';
import { TaskRow } from '../task-row/task-row';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-tasks',
  imports: [FormField, FormRoot, TaskPager, TaskRow],
  templateUrl: './tasks.html',
  host: { class: 'block' },
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
    this.actionError.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected rejectTitle(): void {
    this.actionError.set('El título es obligatorio (máximo 200 caracteres).');
  }

  protected async saveTitle(task: TaskResponse, title: string): Promise<void> {
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
