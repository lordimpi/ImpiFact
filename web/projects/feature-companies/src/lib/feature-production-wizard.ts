/*
 * Punto de entrada perezoso del asistente de paso a producción: `companies.routes.ts` lo carga con
 * `import('./feature-production-wizard')`, un chunk propio llamado `feature-production-wizard` con su presupuesto en
 * `angular.json`. Comparte con las pantallas de la empresa el store `CompanySetupStore` (queda en un chunk común).
 */
export { ProductionWizardPage } from './pages/production-wizard.page';
