import type { TaskResponse } from './task-response';

export interface TaskListResponse {
  items: TaskResponse[];
  totalCount: number;
  page: number;
  pageSize: number;
}
