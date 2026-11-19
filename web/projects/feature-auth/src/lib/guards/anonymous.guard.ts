import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CORE_ROUTE_PATHS, SessionStore } from '@impifact/core';

/** El login es solo para quien no tiene sesión: con sesión (recuperada o no) se va al inicio. */
export const anonymousGuard: CanActivateFn = async () => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const paths = inject(CORE_ROUTE_PATHS);
  await session.restore();
  return session.isAuthenticated() ? router.parseUrl(paths.home) : true;
};
