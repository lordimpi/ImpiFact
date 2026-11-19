/*
 * Punto de entrada perezoso del feature: `plans.routes.ts` lo carga con `import('./feature-plans')`, así todo lo de
 * aquí queda en un solo chunk llamado `feature-plans` (el nombre de su presupuesto en `angular.json`).
 */
export { CurrentPlanPage } from './pages/current-plan.page';
