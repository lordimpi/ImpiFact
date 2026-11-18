/*
 * API pública de feature-masters: clientes y productos de la empresa activa. Solo expone las rutas, los roles que
 * las ven y el scope de textos: las páginas quedan en el chunk perezoso `feature-masters` y no pesan en el bundle
 * inicial.
 */
export { MASTERS_I18N_SCOPE } from './lib/masters-i18n';
export { MASTERS_ROLES, mastersRoutes } from './lib/masters.routes';
