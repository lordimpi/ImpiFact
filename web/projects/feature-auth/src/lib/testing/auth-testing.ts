import { signal } from '@angular/core';
import { MyCompany } from '@impifact/api';
import { MyCompaniesStore, SessionStore } from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import authEsCO from '../../i18n/es-CO.json';

/*
 * Ayudas de prueba del feature (solo las usan los archivos .spec.ts; no se exportan en la API pública).
 */

// Cargador en memoria con el archivo real de textos del feature; el idioma raíz queda vacío.
class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    return of((path === 'auth/es-CO' ? authEsCO : {}) as Translation);
  }
}

export function provideAuthTestingI18n() {
  return provideTransloco({
    config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
    loader: InMemoryLoader,
  });
}

/** Sesión falsa con los signals y métodos que usa el feature. */
export function fakeSession(
  initial: {
    authenticated?: boolean;
    companyId?: string | null;
    companies?: string[];
    /** Empresas con nombre de `GET /api/v1/me/companies`; sin ellas, la lista no cargó. */
    directory?: MyCompany[];
  } = {},
) {
  const isAuthenticated = signal(initial.authenticated ?? false);
  const companyId = signal<string | null>(initial.companyId ?? null);
  const companies = signal<string[]>(initial.companies ?? []);
  const session = {
    isAuthenticated,
    companyId,
    companies,
    user: signal<{ id: string; email: string | null } | null>(null),
    login: vi.fn(async () => {
      isAuthenticated.set(true);
    }),
    selectCompany: vi.fn(async (id: string) => {
      companyId.set(id);
    }),
    logout: vi.fn(async () => {
      isAuthenticated.set(false);
    }),
    restore: vi.fn(async () => undefined),
  };
  const items = signal<MyCompany[]>(initial.directory ?? []);
  const directory = {
    items,
    status: signal(initial.directory ? 'ready' : 'error'),
    byId: (id: string | null | undefined) => items().find((company) => company.id === id) ?? null,
    reload: vi.fn(async () => undefined),
    whenLoaded: vi.fn(async () => undefined),
  };
  return {
    session,
    directory,
    provider: [
      { provide: SessionStore, useValue: session },
      { provide: MyCompaniesStore, useValue: directory },
    ],
  };
}
