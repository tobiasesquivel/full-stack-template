import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormField, FormRoot, form, maxLength, required } from '@angular/forms/signals';
import { TasksApi } from '../api/tasks-api';
import { describeHttpError } from '../../../core/http-error';
import type { TaskResponse } from '../api/task-response';
import { TaskPager } from '../task-pager/task-pager';
import { TaskRow } from '../task-row/task-row';
import { TaskDeleteDialog } from '../task-delete-dialog/task-delete-dialog';
import { taskConflictMessage } from './task-conflict';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-tasks',
  imports: [FormField, FormRoot, TaskPager, TaskRow, TaskDeleteDialog],
  templateUrl: './tasks.html',
  host: { class: 'block' },
})
export class Tasks {
  private readonly tasksApi = inject(TasksApi);

  protected readonly items = signal<TaskResponse[]>([]);
  private readonly cursors = signal<(string | null)[]>([null]);
  private readonly nextCursor = signal<string | null>(null);
  protected readonly page = computed(() => this.cursors().length);
  protected readonly hasPrevious = computed(() => this.cursors().length > 1);
  protected readonly hasNext = computed(() => this.nextCursor() !== null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly createError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly pendingId = signal<string | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly pendingDelete = signal<TaskResponse | null>(null);
  private deleteTrigger: HTMLButtonElement | null = null;
  private readonly injector = inject(Injector);
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
      const result = await this.tasksApi.list(this.cursors().at(-1) ?? null, PAGE_SIZE);
      this.items.set(result.items);
      this.nextCursor.set(result.nextCursor);
    } catch (err) {
      this.error.set(describeHttpError(err));
    } finally {
      this.loading.set(false);
    }
  }

  protected async next(): Promise<void> {
    const cursor = this.nextCursor();
    if (cursor === null) {
      return;
    }
    this.cursors.update((cursors) => [...cursors, cursor]);
    await this.load();
  }

  protected async previous(): Promise<void> {
    if (!this.hasPrevious()) {
      return;
    }
    this.cursors.update((cursors) => cursors.slice(0, -1));
    await this.load();
  }

  private async create(): Promise<undefined> {
    this.createError.set(null);
    try {
      await this.tasksApi.create(this.newTask().title.trim());
      this.createForm().reset({ title: '' });
      this.cursors.set([null]);
      await this.load();
    } catch (err) {
      this.createError.set(describeHttpError(err));
    }
    return undefined;
  }

  protected async setCompleted(task: TaskResponse, completed: boolean): Promise<void> {
    await this.runRowAction(task.id, () =>
      this.tasksApi.update(task.id, {
        title: task.title,
        isCompleted: completed,
        version: task.version,
      }),
    );
  }

  protected startEdit(task: TaskResponse): void {
    this.editingId.set(task.id);
    this.actionError.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected async saveTitle(task: TaskResponse, title: string): Promise<void> {
    await this.runRowAction(task.id, () =>
      this.tasksApi.update(task.id, {
        title,
        isCompleted: task.isCompleted,
        version: task.version,
      }),
    );
    if (this.pendingId() === null && !this.actionError()) {
      this.cancelEdit();
    }
  }

  protected askRemove(task: TaskResponse, trigger: HTMLButtonElement): void {
    this.deleteTrigger = trigger;
    this.pendingDelete.set(task);
  }

  protected cancelRemove(): void {
    this.pendingDelete.set(null);
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
    await this.remove(task, trigger);
  }

  protected async remove(task: TaskResponse, trigger?: HTMLButtonElement | null): Promise<void> {
    const fallback = trigger ?? null;
    this.actionError.set(null);
    this.pendingId.set(task.id);
    let failed = false;
    try {
      await this.tasksApi.remove(task.id);
      if (this.items().length === 1 && this.hasPrevious()) {
        this.cursors.update((cursors) => cursors.slice(0, -1));
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
    const inDialog = active !== null && active.closest('dialog') !== null;
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
      const conflict = taskConflictMessage(err);
      this.actionError.set(conflict ?? describeHttpError(err));
      if (conflict) {
        await this.load();
      }
    } finally {
      this.pendingId.set(null);
    }
  }
}
