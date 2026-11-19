import { InjectionToken, inject } from '@angular/core';
import { CanActivateFn, CanDeactivateFn, GuardResult, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { CORE_ROUTE_PATHS } from '../session/route-paths';
import { SessionStore } from '../session/session.store';

/*
 * Guards funcionales de las rutas (sección 6 de la arquitectura). Ocultan pantallas a quien no corresponde, pero
 * la autoridad es el Api: cada endpoint valida rol, alcance y empresa por su cuenta.
 * Sirven en `canActivate` y en `canActivateChild`.
 */

/** Espera la recuperación de la sesión y, sin sesión, manda al login con la ruta pedida en `returnUrl`. */
async function requireSession(url: string): Promise<GuardResult> {
  const session = inject(SessionStore);
  const router = inject(Router);
  const paths = inject(CORE_ROUTE_PATHS);
  await session.restore();
  return session.isAuthenticated()
    ? true
    : router.createUrlTree([paths.login], { queryParams: { returnUrl: url } });
}

/** Exige sesión. */
export const authGuard: CanActivateFn = (_route, state) => requireSession(state.url);

/** Exige sesión y al menos uno de los roles (`owner`, `seller`, `customer`, `reseller`, `platform_admin`). */
export function roleGuard(roles: readonly string[]): CanActivateFn {
  return async (_route, state) => {
    const session = inject(SessionStore);
    const router = inject(Router);
    const paths = inject(CORE_ROUTE_PATHS);
    const signedIn = await requireSession(state.url);
    if (signedIn !== true) {
      return signedIn;
    }
    return session.hasRole(...roles) ? true : router.parseUrl(paths.home);
  };
}

/**
 * Exige sesión con el alcance (`scope` del token, por ejemplo `documents:write` para emitir); sin él, manda al
 * inicio. El Api vuelve a validarlo en cada endpoint.
 */
export function scopeGuard(scope: string): CanActivateFn {
  return async (_route, state) => {
    const session = inject(SessionStore);
    const router = inject(Router);
    const paths = inject(CORE_ROUTE_PATHS);
    const signedIn = await requireSession(state.url);
    if (signedIn !== true) {
      return signedIn;
    }
    return session.hasScope(scope) ? true : router.parseUrl(paths.home);
  };
}

/** Exige sesión con empresa activa; si no hay, manda a la selección de empresa. */
export const companyGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const paths = inject(CORE_ROUTE_PATHS);
  const signedIn = await requireSession(state.url);
  if (signedIn !== true) {
    return signedIn;
  }
  return session.companyId() ? true : router.parseUrl(paths.selectCompany);
};

/** Lo implementa la página con un formulario que se puede abandonar a medias. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Pregunta de confirmación antes de salir con cambios sin guardar. Por defecto, el diálogo del navegador con el
 * texto `core.unsavedChanges`; un feature puede darle uno propio (por ejemplo, con `ui-dialog`).
 */
export const UNSAVED_CHANGES_CONFIRM = new InjectionToken<
  (message: string) => boolean | Promise<boolean>
>('UNSAVED_CHANGES_CONFIRM', {
  providedIn: 'root',
  factory: () => (message: string) => window.confirm(message),
});

/** Pide confirmación al salir de una página con cambios sin guardar. */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = async (component) => {
  if (!component?.hasUnsavedChanges()) {
    return true;
  }
  const confirm = inject(UNSAVED_CHANGES_CONFIRM);
  return confirm(inject(TranslocoService).translate('core.unsavedChanges'));
};
