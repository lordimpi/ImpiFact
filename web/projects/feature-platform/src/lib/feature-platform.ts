/*
 * Punto de entrada perezoso del feature: `platform.routes.ts` lo carga con `import('./feature-platform')`, así todo
 * lo de aquí queda en un solo chunk llamado `feature-platform` (el nombre de su presupuesto en `angular.json`).
 */
export { AssignPlanPage } from './pages/assign-plan.page';
export { PlanCompaniesPage } from './pages/plan-companies.page';
export { PlatformPlansPage } from './pages/platform-plans.page';
