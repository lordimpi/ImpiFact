/*
 * Punto de entrada perezoso de las pantallas de la empresa: `companies.routes.ts` lo carga con
 * `import('./feature-companies')`, así todo lo de aquí queda en un chunk llamado `feature-companies` (el nombre que
 * usa su presupuesto en `angular.json`).
 */
export { CertificatePage } from './pages/certificate.page';
export { CompanyDataPage } from './pages/company-data.page';
export { CompanyLayoutPage } from './pages/company-layout.page';
export { EnvironmentsPage } from './pages/environments.page';
export { ResolutionsPage } from './pages/resolutions.page';
export { SoftwarePage } from './pages/software.page';
