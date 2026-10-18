import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-task-pager',
  templateUrl: './task-pager.html',
  host: { class: 'block' },
})
export class TaskPager {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly disabled = input(false);
  readonly pageChange = output<number>();
}
