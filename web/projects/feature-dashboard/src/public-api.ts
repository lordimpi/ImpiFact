/*
 * API pública de feature-dashboard: el tablero de `/`. Solo expone las rutas y los roles que lo ven: la página
 * queda en el chunk perezoso `feature-dashboard` y no pesa en el bundle inicial.
 */
export { DASHBOARD_ROLES, dashboardMatch, dashboardRoutes } from './lib/dashboard.routes';
