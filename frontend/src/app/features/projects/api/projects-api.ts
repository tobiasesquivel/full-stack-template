import { Service, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { CreateProjectRequest } from './create-project-request';
import type { ProjectListResponse } from './project-list-response';
import type { ProjectResponse } from './project-response';
import type { UpdateProjectRequest } from './update-project-request';

@Service()
export class ProjectsApi {
  private readonly http = inject(HttpClient);

  list(cursor: string | null, pageSize: number): Promise<ProjectListResponse> {
    let params = new HttpParams().set('pageSize', String(pageSize));
    if (cursor !== null) {
      params = params.set('cursor', cursor);
    }
    return firstValueFrom(this.http.get<ProjectListResponse>('/api/v1/projects', { params }));
  }

  create(request: CreateProjectRequest, idempotencyKey: string): Promise<ProjectResponse> {
    return firstValueFrom(
      this.http.post<ProjectResponse>('/api/v1/projects', request, {
        headers: { 'Idempotency-Key': idempotencyKey },
      }),
    );
  }

  update(id: string, request: UpdateProjectRequest): Promise<ProjectResponse> {
    return firstValueFrom(this.http.put<ProjectResponse>(`/api/v1/projects/${id}`, request));
  }
}
