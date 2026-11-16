import { HttpErrorResponse } from '@angular/common/http';
import type { ProblemDetails } from '../../../core/problem-details';

export function taskConflictMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const problem = error.error as ProblemDetails | null;
  return problem?.code === 'Task.Conflict'
    ? 'Otra pestaña o dispositivo cambió esta tarea. Se muestran los datos actuales.'
    : null;
}
