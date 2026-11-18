import { inject } from '@angular/core';
import { CanMatchFn, Routes } from '@angular/router';
import { SessionStore, authGuard, companyGuard } from '@impifact/core';
import { dashboardTextsResolver } from './dashboard-i18n';

/*
 * Tablero en `/` (sección 7 de la arquitectura del frontend), para el propietario y el vendedor. Las demás personas
 * con sesión (cliente, plataforma) siguen a la ruta siguiente de `/`, la página de inicio de la aplicación: por eso
 * la ruta usa `canMatch` y no un guard que redirija a `/` (sería un ciclo). Este archivo entra en el bundle inicial
 * y es mínimo: la página se carga con `import('./feature-dashboard')`, un chunk perezoso con su propio presupuesto.
 */
const loadFeature = () => import('./feature-dashboard');

/** Roles que ven el tablero. */
export const DASHBOARD_ROLES: readonly string[] = ['owner', 'seller'];

/** Solo coincide con sesión y uno de los roles del tablero (espera la recuperación de la sesión al arrancar). */
export const dashboardMatch: CanMatchFn = async () => {
  const session = inject(SessionStore);
  await session.restore();
  return session.isAuthenticated() && session.hasRole(...DASHBOARD_ROLES);
};

export const dashboardRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canMatch: [dashboardMatch],
    canActivate: [authGuard, companyGuard],
    resolve: { texts: dashboardTextsResolver },
    loadComponent: () => loadFeature().then((m) => m.DashboardPage),
  },
];
