import { Service, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { TaskListResponse } from './task-list-response';
import type { TaskResponse } from './task-response';
import type { UpdateTaskRequest } from './update-task-request';

@Service()
export class TasksApi {
  private readonly http = inject(HttpClient);

  list(cursor: string | null, pageSize: number): Promise<TaskListResponse> {
    let params = new HttpParams().set('pageSize', String(pageSize));
    if (cursor !== null) {
      params = params.set('cursor', cursor);
    }
    return firstValueFrom(this.http.get<TaskListResponse>('/api/v1/tasks', { params }));
  }

  create(title: string): Promise<TaskResponse> {
    return firstValueFrom(this.http.post<TaskResponse>('/api/v1/tasks', { title }));
  }

  update(id: string, request: UpdateTaskRequest): Promise<TaskResponse> {
    return firstValueFrom(this.http.put<TaskResponse>(`/api/v1/tasks/${id}`, request));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete(`/api/v1/tasks/${id}`, { responseType: 'text' })).then(
      () => undefined,
    );
  }
}
