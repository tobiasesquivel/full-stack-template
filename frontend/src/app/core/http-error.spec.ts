import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { describeHttpError } from './http-error';

function httpError(
  status: number,
  error: unknown = null,
  headers?: Record<string, string>,
): HttpErrorResponse {
  return new HttpErrorResponse({ status, error, headers: new HttpHeaders(headers) });
}

describe('describeHttpError', () => {
  it('describes an error that does not come from HTTP', () => {
    expect(describeHttpError(new Error('boom'))).toBe('Error inesperado. Inténtalo de nuevo.');
  });

  it('describes a lost connection', () => {
    expect(describeHttpError(httpError(0))).toBe(
      'Sin conexión con el servidor. Revisa tu red y pulsa Reintentar.',
    );
  });

  it('joins the validation messages of a 400 ProblemDetails', () => {
    const problem = {
      title: 'Validation',
      errors: { Title: ['El título es obligatorio.'], Page: ['Página inválida.'] },
    };
    expect(describeHttpError(httpError(400, problem))).toBe(
      'El título es obligatorio. Página inválida.',
    );
  });

  it('uses the detail of a 400 without validation messages, or a fixed text', () => {
    expect(describeHttpError(httpError(400, { detail: 'Zona horaria desconocida.' }))).toBe(
      'Zona horaria desconocida.',
    );
    expect(describeHttpError(httpError(400, 'plain text'))).toBe(
      'Los datos enviados no son válidos. Revísalos e inténtalo de nuevo.',
    );
  });

  it('gives a fixed text for 403 and 404 even with a detail', () => {
    expect(describeHttpError(httpError(403, { detail: 'internal' }))).toBe(
      'No tienes permiso para realizar esta acción.',
    );
    expect(describeHttpError(httpError(404, { detail: 'internal' }))).toBe(
      'El recurso solicitado no existe o ya fue eliminado.',
    );
  });

  it('prefers the detail for 401, 409 and 500, with a fixed text as fallback', () => {
    expect(describeHttpError(httpError(401))).toBe('La sesión ha caducado. Vuelve a entrar.');
    expect(describeHttpError(httpError(409, { detail: 'El usuario ya existe.' }))).toBe(
      'El usuario ya existe.',
    );
    expect(describeHttpError(httpError(409))).toBe('Ya existe un registro con esos datos.');
    expect(describeHttpError(httpError(500))).toBe(
      'Error inesperado del servidor. Inténtalo más tarde.',
    );
  });

  it('shows a fixed text for the coded 401 of the session and the coded 500', () => {
    const sessionRequired = {
      status: 401,
      title: 'An active session is required.',
      code: 'Auth.SessionRequired',
      traceId: '00-trace-span-00',
    };
    const unexpected = {
      status: 500,
      title: 'An unexpected error occurred.',
      code: 'Http.Unexpected',
      traceId: '00-trace-span-00',
    };
    expect(describeHttpError(httpError(401, sessionRequired))).toBe(
      'La sesión ha caducado. Vuelve a entrar.',
    );
    expect(describeHttpError(httpError(500, unexpected))).toBe(
      'Error inesperado del servidor. Inténtalo más tarde.',
    );
  });

  it('asks to wait the seconds of Retry-After when the API limits the requests', () => {
    const tooMany = {
      status: 429,
      title: 'Too Many Requests',
      code: 'Http.TooManyRequests',
      traceId: '00-trace-span-00',
    };
    expect(describeHttpError(httpError(429, tooMany, { 'Retry-After': '60' }))).toBe(
      'Demasiadas solicitudes. Vuelve a intentarlo en 60 segundos.',
    );
    expect(describeHttpError(httpError(429, tooMany))).toBe(
      'Demasiadas solicitudes. Espera un momento y vuelve a intentarlo.',
    );
  });

  it('says the server took too long when the gateway answers 504', () => {
    const gatewayTimeout = {
      status: 504,
      title: 'Gateway Timeout',
      code: 'Http.GatewayTimeout',
      traceId: '00-trace-span-00',
    };
    expect(describeHttpError(httpError(504, gatewayTimeout))).toBe(
      'El servidor tardó demasiado en responder. Inténtalo de nuevo.',
    );
  });
});
