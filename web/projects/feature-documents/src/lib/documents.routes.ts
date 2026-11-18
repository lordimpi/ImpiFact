import { Routes } from '@angular/router';
import { companyGuard, roleGuard, scopeGuard, unsavedChangesGuard } from '@impifact/core';
import { documentsTextsResolver } from './documents-i18n';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): `/documentos`, `/documentos/nuevo` y
 * `/documentos/:id`, para el propietario y el vendedor con una empresa activa; emitir pide además el alcance
 * `documents:write`. Este archivo entra en el bundle inicial y es mínimo: las páginas se cargan con
 * `import('./feature-documents')`, un solo chunk perezoso llamado `feature-documents` con su presupuesto en
 * `angular.json`.
 */
const loadFeature = () => import('./feature-documents');

/** Alcance que da el Api a quien puede emitir (propietario y vendedor). */
export const ISSUE_SCOPE = 'documents:write';

export const documentsRoutes: Routes = [
  {
    path: 'documentos',
    canActivate: [roleGuard(['owner', 'seller']), companyGuard],
    resolve: { texts: documentsTextsResolver },
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => loadFeature().then((m) => m.DocumentListPage),
      },
      {
        path: 'nuevo',
        canActivate: [scopeGuard(ISSUE_SCOPE)],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => loadFeature().then((m) => m.InvoiceIssuePage),
      },
      {
        path: ':id',
        loadComponent: () => loadFeature().then((m) => m.DocumentDetailPage),
      },
    ],
  },
];
