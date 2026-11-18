import { HttpErrorResponse } from '@angular/common/http';
import type { ProblemDetails } from '../../../core/problem-details';

const PROJECT_ARCHIVED = 'El proyecto elegido está archivado. Elige otro o deja «Sin proyecto».';
const PROJECT_UNAVAILABLE =
  'El proyecto elegido ya no está disponible. Elige otro o deja «Sin proyecto».';

export function taskProjectErrorMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }
  const problem = error.error as Partial<ProblemDetails> | string | null;
  if (typeof problem !== 'object' || problem === null) {
    return null;
  }
  if (error.status === 409 && problem.code === 'Task.ProjectArchived') {
    return PROJECT_ARCHIVED;
  }
  if (error.status === 400 && problem.errors && 'ProjectId' in problem.errors) {
    return PROJECT_UNAVAILABLE;
  }
  return null;
}
