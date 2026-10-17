import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Session } from './session';

export const authGuard: CanActivateFn = () => {
  const session = inject(Session);
  const router = inject(Router);
  return session.ready().then(() => (session.isAuthenticated() ? true : router.createUrlTree(['/login'])));
};

export const guestGuard: CanActivateFn = () => {
  const session = inject(Session);
  const router = inject(Router);
  return session.ready().then(() => (session.isAuthenticated() ? router.createUrlTree(['/tasks']) : true));
};
