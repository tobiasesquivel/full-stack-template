import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { first, map } from 'rxjs';
import { loginUrl } from './return-url';
import { Session } from './session';

function restored(session: Session) {
  return toObservable(session.status).pipe(first((status) => status !== 'restoring'));
}

export const authGuard: CanActivateFn = (_route, state) => {
  const session = inject(Session);
  const router = inject(Router);
  return restored(session).pipe(
    map((status) => (status === 'open' ? true : loginUrl(router, state.url))),
  );
};

export const guestGuard: CanActivateFn = () => {
  const session = inject(Session);
  const router = inject(Router);
  return restored(session).pipe(
    map((status) => (status === 'open' ? router.createUrlTree(['/tasks']) : true)),
  );
};
