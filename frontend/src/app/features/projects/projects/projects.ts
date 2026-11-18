import { Component, computed, inject, signal } from '@angular/core';
import {
  FormField,
  FormRoot,
  form,
  maxLength,
  required,
  requiredError,
  validate,
} from '@angular/forms/signals';
import { ProjectsApi } from '../api/projects-api';
import type { ProjectResponse } from '../api/project-response';
import { describeHttpError } from '../../../core/http-error';
import { ProjectPager } from '../project-pager/project-pager';
import { ProjectRow } from '../project-row/project-row';
import { projectErrorMessage } from './project-error';
import { type CreateAttempt, nextCreateAttempt } from './create-attempt';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-projects',
  imports: [FormField, FormRoot, ProjectPager, ProjectRow],
  templateUrl: './projects.html',
  host: { class: 'block' },
})
export class Projects {
  private readonly projectsApi = inject(ProjectsApi);

  protected readonly items = signal<ProjectResponse[]>([]);
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
  private readonly newProject = signal({ name: '' });
  private lastAttempt: CreateAttempt | null = null;
  protected readonly createForm = form(
    this.newProject,
    (path) => {
      required(path.name);
      maxLength(path.name, 100);
      validate(path.name, ({ value }) => (value().trim() === '' ? requiredError() : undefined));
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
      const result = await this.projectsApi.list(this.cursors().at(-1) ?? null, PAGE_SIZE);
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
      const attempt = nextCreateAttempt(this.lastAttempt, {
        name: this.newProject().name.trim(),
      });
      this.lastAttempt = attempt;
      await this.projectsApi.create(attempt.request, attempt.key);
      this.lastAttempt = null;
      this.createForm().reset({ name: '' });
      this.cursors.set([null]);
      await this.load();
    } catch (err) {
      this.createError.set(projectErrorMessage(err) ?? describeHttpError(err));
    }
    return undefined;
  }

  protected startEdit(project: ProjectResponse): void {
    this.editingId.set(project.id);
    this.actionError.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected async saveName(project: ProjectResponse, name: string): Promise<void> {
    await this.runRowAction(project.id, () =>
      this.projectsApi.update(project.id, {
        name,
        isArchived: project.isArchived,
        version: project.version,
      }),
    );
    if (this.pendingId() === null && !this.actionError()) {
      this.cancelEdit();
    }
  }

  protected async setArchived(project: ProjectResponse, isArchived: boolean): Promise<void> {
    await this.runRowAction(project.id, () =>
      this.projectsApi.update(project.id, {
        name: project.name,
        isArchived,
        version: project.version,
      }),
    );
  }

  private async runRowAction(id: string, action: () => Promise<ProjectResponse>): Promise<void> {
    this.actionError.set(null);
    this.pendingId.set(id);
    try {
      const updated = await action();
      this.items.update((items) => items.map((item) => (item.id === id ? updated : item)));
    } catch (err) {
      const conflict = projectErrorMessage(err);
      this.actionError.set(conflict ?? describeHttpError(err));
      if (conflict) {
        await this.load();
      }
    } finally {
      this.pendingId.set(null);
    }
  }
}
