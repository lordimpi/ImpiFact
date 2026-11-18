import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
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
}

/**
 * Monta las rutas reales del feature (guards, *resolver* de textos y carga perezosa) con una sesión y un directorio
 * de empresas falsos, el interceptor de errores de core y el Api simulado con `HttpTestingController`.
 */
export async function renderCompaniesRoute(url: string, options: RenderOptions = {}) {
  const roles = signal(options.roles ?? ['owner']);
  const session = {
    isAuthenticated: signal(true),
    companyId: signal(options.companyId === undefined ? COMPANY_ID : options.companyId),
    companies: signal([COMPANY_ID, 'company-2']),
    roles,
    hasRole: (...wanted: string[]) => wanted.some((role) => roles().includes(role)),
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
