import { Routes } from '@angular/router';

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

// Rutas raíz. Cada feature se agrega con loadChildren (carga perezosa) desde su librería.
export const routes: Routes = [
  { path: '', loadComponent: () => import('./home/home.page').then((m) => m.HomePage) },
  ...devRoutes,
  { path: '**', redirectTo: '' },
];
