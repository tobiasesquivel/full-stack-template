import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.token();
  const outgoing = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !request.url.startsWith('/api/v1/auth/')
      ) {
        return from(auth.refreshNow()).pipe(
          switchMap(() => {
            const retryToken = auth.token();
            const retry = retryToken
              ? request.clone({ setHeaders: { Authorization: `Bearer ${retryToken}` } })
              : request;
            return next(retry);
          }),
          catchError(() => {
            auth.clearSession();
            void router.navigateByUrl('/login');
            return throwError(() => error);
          }),
        );
      }
      return throwError(() => error);
    }),
  );
};
