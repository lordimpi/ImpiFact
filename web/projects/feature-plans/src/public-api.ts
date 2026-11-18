/*
 * API pública de feature-plans: plan y consumo de la empresa activa. Solo expone las rutas y los roles que las ven:
 * la página queda en el chunk perezoso `feature-plans` y no pesa en el bundle inicial.
 */
export { PLANS_ROLES, plansRoutes } from './lib/plans.routes';
