/*
 * API pública de feature-companies: configuración de la empresa activa y asistente de paso a producción. Solo expone
 * las rutas, los roles que las ven y el scope de textos: las pantallas quedan en los chunks perezosos
 * `feature-companies` y `feature-production-wizard` y no pesan en el bundle inicial.
 */
export { COMPANIES_I18N_SCOPE } from './lib/companies-i18n';
export { COMPANY_ROLES, companiesRoutes } from './lib/companies.routes';
