import { Type } from '@angular/core';
import { Routes } from '@angular/router';
import { authGuard } from '@impifact/core';
import { authTextsResolver } from './auth-i18n';
import { anonymousGuard } from './guards/anonymous.guard';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend). Este archivo entra en el bundle inicial y es mínimo:
 * las páginas se cargan con `import('./feature-auth')`, un solo chunk perezoso llamado `feature-auth`, que tiene su
 * propio presupuesto en `angular.json`.
 */
const loadFeature = () => import('./feature-auth');

export const authRoutes: Routes = [
  {
    path: 'login',
    canActivate: [anonymousGuard],
    resolve: { texts: authTextsResolver },
    loadComponent: () => loadFeature().then((m) => m.LoginPage),
  },
  {
    path: 'recuperar',
    resolve: { texts: authTextsResolver },
    loadComponent: () => loadFeature().then((m) => m.PasswordRecoveryPage),
  },
  {
    path: 'empresas/seleccionar',
    canActivate: [authGuard],
    resolve: { texts: authTextsResolver },
    loadComponent: () => loadFeature().then((m) => m.CompanySelectionPage),
  },
];

/**
 * Carga el diálogo de inactividad (con sus textos ya cargados aparte, ver `loadAuthTexts`). El shell lo pide la
 * primera vez que `InactivityMonitor` avisa, así el diálogo no pesa en el bundle inicial ni en el del login: va en su
 * propio chunk, `feature-auth-inactivity`.
 */
export const loadInactivityWarning = (): Promise<Type<unknown>> =>
  import('./feature-auth-inactivity').then((m) => m.InactivityWarningComponent);
