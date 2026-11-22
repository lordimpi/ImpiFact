import { Routes } from '@angular/router';
import { companyGuard, roleGuard, scopeGuard, unsavedChangesGuard } from '@impifact/core';
import { documentsTextsResolver } from './documents-i18n';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): `/documentos`, `/documentos/nuevo`,
 * `/documentos/documento-soporte`, `/documentos/nota-credito`, `/documentos/nota-debito`, `/documentos/nota-ajuste`
 * y `/documentos/:id`, para el propietario y el vendedor con una empresa activa; emitir pide además el alcance
 * `documents:write`. Las notas son rutas propias (y no `nuevo?tipo=`) porque su formulario es otro (documento de
 * referencia y concepto, sin pago) y así cada una tiene su enlace; las tres usan la misma página con el tipo en
 * `data`. Este archivo entra en el bundle inicial y es mínimo: las páginas se cargan con
 * `import('./feature-documents')`, un solo chunk perezoso llamado `feature-documents` con su presupuesto en
 * `angular.json`.
 */
const loadFeature = () => import('./feature-documents');

/** Alcance que da el Api a quien puede emitir (propietario y vendedor). */
export const ISSUE_SCOPE = 'documents:write';

/** Ruta de cada nota; la de ajuste corrige un documento soporte (`?soporte=<id>`). */
const NOTE_PATHS = {
  credit: 'nota-credito',
  debit: 'nota-debito',
  adjustment: 'nota-ajuste',
} as const;

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
        path: 'documento-soporte',
        canActivate: [scopeGuard(ISSUE_SCOPE)],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => loadFeature().then((m) => m.SupportDocumentIssuePage),
      },
      ...(['credit', 'debit', 'adjustment'] as const).map((kind) => ({
        path: NOTE_PATHS[kind],
        data: { kind },
        canActivate: [scopeGuard(ISSUE_SCOPE)],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => loadFeature().then((m) => m.NoteIssuePage),
      })),
      {
        path: ':id',
        loadComponent: () => loadFeature().then((m) => m.DocumentDetailPage),
      },
    ],
  },
];
