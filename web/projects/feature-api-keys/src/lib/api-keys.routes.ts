import { Routes } from '@angular/router';
import { authGuard, companyGuard, roleGuard } from '@impifact/core';
import { apiKeysTextsResolver } from './api-keys-i18n';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): `/llaves-api`, solo para el propietario de la
 * empresa activa. Este archivo entra en el bundle inicial y es mínimo: la página se carga con
 * `import('./feature-api-keys')`, un solo chunk perezoso con su propio presupuesto en `angular.json`. El Api exige
 * `config:write` para crear y revocar llaves.
 */
const loadFeature = () => import('./feature-api-keys');

/** Roles que administran las llaves de API. */
export const API_KEYS_ROLES: readonly string[] = ['owner'];

export const apiKeysRoutes: Routes = [
  {
    path: 'llaves-api',
    canActivate: [authGuard, companyGuard, roleGuard(API_KEYS_ROLES)],
    resolve: { texts: apiKeysTextsResolver },
    loadComponent: () => loadFeature().then((m) => m.ApiKeysPage),
  },
];
