import { HttpErrorResponse } from '@angular/common/http';
import type { ProblemDetails } from '../../../core/problem-details';

const MESSAGES = new Map<string, string>([
  ['Project.NameTaken', 'Ya tienes un proyecto con ese nombre.'],
  ['Project.Archived', 'El proyecto está archivado. Restáuralo antes de cambiar su nombre.'],
  [
    'Project.Conflict',
    'Otra pestaña o dispositivo cambió este proyecto. Se muestran los datos actuales.',
  ],
]);

export function projectErrorMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const problem = error.error as ProblemDetails | null;
  return problem?.code ? (MESSAGES.get(problem.code) ?? null) : null;
}
