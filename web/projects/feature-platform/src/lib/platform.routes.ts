import { Routes } from '@angular/router';
import { authGuard, roleGuard, scopeGuard } from '@impifact/core';
import { platformTextsResolver } from './platform-i18n';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): `/plataforma/planes`, `/plataforma/planes/:id` y
 * `/plataforma/empresas`, para el administrador de la plataforma. El Api da `platform:*` solo a un `platform_admin`
 * sin empresa activa, así que además del rol se exige ese alcance. El revendedor (`reseller`) aún no tiene alcances
 * en el Api y `/plataforma/revendedores` no tiene endpoints: no se ofrecen. Este archivo entra en el bundle inicial
 * y es mínimo: las páginas se cargan con `import('./feature-platform')`, un chunk perezoso con su presupuesto.
 */
const loadFeature = () => import('./feature-platform');

/** Roles que administran la plataforma. */
export const PLATFORM_ROLES: readonly string[] = ['platform_admin'];

/** Alcance del token que piden los endpoints de planes. */
export const PLATFORM_SCOPE = 'platform:*';

export const platformRoutes: Routes = [
  {
    path: 'plataforma',
    canActivateChild: [authGuard, roleGuard(PLATFORM_ROLES), scopeGuard(PLATFORM_SCOPE)],
    resolve: { texts: platformTextsResolver },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'planes' },
      { path: 'planes', loadComponent: () => loadFeature().then((m) => m.PlatformPlansPage) },
      {
        path: 'planes/:id',
        loadComponent: () => loadFeature().then((m) => m.PlanCompaniesPage),
      },
      {
        path: 'empresas',
        loadComponent: () => loadFeature().then((m) => m.PlatformCompaniesPage),
      },
    ],
  },
];
