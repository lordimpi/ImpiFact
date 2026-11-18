import { Routes } from '@angular/router';
import { authGuard, companyGuard, roleGuard } from '@impifact/core';
import { mastersTextsResolver } from './masters-i18n';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): `/clientes` y `/productos`, para el propietario y
 * el vendedor de la empresa activa. Este archivo entra en el bundle inicial y es mínimo: las páginas se cargan con
 * `import('./feature-masters')`, un solo chunk perezoso llamado `feature-masters`, que tiene su propio presupuesto
 * en `angular.json`. Los guards solo ocultan pantallas; el Api valida rol, alcance y empresa en cada endpoint.
 */
const loadFeature = () => import('./feature-masters');

/** Roles que ven clientes y productos. */
export const MASTERS_ROLES: readonly string[] = ['owner', 'seller'];

const access = {
  canActivate: [authGuard, companyGuard, roleGuard(MASTERS_ROLES)],
  resolve: { texts: mastersTextsResolver },
};

export const mastersRoutes: Routes = [
  {
    path: 'clientes',
    ...access,
    loadComponent: () => loadFeature().then((m) => m.CustomerListPage),
  },
  {
    path: 'productos',
    ...access,
    loadComponent: () => loadFeature().then((m) => m.ProductListPage),
  },
];
