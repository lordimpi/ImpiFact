import { Routes } from '@angular/router';
import { authGuard } from '@impifact/core';
import { authRoutes } from '@impifact/feature-auth';

/*
 * Rutas solo para desarrollo. En el build de producción `ngDevMode` vale `false` en tiempo de compilación,
 * así que el compilador elimina esta rama y la vitrina no llega al bundle.
 */
const devRoutes: Routes =
  typeof ngDevMode === 'undefined' || ngDevMode
    ? [
        {
          path: 'dev/ui',
          loadComponent: () => import('./dev/ui-showcase.page').then((m) => m.UiShowcasePage),
        },
      ]
    : [];

/*
 * Rutas raíz (sección 7 de la arquitectura del frontend). Cada feature aporta las suyas desde su librería y sus
 * páginas se cargan de forma perezosa: `authRoutes` trae `/login`, `/recuperar` y `/empresas/seleccionar`.
 * Las rutas del panel exigen sesión con `authGuard`, que manda al login con `returnUrl`.
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [authGuard],
    loadComponent: () => import('./home/home.page').then((m) => m.HomePage),
  },
  ...authRoutes,
  ...devRoutes,
  { path: '**', redirectTo: '' },
];
