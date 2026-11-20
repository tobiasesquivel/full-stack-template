import { HttpErrorResponse } from '@angular/common/http';
import type { ProblemDetails } from './problem-details';

function asProblem(error: HttpErrorResponse): ProblemDetails | null {
  const body = error.error as Partial<ProblemDetails> | string | null | undefined;
  if (
    body &&
    typeof body === 'object' &&
    ('title' in body || 'detail' in body || 'errors' in body)
  ) {
    return body as ProblemDetails;
  }
  return null;
}

function validationText(problem: ProblemDetails | null): string | null {
  if (!problem || !problem.errors) {
    return null;
  }
  const messages = Object.values(problem.errors).flat();
  return messages.length > 0 ? messages.join(' ') : null;
}

function retryText(retryAfter: string | null): string {
  const seconds = retryAfter === null ? NaN : Number(retryAfter);
  return Number.isInteger(seconds) && seconds > 0
    ? `Demasiadas solicitudes. Vuelve a intentarlo en ${seconds} segundos.`
    : 'Demasiadas solicitudes. Espera un momento y vuelve a intentarlo.';
}

export function describeHttpError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Error inesperado. Inténtalo de nuevo.';
  }
  if (error.status === 0) {
    return 'Sin conexión con el servidor. Revisa tu red y pulsa Reintentar.';
  }
  const problem = asProblem(error);
  const detail = problem && problem.detail ? problem.detail : null;
  switch (error.status) {
    case 400:
      return (
        validationText(problem) ??
        detail ??
        'Los datos enviados no son válidos. Revísalos e inténtalo de nuevo.'
      );
    case 401:
      return detail ?? 'La sesión ha caducado. Vuelve a entrar.';
    case 403:
      return 'No tienes permiso para realizar esta acción.';
    case 404:
      return 'El recurso solicitado no existe o ya fue eliminado.';
    case 409:
      return detail ?? 'Ya existe un registro con esos datos.';
    case 429:
      return retryText(error.headers.get('Retry-After'));
    case 504:
      return 'El servidor tardó demasiado en responder. Inténtalo de nuevo.';
    default:
      return detail ?? 'Error inesperado del servidor. Inténtalo más tarde.';
  }
}
