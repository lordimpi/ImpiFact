/*
 * API pública de feature-documents: lista, detalle con seguimiento del estado en la DIAN y emisión de factura. Solo
 * expone las rutas y el scope de textos: las páginas quedan en el chunk perezoso `feature-documents` y no pesan en el
 * bundle inicial.
 */
export { DOCUMENTS_I18N_SCOPE, loadDocumentsTexts } from './lib/documents-i18n';
export { ISSUE_SCOPE, documentsRoutes } from './lib/documents.routes';
