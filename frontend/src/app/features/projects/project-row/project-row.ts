import { Component, computed, input, linkedSignal, output } from '@angular/core';
import {
  FormField,
  form,
  maxLength,
  required,
  requiredError,
  submit,
  validate,
} from '@angular/forms/signals';
import type { ProjectResponse } from '../api/project-response';

@Component({
  selector: 'li[app-project-row]',
  imports: [FormField],
  templateUrl: './project-row.html',
  host: {
    class:
      'flex flex-wrap items-center gap-2 rounded-lg border border-l-3 px-3 py-2.5 transition-colors duration-120 ease-in-out hover:border-blue-300',
    '[class]':
      "project().isArchived ? 'border-slate-200 border-l-slate-400 bg-slate-100' : 'border-slate-200 border-l-transparent bg-slate-50'",
  },
})
export class ProjectRow {
  readonly project = input.required<ProjectResponse>();
  readonly busy = input(false);
  readonly editing = input(false);
  readonly editStarted = output();
  readonly editCancelled = output();
  readonly renamed = output<string>();
  readonly archiveChanged = output<boolean>();

  private readonly draft = linkedSignal({
    source: () => ({ name: this.project().name, editing: this.editing() }),
    computation: ({ name }) => ({ name }),
  });
  protected readonly editForm = form(this.draft, (path) => {
    required(path.name);
    maxLength(path.name, 100);
    validate(path.name, ({ value }) => (value().trim() === '' ? requiredError() : undefined));
  });

  protected readonly showNameError = computed(
    () => this.editForm.name().touched() && this.editForm.name().invalid(),
  );

  protected async save(): Promise<void> {
    await submit(this.editForm, {
      action: async () => {
        this.renamed.emit(this.draft().name.trim());
        return undefined;
      },
    });
  }
}
