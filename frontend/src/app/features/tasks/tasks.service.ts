import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { TaskListResponse, TaskResponse, UpdateTaskPayload } from './tasks-models';

@Injectable({ providedIn: 'root' })
export class TasksService {
  private readonly http = inject(HttpClient);

  list(page: number, pageSize: number): Promise<TaskListResponse> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return firstValueFrom(this.http.get<TaskListResponse>('/api/v1/tasks', { params }));
  }

  create(title: string): Promise<TaskResponse> {
    return firstValueFrom(this.http.post<TaskResponse>('/api/v1/tasks', { title }));
  }

  /** PUT con estado final: el cliente siempre envía título y completado. */
  update(id: string, payload: UpdateTaskPayload): Promise<TaskResponse> {
    return firstValueFrom(this.http.put<TaskResponse>(`/api/v1/tasks/${id}`, payload));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete(`/api/v1/tasks/${id}`, { responseType: 'text' })).then(
      () => undefined,
    );
  }
}
