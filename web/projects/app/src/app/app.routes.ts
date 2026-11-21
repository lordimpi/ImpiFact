import { Routes } from '@angular/router';
import { authGuard } from '@impifact/core';
import { authRoutes } from '@impifact/feature-auth';
import { mastersRoutes } from '@impifact/feature-masters';
import { documentsRoutes } from '@impifact/feature-documents';
import { companiesRoutes } from '@impifact/feature-companies';
import { notificationsRoutes } from '@impifact/feature-notifications';
import { apiKeysRoutes } from '@impifact/feature-api-keys';
import { plansRoutes } from '@impifact/feature-plans';
import { dashboardRoutes } from '@impifact/feature-dashboard';
import { platformRoutes } from '@impifact/feature-platform';

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
 * páginas se cargan de forma perezosa: `authRoutes` trae `/login`, `/recuperar` y `/empresas/seleccionar`;
 * `mastersRoutes`, `/clientes` y `/productos`.
 * `documentsRoutes`, `/documentos`, `/documentos/nuevo` y `/documentos/:id`; `notificationsRoutes`, `/notificaciones`;
 * `apiKeysRoutes`, `/llaves-api`; `plansRoutes`, `/plan`; `platformRoutes`, `/plataforma/*`; `dashboardRoutes`, el
 * tablero de `/` para el propietario y el vendedor.
 * `companiesRoutes`, `/empresa` y `/empresas/:id/...` (configuración y paso a producción); va después de
 * `authRoutes` para que `/empresas/seleccionar` no se tome por una empresa. También trae
 * `/plataforma/empresas/:nit/usuarios` (los usuarios de una empresa para la plataforma, junto a su chunk), así que va
 * antes que `platformRoutes`.
 * Las rutas del panel exigen sesión con `authGuard`, que manda al login con `returnUrl`.
 */
export const routes: Routes = [
  // El tablero toma `/` para el propietario y el vendedor; los demás siguen a la página de inicio.
  ...dashboardRoutes,
  {
    path: '',
    pathMatch: 'full',
    canActivate: [authGuard],
    loadComponent: () => import('./home/home.page').then((m) => m.HomePage),
  },
  ...authRoutes,
  ...mastersRoutes,
  ...documentsRoutes,
  ...companiesRoutes,
  ...notificationsRoutes,
  ...apiKeysRoutes,
  ...plansRoutes,
  ...platformRoutes,
  ...devRoutes,
  { path: '**', redirectTo: '' },
];
