import { Type } from '@angular/core';
import { Routes } from '@angular/router';
import { authGuard, companyGuard } from '@impifact/core';
import { notificationsTextsResolver } from './notifications-i18n';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): `/notificaciones`, para cualquier rol con empresa
 * activa. Este archivo entra en el bundle inicial y es mínimo: la página y la lista de la campana se cargan con
 * `import('./feature-notifications')`, un solo chunk perezoso con su propio presupuesto en `angular.json`.
 */
const loadFeature = () => import('./feature-notifications');

/** Roles de empresa que ven los avisos en el menú (todos los que entran con una empresa activa). */
export const NOTIFICATIONS_ROLES: readonly string[] = ['owner', 'seller', 'customer'];

export const notificationsRoutes: Routes = [
  {
    path: 'notificaciones',
    canActivate: [authGuard, companyGuard],
    resolve: { texts: notificationsTextsResolver },
    loadComponent: () => loadFeature().then((m) => m.NotificationListPage),
  },
];

/** Lista desplegable de la campana; el shell la carga la primera vez que se abre. */
export function loadNotificationsDropdown(): Promise<Type<unknown>> {
  return loadFeature().then((m) => m.NotificationsDropdownComponent);
}
