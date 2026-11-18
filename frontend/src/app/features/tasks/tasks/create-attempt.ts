import type { CreateTaskRequest } from '../api/create-task-request';

export interface CreateAttempt {
  readonly request: CreateTaskRequest;
  readonly key: string;
}

export function nextCreateAttempt(
  last: CreateAttempt | null,
  request: CreateTaskRequest,
): CreateAttempt {
  if (
    last !== null &&
    last.request.title === request.title &&
    last.request.projectId === request.projectId
  ) {
    return last;
  }
  return { request, key: crypto.randomUUID() };
}
