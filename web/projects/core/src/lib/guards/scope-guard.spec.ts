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
import { provideTransloco } from '@jsverse/transloco';
import { firstValueFrom, isObservable } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { FakeSessionSync, fakeJwt, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { scopeGuard } from './guards';

const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve));

describe('scopeGuard', () => {
  let http: HttpTestingController;
  let sync: FakeSessionSync;

  beforeEach(() => {
    const fake = provideFakeSessionSync();
    sync = fake.sync;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTransloco({ config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' } }),
        fake.provider,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const route = {} as ActivatedRouteSnapshot;
  const state = (url: string) => ({ url }) as RouterStateSnapshot;

  async function run(url: string, scope: string): Promise<GuardResult> {
    const result: MaybeAsync<GuardResult> = TestBed.runInInjectionContext(() =>
      scopeGuard(scope)(route, state(url)),
    );
    return isObservable(result) ? firstValueFrom(result) : result;
  }

  const serialize = (result: GuardResult) =>
    result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;

  function signIn(scopes: string[]) {
    TestBed.inject(SessionStore);
    const token = fakeJwt({ sub: 'user-1', role: ['seller'], scope: scopes, company_id: 'c-1' });
    sync.receive({ type: 'session', tokens: panelTokens({ access_token: token }), email: null });
  }

  it('lets through a session with the scope', async () => {
    signIn(['documents:read', 'documents:write']);

    expect(await run('/documentos/nuevo', 'documents:write')).toBe(true);
  });

  it('sends a session without the scope to the home page', async () => {
    signIn(['documents:read']);

    expect(serialize(await run('/documentos/nuevo', 'documents:write'))).toBe('/');
  });

  it('sends a visitor without session to login with the return url', async () => {
    const result = run('/documentos/nuevo', 'documents:write');
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(serialize(await result)).toBe('/login?returnUrl=%2Fdocumentos%2Fnuevo');
  });
});
