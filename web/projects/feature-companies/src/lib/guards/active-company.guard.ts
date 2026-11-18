import { inject } from '@angular/core';
import { CanActivateFn, GuardResult, Router } from '@angular/router';
import { CORE_ROUTE_PATHS, SessionStore, companyGuard } from '@impifact/core';

/*
 * Guards del feature. El Api solo deja cambiar la configuración de la empresa activa de la sesión (la de la
 * credencial), así que `/empresas/:id` exige que `:id` sea esa empresa. Los guards solo ocultan pantallas: el Api
 * valida rol, alcance y empresa en cada endpoint.
 */

/**
 * `:id` debe ser la empresa activa; si es otra, manda a la selección de empresa y vuelve a esta ruta después de
 * elegir.
 */
export const activeCompanyGuard: CanActivateFn = async (route, state) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const paths = inject(CORE_ROUTE_PATHS);
  // companyGuard es asíncrono: siempre resuelve a un resultado, nunca a un Observable.
  const allowed = (await companyGuard(route, state)) as GuardResult;
  if (allowed !== true) {
    return allowed;
  }
  return route.paramMap.get('id') === session.companyId()
    ? true
    : router.createUrlTree([paths.selectCompany], { queryParams: { returnUrl: state.url } });
};

/** Atajo sin identificador (`/empresa`, `/empresa/produccion`): lleva a la sección de la empresa activa. */
export function redirectToActiveCompany(section: string): CanActivateFn {
  return async (route, state) => {
    const session = inject(SessionStore);
    const router = inject(Router);
    // companyGuard es asíncrono: siempre resuelve a un resultado, nunca a un Observable.
    const allowed = (await companyGuard(route, state)) as GuardResult;
    if (allowed !== true) {
      return allowed;
    }
    return router.createUrlTree(['/empresas', session.companyId(), section]);
  };
}
