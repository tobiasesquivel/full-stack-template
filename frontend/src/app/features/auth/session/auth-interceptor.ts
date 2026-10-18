import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { SKIP_SESSION } from '../api/skip-session';
import { Session } from './session';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/') || request.context.get(SKIP_SESSION)) {
    return next(request);
  }
  const session = inject(Session);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && session.isOpen()) {
        session.end();
      }
      return throwError(() => error);
    }),
  );
};
