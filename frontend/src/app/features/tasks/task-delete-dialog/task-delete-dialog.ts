import { Component, ElementRef, afterRenderEffect, input, output, viewChild } from '@angular/core';
import type { TaskResponse } from '../api/task-response';

@Component({
  selector: 'app-task-delete-dialog',
  templateUrl: './task-delete-dialog.html',
})
export class TaskDeleteDialog {
  readonly task = input<TaskResponse | null>(null);
  readonly confirmed = output();
  readonly cancelled = output();
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    afterRenderEffect(() => {
      const dialog = this.dialog().nativeElement;
      const open = this.task() !== null;
      if (open && !dialog.open) {
        dialog.showModal();
      } else if (!open && dialog.open) {
        dialog.close();
      }
    });
  }

  protected cancel(event?: Event): void {
    event?.preventDefault();
    this.dialog().nativeElement.close();
    this.cancelled.emit();
  }

  protected confirm(): void {
    this.dialog().nativeElement.close();
    this.confirmed.emit();
  }
}
