import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  GuardResult,
  MaybeAsync,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import {
  Translation,
  TranslocoLoader,
  TranslocoService,
  provideTransloco,
} from '@jsverse/transloco';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { FakeSessionSync, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import {
  HasUnsavedChanges,
  UNSAVED_CHANGES_CONFIRM,
  authGuard,
  companyGuard,
  roleGuard,
  unsavedChangesGuard,
} from './guards';

const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve));

class InMemoryLoader implements TranslocoLoader {
  getTranslation() {
    return of({
      core: { unsavedChanges: 'Hay cambios sin guardar. ¿Salir de todos modos?' },
    } as Translation);
  }
}

describe('core guards', () => {
  let http: HttpTestingController;
  let sync: FakeSessionSync;
  const confirm = vi.fn<(message: string) => boolean | Promise<boolean>>();

  beforeEach(() => {
    confirm.mockReset();
    const fake = provideFakeSessionSync();
    sync = fake.sync;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTransloco({
          config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
          loader: InMemoryLoader,
        }),
        fake.provider,
        { provide: UNSAVED_CHANGES_CONFIRM, useValue: confirm },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const route = {} as ActivatedRouteSnapshot;
  const state = (url: string) => ({ url }) as RouterStateSnapshot;

  async function resolve(result: MaybeAsync<GuardResult>): Promise<GuardResult> {
    return isObservable(result) ? firstValueFrom(result) : result;
  }

  function run(guard: () => MaybeAsync<GuardResult>): Promise<GuardResult> {
    return resolve(TestBed.runInInjectionContext(guard));
  }

  const serialize = (result: GuardResult) =>
    result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;

  function signIn(overrides: Parameters<typeof panelTokens>[0] = {}) {
    // El store se suscribe a los mensajes al crearse.
    TestBed.inject(SessionStore);
    sync.receive({ type: 'session', tokens: panelTokens(overrides), email: null });
  }

  describe('authGuard', () => {
    it('waits for the session restore and lets an authenticated user through', async () => {
      const result = run(() => authGuard(route, state('/documentos')));
      await flushMicrotasks();
      http.expectOne('/api/v1/auth/refresh').flush(panelTokens());

      expect(await result).toBe(true);
    });

    it('sends a visitor without session to login with the return url', async () => {
      const result = run(() => authGuard(route, state('/documentos?estado=Accepted')));
      await flushMicrotasks();
      http
        .expectOne('/api/v1/auth/refresh')
        .flush(null, { status: 401, statusText: 'Unauthorized' });

      expect(serialize(await result)).toBe('/login?returnUrl=%2Fdocumentos%3Festado%3DAccepted');
    });
  });

  describe('roleGuard', () => {
    it('lets through a user with any of the roles', async () => {
      signIn({ roles: ['seller'] });

      expect(await run(() => roleGuard(['owner', 'seller'])(route, state('/clientes')))).toBe(true);
    });

    it('sends a user without the role to the home page', async () => {
      signIn({ roles: ['customer'] });

      expect(serialize(await run(() => roleGuard(['owner'])(route, state('/plan'))))).toBe('/');
    });

    it('sends a visitor without session to login', async () => {
      const result = run(() => roleGuard(['owner'])(route, state('/plan')));
      await flushMicrotasks();
      http
        .expectOne('/api/v1/auth/refresh')
        .flush(null, { status: 401, statusText: 'Unauthorized' });

      expect(serialize(await result)).toBe('/login?returnUrl=%2Fplan');
    });
  });

  describe('companyGuard', () => {
    it('lets through a session with an active company', async () => {
      signIn();

      expect(await run(() => companyGuard(route, state('/documentos')))).toBe(true);
    });

    it('sends a session without active company to the company selection', async () => {
      signIn({ company_id: null });

      expect(serialize(await run(() => companyGuard(route, state('/documentos'))))).toBe(
        '/empresas/seleccionar',
      );
    });
  });

  describe('unsavedChangesGuard', () => {
    const page = (dirty: boolean): HasUnsavedChanges => ({ hasUnsavedChanges: () => dirty });
    const leave = (component: HasUnsavedChanges) =>
      run(() => unsavedChangesGuard(component, route, state('/a'), state('/b')));

    it('leaves without asking when nothing changed', async () => {
      expect(await leave(page(false))).toBe(true);
      expect(confirm).not.toHaveBeenCalled();
    });

    it('asks before leaving with unsaved changes and follows the answer', async () => {
      await firstValueFrom(TestBed.inject(TranslocoService).load('es-CO'));
      confirm.mockReturnValueOnce(false).mockResolvedValueOnce(true);

      expect(await leave(page(true))).toBe(false);
      expect(await leave(page(true))).toBe(true);
      expect(confirm).toHaveBeenCalledTimes(2);
      expect(confirm).toHaveBeenCalledWith('Hay cambios sin guardar. ¿Salir de todos modos?');
    });
  });
});
