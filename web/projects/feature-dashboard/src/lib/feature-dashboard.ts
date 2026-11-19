/*
 * Punto de entrada perezoso del feature: `dashboard.routes.ts` lo carga con `import('./feature-dashboard')`, así todo
 * lo de aquí queda en un solo chunk llamado `feature-dashboard` (el nombre de su presupuesto en `angular.json`).
 */
export { DashboardPage } from './pages/dashboard.page';
