import type { Router, UrlTree } from '@angular/router';

const PATHS_WITHOUT_RETURN = new Set(['/', '/login', '/register']);

export function loginUrl(router: Router, returnUrl: string): UrlTree {
  const path = returnUrl.split(/[?#]/)[0];
  return PATHS_WITHOUT_RETURN.has(path)
    ? router.createUrlTree(['/login'])
    : router.createUrlTree(['/login'], { queryParams: { returnUrl } });
}

export function returnTarget(value: string | null): string {
  const internal = value !== null && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\');
  return internal ? value : '/tasks';
}
