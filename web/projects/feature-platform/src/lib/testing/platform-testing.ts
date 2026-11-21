import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withComponentInputBinding } from '@angular/router';
import { SessionStore, errorInterceptor } from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { render, waitFor } from '@testing-library/angular';
import { of } from 'rxjs';
import platformEsCO from '../../i18n/es-CO.json';
import { platformRoutes } from '../platform.routes';
import { catalogFixtures, catalogRequests } from './catalog-fixtures';

/*
 * Ayudas de prueba del feature (solo las usan los archivos .spec.ts; no se exportan en la API pública).
 */

class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    return of((path === 'platform/es-CO' ? platformEsCO : {}) as Translation);
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
  scopes?: string[];
}

/** Monta las rutas reales del feature con una sesión falsa y el Api simulado, y navega a `url`. */
export async function renderPlatformRoute(url: string, options: RenderOptions = {}) {
  const roles = signal(options.roles ?? ['platform_admin']);
  const scopes = signal(options.scopes ?? ['platform:*']);
  const session = {
    isAuthenticated: signal(true),
    companyId: signal<string | null>(null),
    companies: signal<string[]>([]),
    roles,
    scopes,
    hasRole: (...wanted: string[]) => wanted.some((role) => roles().includes(role)),
    hasScope: (scope: string) => scopes().includes(scope),
    restore: vi.fn(async () => undefined),
  };
  catalogRequests.length = 0;
  await render('<router-outlet />', {
    imports: [RouterOutlet],
    providers: [
      provideRouter(
        [
          // The users of a company belong to feature-companies; as in the app, its route goes first.
          { path: 'plataforma/empresas/:nit/usuarios', component: StubPage },
          ...platformRoutes,
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
