/*
 * API pública de feature-auth: inicio de sesión, recuperación de contraseña, selección de empresa y diálogo de
 * inactividad. Solo expone las rutas, el cargador del diálogo y el scope de textos: las páginas quedan en el chunk
 * perezoso `feature-auth` y no pesan en el bundle inicial.
 */
export { AUTH_I18N_SCOPE, loadAuthTexts } from './lib/auth-i18n';
export { authRoutes, loadInactivityWarning } from './lib/auth.routes';
