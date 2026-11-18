import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withComponentInputBinding } from '@angular/router';
import { SessionStore, UNSAVED_CHANGES_CONFIRM, errorInterceptor } from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { render, waitFor } from '@testing-library/angular';
import { of } from 'rxjs';
import mastersEsCO from '../../i18n/es-CO.json';
import { mastersRoutes } from '../masters.routes';
import { catalogFixtures, catalogRequests } from './catalog-fixtures';

/*
 * Ayudas de prueba del feature (solo las usan los archivos .spec.ts; no se exportan en la API pública).
 */

// Cargador en memoria con el archivo real de textos del feature; el idioma raíz queda vacío.
class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    return of((path === 'masters/es-CO' ? mastersEsCO : {}) as Translation);
  }
}

/** Página vacía para las rutas a las que el feature manda a la persona (inicio, selección de empresa). */
@Component({
  selector: 'feat-stub-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p>stub</p>',
})
class StubPage {}

export interface RenderOptions {
  roles?: string[];
  companyId?: string | null;
  /** Respuesta de la confirmación al salir con cambios sin guardar. */
  confirmLeave?: boolean;
}

/**
 * Monta las rutas reales del feature (guards, *resolver* de textos y carga perezosa) con una sesión falsa, el
 * interceptor de errores de core (convierte las respuestas en `AppError`) y el Api simulado con
 * `HttpTestingController`, y navega a `url`.
 */
export async function renderMastersRoute(url: string, options: RenderOptions = {}) {
  const roles = signal(options.roles ?? ['owner']);
  const session = {
    isAuthenticated: signal(true),
    companyId: signal(options.companyId === undefined ? 'company-1' : options.companyId),
    companies: signal(['company-1']),
    roles,
    hasRole: (...wanted: string[]) => wanted.some((role) => roles().includes(role)),
    restore: vi.fn(async () => undefined),
  };
  const confirmLeave = vi.fn(() => options.confirmLeave ?? true);
  catalogRequests.length = 0;
  await render('<router-outlet />', {
    imports: [RouterOutlet],
    providers: [
      provideRouter(
        [
          ...mastersRoutes,
          { path: '', pathMatch: 'full', component: StubPage },
          { path: 'empresas/seleccionar', component: StubPage },
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
      { provide: UNSAVED_CHANGES_CONFIRM, useValue: confirmLeave },
    ],
  });
  const router = TestBed.inject(Router);
  const http = TestBed.inject(HttpTestingController);
  await router.navigateByUrl(url);
  return { router, http, session, confirmLeave };
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
