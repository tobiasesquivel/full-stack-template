export interface TaskResponse {
  id: string;
  title: string;
  isCompleted: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface TaskListResponse {
  items: TaskResponse[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface UpdateTaskPayload {
  title: string;
  isCompleted: boolean;
}
