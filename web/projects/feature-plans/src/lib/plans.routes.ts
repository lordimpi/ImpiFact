import { Routes } from '@angular/router';
import { authGuard, companyGuard, roleGuard } from '@impifact/core';
import { plansTextsResolver } from './plans-i18n';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): `/plan`, el plan y el consumo de la empresa
 * activa, solo para el propietario. Este archivo entra en el bundle inicial y es mínimo: la página se carga con
 * `import('./feature-plans')`, un solo chunk perezoso con su propio presupuesto en `angular.json`.
 */
const loadFeature = () => import('./feature-plans');

/** Roles que ven el plan de la empresa. */
export const PLANS_ROLES: readonly string[] = ['owner'];

export const plansRoutes: Routes = [
  {
    path: 'plan',
    canActivate: [authGuard, companyGuard, roleGuard(PLANS_ROLES)],
    resolve: { texts: plansTextsResolver },
    loadComponent: () => loadFeature().then((m) => m.CurrentPlanPage),
  },
];
