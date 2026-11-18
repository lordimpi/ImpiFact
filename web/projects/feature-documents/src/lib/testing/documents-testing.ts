import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withComponentInputBinding } from '@angular/router';
import { SessionStore, ToastStore } from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { render } from '@testing-library/angular';
import { of } from 'rxjs';
import documentsEsCO from '../../i18n/es-CO.json';
import { documentsRoutes } from '../documents.routes';

/*
 * Ayudas de prueba del feature (solo las usan los archivos .spec.ts; no se exportan en la API pública).
 */

// Cargador en memoria con el archivo real de textos del feature; el idioma raíz trae lo que usa ui.
class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    const root = {
      ui: {
        retry: 'Reintentar',
        paginator: {
          navigation: 'Paginación',
          previous: 'Anterior',
          next: 'Siguiente',
          page: 'Página {page}',
        },
      },
    };
    return of((path === 'documents/es-CO' ? documentsEsCO : root) as Translation);
  }
}

export function provideDocumentsTestingI18n() {
  return provideTransloco({
    config: {
      availableLangs: ['es-CO'],
      defaultLang: 'es-CO',
      missingHandler: { logMissingKey: false },
    },
    loader: InMemoryLoader,
  });
}

/** Sesión falsa con lo que usa el feature: roles, alcances y empresa activa. */
export function fakeSession(
  initial: { roles?: string[]; scopes?: string[]; companyId?: string | null } = {},
) {
  const roles = signal(initial.roles ?? ['owner']);
  const scopes = signal(initial.scopes ?? ['documents:read', 'documents:write']);
  const session = {
    isAuthenticated: signal(true),
    companyId: signal(initial.companyId === undefined ? 'company-1' : initial.companyId),
    companies: signal(['company-1']),
    roles,
    scopes,
    hasRole: (...wanted: string[]) => wanted.some((role) => roles().includes(role)),
    hasScope: (scope: string) => scopes().includes(scope),
    restore: vi.fn(async () => undefined),
  };
  return { session, provider: { provide: SessionStore, useValue: session } };
}

@Component({
  selector: 'feat-stub-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p>stub</p>',
})
class StubPage {}

/**
 * Monta las rutas reales del feature (guards, *resolver* de textos y carga perezosa) con el cliente HTTP de prueba
 * y una sesión falsa, y navega a `url`.
 */
export async function renderDocumentsRoute(
  url: string,
  session: Parameters<typeof fakeSession>[0] = {},
) {
  const fake = fakeSession(session);
  const view = await render('<router-outlet />', {
    imports: [RouterOutlet],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter(
        [
          ...documentsRoutes,
          { path: '', pathMatch: 'full', component: StubPage },
          { path: 'clientes/nuevo', component: StubPage },
          { path: 'empresas/seleccionar', component: StubPage },
        ],
        withComponentInputBinding(),
      ),
      provideDocumentsTestingI18n(),
      fake.provider,
    ],
  });
  const router = TestBed.inject(Router);
  await router.navigateByUrl(url);
  return {
    view,
    router,
    session: fake.session,
    http: TestBed.inject(HttpTestingController),
    toasts: TestBed.inject(ToastStore),
  };
}
