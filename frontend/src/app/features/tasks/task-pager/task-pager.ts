import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-task-pager',
  templateUrl: './task-pager.html',
  host: { class: 'block' },
})
export class TaskPager {
  readonly page = input.required<number>();
  readonly hasPrevious = input.required<boolean>();
  readonly hasNext = input.required<boolean>();
  readonly disabled = input(false);
  readonly previous = output();
  readonly next = output();
}
