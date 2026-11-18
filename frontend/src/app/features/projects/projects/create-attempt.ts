import type { CreateProjectRequest } from '../api/create-project-request';

export interface CreateAttempt {
  readonly request: CreateProjectRequest;
  readonly key: string;
}

export function nextCreateAttempt(
  last: CreateAttempt | null,
  request: CreateProjectRequest,
): CreateAttempt {
  if (last !== null && last.request.name === request.name) {
    return last;
  }
  return { request, key: crypto.randomUUID() };
}
