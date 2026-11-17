import { Routes } from '@angular/router';

// Rutas raíz. Cada feature se agrega con loadChildren (carga perezosa) desde su librería.
export const routes: Routes = [
  { path: '', loadComponent: () => import('./home/home.page').then((m) => m.HomePage) },
  { path: '**', redirectTo: '' },
];
