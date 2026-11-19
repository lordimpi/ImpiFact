/*
 * API pública de feature-platform: administración de la plataforma (planes y su asignación a empresas). Solo expone
 * las rutas, los roles y el alcance que las ven: las páginas quedan en el chunk perezoso `feature-platform`.
 */
export { PLATFORM_ROLES, PLATFORM_SCOPE, platformRoutes } from './lib/platform.routes';
