/*
 * API pública de feature-companies: configuración de la empresa activa, asistente de paso a producción y usuarios de
 * la empresa. Solo expone las rutas, los roles que las ven y el scope de textos: las pantallas quedan en los chunks
 * perezosos `feature-companies`, `feature-production-wizard` y `feature-company-users` y no pesan en el bundle
 * inicial.
 */
export { COMPANIES_I18N_SCOPE } from './lib/companies-i18n';
export { COMPANY_ROLES, companiesRoutes } from './lib/companies.routes';
