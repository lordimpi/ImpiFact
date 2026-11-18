/*
 * Punto de entrada perezoso del feature: `documents.routes.ts` lo carga con `import('./feature-documents')`, así
 * todo lo de aquí queda en un solo chunk llamado `feature-documents` (el nombre que usa su presupuesto en
 * `angular.json`).
 */
export { DocumentDetailPage } from './pages/document-detail.page';
export { DocumentListPage } from './pages/document-list.page';
export { InvoiceIssuePage } from './pages/invoice-issue.page';
