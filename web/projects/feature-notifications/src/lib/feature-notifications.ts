/*
 * Punto de entrada perezoso del feature: `notifications.routes.ts` lo carga con `import('./feature-notifications')`,
 * así todo lo de aquí queda en un solo chunk llamado `feature-notifications` (el nombre de su presupuesto).
 */
export { NotificationsDropdownComponent } from './components/notifications-dropdown.component';
export { NotificationListPage } from './pages/notification-list.page';
