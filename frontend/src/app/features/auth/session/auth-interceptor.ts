import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { Session } from './session';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(Session);
  const router = inject(Router);

  const token = session.accessToken();
  const outgoing = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !request.url.startsWith('/api/v1/auth/')
      ) {
        return from(session.refresh()).pipe(
          switchMap(() => {
            const retryToken = session.accessToken();
            const retry = retryToken
              ? request.clone({ setHeaders: { Authorization: `Bearer ${retryToken}` } })
              : request;
            return next(retry);
          }),
          catchError(() => {
            session.end();
            void router.navigateByUrl('/login');
            return throwError(() => error);
          }),
        );
      }
      return throwError(() => error);
    }),
  );
};
