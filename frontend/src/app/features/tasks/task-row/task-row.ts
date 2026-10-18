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
import type { TaskResponse } from '../api/task-response';

@Component({
  selector: 'li[app-task-row]',
  imports: [FormField],
  templateUrl: './task-row.html',
  host: {
    class:
      'flex flex-wrap items-center gap-2 rounded-lg border border-l-3 border-slate-200 px-3 py-2.5 transition-colors duration-120 ease-in-out hover:border-blue-300',
    '[class]':
      "task().isCompleted ? 'border-l-green-500 bg-green-50 hover:border-l-green-500' : 'border-l-transparent bg-slate-50'",
  },
})
export class TaskRow {
  readonly task = input.required<TaskResponse>();
  readonly editing = input(false);
  readonly pending = input(false);
  readonly completedChange = output<boolean>();
  readonly editStarted = output();
  readonly renamed = output<string>();
  readonly editCancelled = output();
  readonly removeRequested = output<HTMLButtonElement>();

  private readonly draft = linkedSignal({
    source: () => ({ title: this.task().title, editing: this.editing() }),
    computation: ({ title }) => ({ title }),
  });
  protected readonly editForm = form(this.draft, (path) => {
    required(path.title);
    maxLength(path.title, 200);
    validate(path.title, ({ value }) => (value().trim() === '' ? requiredError() : undefined));
  });

  protected readonly showTitleError = computed(
    () => this.editForm.title().touched() && this.editForm.title().invalid(),
  );

  protected async save(): Promise<void> {
    await submit(this.editForm, {
      action: async () => {
        this.renamed.emit(this.draft().title.trim());
        return undefined;
      },
    });
  }
}
