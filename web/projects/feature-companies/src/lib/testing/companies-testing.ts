import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withComponentInputBinding } from '@angular/router';
import { MyCompany } from '@impifact/api';
import {
  MyCompaniesStore,
  SessionStore,
  UNSAVED_CHANGES_CONFIRM,
  errorInterceptor,
} from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { render, waitFor } from '@testing-library/angular';
import { of } from 'rxjs';
import companiesEsCO from '../../i18n/es-CO.json';
import { companiesRoutes } from '../companies.routes';
import { catalogFixtures, catalogRequests } from './catalog-fixtures';

/*
 * Ayudas de prueba del feature (solo las usan los archivos .spec.ts; no se exportan en la API pública).
 */

export const COMPANY_ID = 'company-1';
export const ACME: MyCompany = {
  id: COMPANY_ID,
  name: 'Acme SAS',
  nit: '900123456',
  dv: '7',
  roles: ['owner'],
  is_active: true,
};

// Cargador en memoria con el archivo real de textos del feature; el idioma raíz trae lo que usa ui.
class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    const root = { ui: { close: 'Cerrar', retry: 'Reintentar' } };
    return of((path === 'companies/es-CO' ? companiesEsCO : root) as Translation);
  }
}

@Component({
  selector: 'feat-stub-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p>stub</p>',
})
class StubPage {}

export interface RenderOptions {
  roles?: string[];
  companyId?: string | null;
  /** Empresas de `GET /api/v1/me/companies`; `null` simula que la lista no cargó. */
  directory?: MyCompany[] | null;
  confirmLeave?: boolean;
  /** Id of the signed-in user (`sub` of the token); `user-1` by default. */
  userId?: string | null;
  /** Scopes of the token (`scope`); none by default. */
  scopes?: string[];
}

/**
 * Monta las rutas reales del feature (guards, *resolver* de textos y carga perezosa) con una sesión y un directorio
 * de empresas falsos, el interceptor de errores de core y el Api simulado con `HttpTestingController`.
 */
export async function renderCompaniesRoute(url: string, options: RenderOptions = {}) {
  const roles = signal(options.roles ?? ['owner']);
  const session = {
    isAuthenticated: signal(true),
    userId: signal<string | null>(options.userId ?? 'user-1'),
    companyId: signal(options.companyId === undefined ? COMPANY_ID : options.companyId),
    companies: signal([COMPANY_ID, 'company-2']),
    roles,
    hasRole: (...wanted: string[]) => wanted.some((role) => roles().includes(role)),
    hasScope: (scope: string) => (options.scopes ?? []).includes(scope),
    restore: vi.fn(async () => undefined),
  };
  const items = signal<MyCompany[]>(
    options.directory === null ? [] : (options.directory ?? [ACME]),
  );
  const directory = {
    items,
    status: signal(options.directory === null ? 'error' : 'ready'),
    byId: (id: string | null | undefined) => items().find((company) => company.id === id) ?? null,
    reload: vi.fn(async () => undefined),
    whenLoaded: vi.fn(async () => undefined),
  };
  const confirmLeave = vi.fn(() => options.confirmLeave ?? true);
  catalogRequests.length = 0;
  await render('<router-outlet />', {
    imports: [RouterOutlet],
    providers: [
      provideRouter(
        [
          // Como en la aplicación, la selección de empresa va antes que `empresas/:id`.
          { path: 'empresas/seleccionar', component: StubPage },
          ...companiesRoutes,
          // The platform list of companies (feature-platform) that the platform users page links back to.
          { path: 'plataforma/empresas', component: StubPage },
          { path: '', pathMatch: 'full', component: StubPage },
        ],
        withComponentInputBinding(),
      ),
      // Los catálogos los contesta `catalogFixtures`; el resto del Api, `HttpTestingController`.
      provideHttpClient(withInterceptors([catalogFixtures, errorInterceptor])),
      provideHttpClientTesting(),
      provideTransloco({
        config: {
          availableLangs: ['es-CO'],
          defaultLang: 'es-CO',
          missingHandler: { logMissingKey: false },
        },
        loader: InMemoryLoader,
      }),
      { provide: SessionStore, useValue: session },
      { provide: MyCompaniesStore, useValue: directory },
      { provide: UNSAVED_CHANGES_CONFIRM, useValue: confirmLeave },
    ],
  });
  const router = TestBed.inject(Router);
  const http = TestBed.inject(HttpTestingController);
  await router.navigateByUrl(url);
  return { router, http, session, directory, confirmLeave };
}

