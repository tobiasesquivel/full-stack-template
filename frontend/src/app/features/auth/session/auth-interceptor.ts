import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { SKIP_SESSION } from '../api/skip-session';
import { Session } from './session';

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/') || request.context.get(SKIP_SESSION)) {
    return next(request);
  }
  const session = inject(Session);

  return next(withToken(request, session.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return from(session.refresh()).pipe(
        catchError(() => {
          session.end();
          return throwError(() => error);
        }),
        switchMap(() => next(withToken(request, session.accessToken()))),
      );
    }),
  );
};
