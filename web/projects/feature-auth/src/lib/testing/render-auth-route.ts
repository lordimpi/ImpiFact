import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withComponentInputBinding } from '@angular/router';
import { errorInterceptor } from '@impifact/core';
import { render, waitFor } from '@testing-library/angular';
import { authRoutes } from '../auth.routes';
import { fakeSession, provideAuthTestingI18n } from './auth-testing';

/** Página vacía para las rutas a las que el feature manda a la persona (inicio, una ruta protegida). */
@Component({
  selector: 'feat-stub-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p>stub</p>',
})
class StubPage {}

/**
 * Monta las rutas reales del feature (guards, *resolver* de textos y carga perezosa) junto al inicio y a una ruta
 * protegida, con una sesión falsa y el Api simulado (`HttpTestingController`, con el interceptor de errores de core),
 * y navega a `url`.
 */
export async function renderAuthRoute(
  url: string,
  session: Parameters<typeof fakeSession>[0] = {},
) {
  const fake = fakeSession(session);
  await render('<router-outlet />', {
    imports: [RouterOutlet],
    providers: [
      provideRouter(
        [
          ...authRoutes,
          { path: '', pathMatch: 'full', component: StubPage },
          { path: 'documentos', component: StubPage },
        ],
        withComponentInputBinding(),
      ),
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideAuthTestingI18n(),
      fake.provider,
    ],
  });
  const router = TestBed.inject(Router);
  const http = TestBed.inject(HttpTestingController);
  await router.navigateByUrl(url);
  return { session: fake.session, router, http };
}

/** Espera la única solicitud que cumple `match` (las páginas llaman al Api después de un `await`). */
export function expectRequest(
  http: HttpTestingController,
  match: Parameters<HttpTestingController['expectOne']>[0],
) {
  return waitFor(() => http.expectOne(match));
}

/** Respuesta de error del Api (ProblemDetails) para `flush`, con errores por campo opcionales. */
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