/** Espera la solicitud que cumple `match` (la página la manda tras un ciclo de detección de cambios). */
export async function expectRequest(
  http: HttpTestingController,
  match: Parameters<HttpTestingController['expectOne']>[0],
) {
  return waitFor(() => http.expectOne(match));
}

/** Ruta base de la empresa de prueba en el Api. */
export const COMPANY_PATH = '/api/v1/companies/900123456';

/** Espera la lectura (`GET`) de `path` y la contesta con `body`. */
export async function respond(
  http: HttpTestingController,
  path: string,
  body: Parameters<TestRequest['flush']>[0],
) {
  const request = await expectRequest(
    http,
    (candidate) => candidate.method === 'GET' && candidate.url === path,
  );
  request.flush(body);
  return request;
}

/** Datos de la empresa como los devuelve `getCompany`. */
export const COMPANY_DATA = {
  id: COMPANY_ID,
  identification_number: '900123456',
  dv: '7',
  business_name: 'Acme SAS',
  type_document_identification_id: 6,
  type_organization_id: 1,
  type_regime_id: 1,
  type_liability_ids: [7],
  municipality_id: 149,
  address: 'Calle 1 # 2-3',
  phone: '6011234567',
  email: 'facturas@acme.test',
  merchant_registration: '12345-67',
  is_active: true,
  environments: [{ document_kind: 1, type_environment_id: 2 }],
};

/** Ambientes de la empresa de prueba: todo en habilitación. */
export const ENVIRONMENTS = [1, 2, 3, 4, 5].map((kind) => ({
  document_kind: kind,
  type_environment_id: 2,
}));

/** Software de la factura en habilitación, con PIN guardado. */
export const INVOICE_SOFTWARE = {
  document_kind: 1,
  type_environment_id: 2,
  identifier: 'soft-123',
  url: 'https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc',
  test_set_id: 'set-abc',
  has_pin: true,
};

/** Estado del paso a producción de una empresa recién creada. */
export const EMPTY_READINESS = {
  company_complete: false,
  certificate_valid: false,
  software_configured: false,
  test_set_id_configured: false,
  test_set_accepted: null,
  environments: ENVIRONMENTS,
  resolutions: [
    { kind: 'invoice', type_document_id: 1, valid: false },
    { kind: 'credit_note', type_document_id: 4, valid: false },
    { kind: 'debit_note', type_document_id: 5, valid: false },
  ],
};

/** Cuerpo de error del Api (ProblemDetails) para `flush`. */
export function problem(
  status: number,
  code: string,
  message: string,
  errors?: Record<string, string[]>,
) {
  return {
    body: {
      status,
      code,
      message,
      correlation_id: 'test-correlation',
      ...(errors ? { errors } : {}),
    },
    options: { status, statusText: 'Error' },
  };
}

/** Certificado activo de ejemplo como lo devuelve el Api. */
export const CERTIFICATE = {
  id: 'cert-1',
  name: 'acme.p12',
  subject: 'CN=ACME SAS, SERIALNUMBER=900123456',
  issuer: 'CN=GSE CA',
  thumbprint: 'AB12',
  not_before: '2026-01-01T00:00:00Z',
  expiration_date: '2027-06-30',
  certificate_days_left: 225,
  is_active: true,
};
