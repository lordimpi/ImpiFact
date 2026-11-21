import { Routes } from '@angular/router';
import {
  authGuard,
  companyGuard,
  roleGuard,
  scopeGuard,
  unsavedChangesGuard,
} from '@impifact/core';
import { companiesTextsResolver } from './companies-i18n';
import { activeCompanyGuard, redirectToActiveCompany } from './guards/active-company.guard';

/*
 * Rutas del feature (sección 7 de la arquitectura del frontend): la configuración de la empresa
 * (`/empresas/:id/{datos,ambientes,software,certificado,resoluciones}`) y el asistente de paso a producción
 * (`/empresas/:id/produccion`), solo para el propietario. Este archivo entra en el bundle inicial y es mínimo: las
 * pantallas de la empresa se cargan con `import('./feature-companies')` y el asistente con
 * `import('./feature-production-wizard')`, dos chunks perezosos con su propio presupuesto en `angular.json`.
 *
 * Correo (SMTP), logo y plantilla de la sección 7 no tienen pantalla: el Api aún no tiene esos endpoints.
 *
 * The company users page (`/empresa/usuarios`) is not a configuration tab: it is its own sidebar entry under
 * Ajustes, works on the active company of the session and lives in a third lazy chunk, `feature-company-users`.
 * The same page serves the platform admin at `/plataforma/empresas/:nit/usuarios` for any company (the `nit` input):
 * the route lives here, next to its lazy entry, because the app cannot load a feature's internal chunk, and it keeps
 * the guards of `/plataforma/*` (role `platform_admin` and scope `platform:*`, which the API gives only without an
 * active company). The app spreads these routes before `platformRoutes`, so this path wins over `/plataforma`.
 */
const loadCompanies = () => import('./feature-companies');
const loadWizard = () => import('./feature-production-wizard');
const loadUsers = () => import('./feature-company-users');

/** Roles que configuran la empresa. */
export const COMPANY_ROLES: readonly string[] = ['owner'];

/** Role and token scope of the platform admin (the same ones `/plataforma/*` asks for in feature-platform). */
const PLATFORM_ADMIN_ROLES: readonly string[] = ['platform_admin'];
const PLATFORM_ADMIN_SCOPE = 'platform:*';

export const companiesRoutes: Routes = [
  {
    path: 'empresa',
    pathMatch: 'full',
    canActivate: [roleGuard(COMPANY_ROLES), redirectToActiveCompany('datos')],
    children: [],
  },
  {
    path: 'empresa/produccion',
    canActivate: [roleGuard(COMPANY_ROLES), redirectToActiveCompany('produccion')],
    children: [],
  },
  {
    path: 'empresa/usuarios',
    canActivate: [authGuard, companyGuard, roleGuard(COMPANY_ROLES)],
    resolve: { texts: companiesTextsResolver },
    loadComponent: () => loadUsers().then((m) => m.CompanyUsersPage),
  },
  {
    path: 'plataforma/empresas/:nit/usuarios',
    canActivate: [authGuard, roleGuard(PLATFORM_ADMIN_ROLES), scopeGuard(PLATFORM_ADMIN_SCOPE)],
    resolve: { texts: companiesTextsResolver },
    loadComponent: () => loadUsers().then((m) => m.CompanyUsersPage),
  },
  {
    path: 'empresas/:id',
    canActivate: [authGuard, roleGuard(COMPANY_ROLES), activeCompanyGuard],
    resolve: { texts: companiesTextsResolver },
    loadComponent: () => loadCompanies().then((m) => m.CompanyLayoutPage),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'datos' },
      {
        path: 'datos',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => loadCompanies().then((m) => m.CompanyDataPage),
      },
      {
        path: 'ambientes',
        loadComponent: () => loadCompanies().then((m) => m.EnvironmentsPage),
      },
      {
        path: 'software',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => loadCompanies().then((m) => m.SoftwarePage),
      },
      {
        path: 'certificado',
        loadComponent: () => loadCompanies().then((m) => m.CertificatePage),
      },
      {
        path: 'resoluciones',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => loadCompanies().then((m) => m.ResolutionsPage),
      },
      {
        path: 'produccion',
        loadComponent: () => loadWizard().then((m) => m.ProductionWizardPage),
      },
    ],
  },
];
