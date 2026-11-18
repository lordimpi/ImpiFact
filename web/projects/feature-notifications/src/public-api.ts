/*
 * API pública de feature-notifications: la página `/notificaciones` y la lista desplegable de la campana. Solo
 * expone las rutas, el cargador de la lista y el de sus textos: lo demás queda en el chunk perezoso.
 */
export { NOTIFICATIONS_I18N_SCOPE, loadNotificationsTexts } from './lib/notifications-i18n';
export {
  NOTIFICATIONS_ROLES,
  loadNotificationsDropdown,
  notificationsRoutes,
} from './lib/notifications.routes';
