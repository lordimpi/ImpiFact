import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withComponentInputBinding } from '@angular/router';
import { render } from '@testing-library/angular';
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
 * protegida, con una sesión falsa, y navega a `url`.
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
      provideAuthTestingI18n(),
      fake.provider,
    ],
  });
  const router = TestBed.inject(Router);
  await router.navigateByUrl(url);
  return { session: fake.session, router };
}
