import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Rutas privadas: esperan a que el arranque termine y exigen sesión. */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ready().then(() => (auth.isAuthenticated() ? true : router.createUrlTree(['/login'])));
};

/** Login y registro: si ya hay sesión, van directas a las tareas. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ready().then(() => (auth.isAuthenticated() ? router.createUrlTree(['/tasks']) : true));
};
