import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withComponentInputBinding } from '@angular/router';
import { NotificationResponse } from '@impifact/api';
import { SessionStore, errorInterceptor } from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { render, waitFor } from '@testing-library/angular';
import { of } from 'rxjs';
import notificationsEsCO from '../../i18n/es-CO.json';
import { notificationsRoutes } from '../notifications.routes';

/*
 * Ayudas de prueba del feature (solo las usan los archivos .spec.ts; no se exportan en la API pública).
 */

// Cargador en memoria con el archivo real de textos del feature; el idioma raíz queda vacío.
class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    return of((path === 'notifications/es-CO' ? notificationsEsCO : {}) as Translation);
  }
}

@Component({
  selector: 'feat-stub-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p>stub</p>',
})
class StubPage {}

export const translationProviders = () =>
  provideTransloco({
    config: {
      availableLangs: ['es-CO'],
      defaultLang: 'es-CO',
      missingHandler: { logMissingKey: false },
    },
    loader: InMemoryLoader,
  });

/**
 * Sesión falsa con empresa activa. Sin `documents:read`, así el store de la campana de core no consulta por su
 * cuenta y las pruebas solo ven las solicitudes de la página.
 */
export function fakeSession() {
  const roles = signal(['owner']);
  const scopes = signal<string[]>([]);
  return {
    isAuthenticated: signal(true),
    companyId: signal<string | null>('company-1'),
    companies: signal(['company-1']),
    roles,
    scopes,
    hasRole: (...wanted: string[]) => wanted.some((role) => roles().includes(role)),
    hasScope: (scope: string) => scopes().includes(scope),
    restore: vi.fn(async () => undefined),
  };
}

/** Monta las rutas reales del feature con el Api simulado (`HttpTestingController`) y navega a `url`. */
export async function renderNotificationsRoute(url: string) {
  const session = fakeSession();
  await render('<router-outlet />', {
    imports: [RouterOutlet],
    providers: [
      provideRouter(
        [
          ...notificationsRoutes,
          { path: '', pathMatch: 'full', component: StubPage },
          { path: 'empresas/seleccionar', component: StubPage },
        ],
        withComponentInputBinding(),
      ),
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      translationProviders(),
      { provide: SessionStore, useValue: session },
    ],
  });
  const router = TestBed.inject(Router);
  const http = TestBed.inject(HttpTestingController);
  await router.navigateByUrl(url);
  return { router, http, session };
}

export async function expectRequest(
  http: HttpTestingController,
  match: Parameters<HttpTestingController['expectOne']>[0],
) {
  return waitFor(() => http.expectOne(match));
}

export function params(request: TestRequest) {
  const query = request.request.params;
  return Object.fromEntries(query.keys().map((key) => [key, query.get(key)]));
}

export function notice(id: string, overrides: Partial<NotificationResponse> = {}) {
  return {
    id,
    type: 'certificate.expiring',
    title: `Aviso ${id}`,
    body: `Detalle del aviso ${id}`,
    is_read: false,
    created_at: '2026-11-17T15:30:00Z',
    ...overrides,
  } satisfies NotificationResponse;
}
