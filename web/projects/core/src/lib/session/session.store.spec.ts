import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { FakeSessionSync, fakeJwt, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { SessionStore } from './session.store';

describe('SessionStore', () => {
  let store: InstanceType<typeof SessionStore>;
  let http: HttpTestingController;
  let sync: FakeSessionSync;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    const fake = provideFakeSessionSync();
    sync = fake.sync;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        fake.provider,
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    store = TestBed.inject(SessionStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve));

  it('logs in with the panel header and keeps the session in memory only', async () => {
    const tokens = panelTokens();
    const done = store.login({ email: 'ana@example.test', password: 'secreta' });

    const request = http.expectOne('/api/v1/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-Client')).toBe('panel');
    expect(request.request.body).toEqual({
      email: 'ana@example.test',
      password: 'secreta',
      company_id: null,
    });
    request.flush(tokens);
    await done;

    expect(store.status()).toBe('authenticated');
    expect(store.isAuthenticated()).toBe(true);
    expect(store.accessToken()).toBe(tokens.access_token);
    expect(store.user()).toEqual({ id: 'user-1', email: 'ana@example.test' });
    expect(store.roles()).toEqual(['owner']);
    expect(store.scopes()).toEqual(['documents:read', 'documents:write']);
    expect(store.companyId()).toBe('company-1');
    expect(store.companies()).toEqual(['company-1', 'company-2']);
    expect(store.hasRole('seller', 'owner')).toBe(true);
    expect(store.hasRole('platform_admin')).toBe(false);
    expect(store.hasScope('documents:write')).toBe(true);
    expect(sync.posted).toEqual([{ type: 'session', tokens, email: 'ana@example.test' }]);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('restores the session once at startup with a body-less refresh', async () => {
    const first = store.restore();
    const second = store.restore();
    await flushMicrotasks();

    const request = http.expectOne('/api/v1/auth/refresh');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-Client')).toBe('panel');
    expect(request.request.body).toBeNull();
    request.flush(panelTokens({ email: 'ana@example.test' }));
    await Promise.all([first, second]);

    expect(store.status()).toBe('authenticated');
    // A reloaded page only learns who is signed in from the refresh: the JWT carries no email.
    expect(store.user()).toEqual({ id: 'user-1', email: 'ana@example.test' });
    await store.restore();
    http.expectNone('/api/v1/auth/refresh');
  });

  it('ends up without a session when the refresh answers 401', async () => {
    const restored = store.restore();
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    await restored;

    expect(store.status()).toBe('anonymous');
    expect(store.accessToken()).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('keeps a single refresh in flight for concurrent callers', async () => {
    const calls = [store.refresh(null), store.refresh(null), store.refresh(null)];
    await flushMicrotasks();

    http.expectOne('/api/v1/auth/refresh').flush(panelTokens());

    expect(await Promise.all(calls)).toEqual([true, true, true]);
  });

  it('skips its own refresh when another tab renewed the session while it waited for the lock', async () => {
    const stale = fakeJwt({ sub: 'user-1', exp: 1 });
    sync.receive({ type: 'session', tokens: panelTokens({ access_token: stale }), email: null });
    const release = sync.holdLock();

    const refreshed = store.refresh(stale);
    await flushMicrotasks();
    http.expectNone('/api/v1/auth/refresh');

    const renewed = panelTokens({ access_token: fakeJwt({ sub: 'user-1', exp: 2 }) });
    sync.receive({ type: 'session', tokens: renewed, email: null });
    release();

    expect(await refreshed).toBe(true);
    http.expectNone('/api/v1/auth/refresh');
    expect(store.accessToken()).toBe(renewed.access_token);
  });

  it('retries the refresh once when the API reports a concurrent refresh (409)', async () => {
    const refreshed = store.refresh(null);
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 409, statusText: 'Conflict' });
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/refresh').flush(panelTokens());

    expect(await refreshed).toBe(true);
    expect(store.status()).toBe('authenticated');
  });

  it('shares the renewed session with the other tabs', async () => {
    const tokens = panelTokens();
    const refreshed = store.refresh(null);
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/refresh').flush(tokens);
    await refreshed;

    expect(sync.posted).toEqual([{ type: 'session', tokens, email: tokens.email }]);
  });

  it('logs out against the API, clears the session, tells the other tabs and goes to login', async () => {
    sync.receive({ type: 'session', tokens: panelTokens(), email: null });
    expect(store.isAuthenticated()).toBe(true);

    const done = store.logout();
    const request = http.expectOne('/api/v1/auth/logout');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-Client')).toBe('panel');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;

    expect(store.status()).toBe('anonymous');
    expect(store.accessToken()).toBeNull();
    expect(store.roles()).toEqual([]);
    expect(sync.posted).toEqual([{ type: 'logout' }]);
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  // Sonda P1 del verificador. Orden adaptado a la corrección: el cierre limpia la sesión al instante y espera la
  // renovación en vuelo antes de llamar a /auth/logout, para revocar la cookie más nueva.
  it('logout while a refresh is in flight leaves the session closed and revokes the newest cookie', async () => {
    sync.receive({
      type: 'session',
      tokens: panelTokens({ access_token: fakeJwt({ sub: 'user-1', n: 1 }) }),
      email: null,
    });
    const refreshing = store.refresh(store.accessToken());
    await flushMicrotasks();
    const refreshRequest = http.expectOne('/api/v1/auth/refresh');

    const loggingOut = store.logout();
    expect(store.status()).toBe('anonymous');
    http.expectNone('/api/v1/auth/logout');

    refreshRequest.flush(panelTokens({ access_token: fakeJwt({ sub: 'user-1', n: 2 }) }));
    await refreshing;
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await loggingOut;

    expect(store.status()).toBe('anonymous');
    expect(store.accessToken()).toBeNull();
    const afterLogout = sync.posted.slice(
      sync.posted.findIndex((message) => message.type === 'logout') + 1,
    );
    expect(afterLogout.filter((message) => message.type === 'session')).toEqual([]);
  });

  // Sonda P2 del verificador.
  it('a logout from another tab is not undone by an in-flight refresh', async () => {
    sync.receive({
      type: 'session',
      tokens: panelTokens({ access_token: fakeJwt({ sub: 'user-1', n: 1 }) }),
      email: null,
    });
    const refreshing = store.refresh(store.accessToken());
    await flushMicrotasks();
    const refreshRequest = http.expectOne('/api/v1/auth/refresh');

    sync.receive({ type: 'logout' });
    expect(store.status()).toBe('anonymous');
    refreshRequest.flush(panelTokens({ access_token: fakeJwt({ sub: 'user-1', n: 2 }) }));

    expect(await refreshing).toBe(false);
    expect(store.status()).toBe('anonymous');
    expect(store.accessToken()).toBeNull();
    expect(sync.posted.filter((message) => message.type === 'session')).toEqual([]);
  });

  it('shares one logout between concurrent callers and ignores a repeated one', async () => {
    sync.receive({ type: 'session', tokens: panelTokens(), email: null });

    const first = store.logout();
    const second = store.logout();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await Promise.all([first, second]);
    await store.logout();

    http.expectNone('/api/v1/auth/logout');
    expect(sync.posted.filter((message) => message.type === 'logout')).toHaveLength(1);
    expect(navigate).toHaveBeenCalledOnce();
  });

  it('logs out for inactivity, tells the other tabs why and opens the login with the reason', async () => {
    sync.receive({ type: 'session', tokens: panelTokens(), email: null });

    const done = store.logout({ reason: 'inactivity' });
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await done;

    expect(store.status()).toBe('anonymous');
    expect(sync.posted).toEqual([{ type: 'logout', reason: 'inactivity' }]);
    expect(navigate).toHaveBeenCalledWith('/login?reason=inactivity');
  });

  it('opens the login with the reason when another tab logs out for inactivity', () => {
    sync.receive({ type: 'session', tokens: panelTokens(), email: null });

    sync.receive({ type: 'logout', reason: 'inactivity' });

    expect(store.status()).toBe('anonymous');
    expect(navigate).toHaveBeenCalledWith('/login?reason=inactivity');
  });

  it('switches the active company with a refresh that carries company_id', async () => {
    sync.receive({
      type: 'session',
      tokens: panelTokens({ company_id: null }),
      email: 'ana@example.test',
    });
    const renewed = panelTokens({ company_id: 'company-2' });

    const done = store.selectCompany('company-2');
    await flushMicrotasks();
    const request = http.expectOne('/api/v1/auth/refresh');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-Client')).toBe('panel');
    expect(request.request.body).toEqual({ company_id: 'company-2', refresh_token: null });
    request.flush(renewed);
    await done;

    expect(store.companyId()).toBe('company-2');
    expect(store.accessToken()).toBe(renewed.access_token);
    expect(store.user()).toEqual({ id: 'user-1', email: 'ana@example.test' });
    expect(sync.posted).toEqual([{ type: 'session', tokens: renewed, email: 'ana@example.test' }]);
  });

  it('keeps the session and rethrows when the API refuses the company (403)', async () => {
    sync.receive({ type: 'session', tokens: panelTokens({ company_id: null }), email: null });

    const done = store.selectCompany('company-9');
    await flushMicrotasks();
    http
      .expectOne('/api/v1/auth/refresh')
      .flush({ code: 'identity.company_not_allowed' }, { status: 403, statusText: 'Forbidden' });

    await expect(done).rejects.toMatchObject({ status: 403 });
    expect(store.status()).toBe('authenticated');
    expect(store.companyId()).toBeNull();
  });

  it('ends the session and rethrows when the company switch answers 401', async () => {
    sync.receive({ type: 'session', tokens: panelTokens({ company_id: null }), email: null });

    const done = store.selectCompany('company-2');
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(done).rejects.toMatchObject({ status: 401 });
    expect(store.status()).toBe('anonymous');
  });

  it('a logout during a company switch is not undone by its answer', async () => {
    sync.receive({ type: 'session', tokens: panelTokens({ company_id: null }), email: null });

    const switching = store.selectCompany('company-2');
    await flushMicrotasks();
    const request = http.expectOne('/api/v1/auth/refresh');
    const loggingOut = store.logout();
    http.expectNone('/api/v1/auth/logout');
    request.flush(panelTokens({ company_id: 'company-2' }));
    await switching.catch(() => undefined);
    await flushMicrotasks();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await loggingOut;

    expect(store.status()).toBe('anonymous');
    expect(store.companyId()).toBeNull();
  });

  it('logs out locally when another tab logs out', () => {
    sync.receive({ type: 'session', tokens: panelTokens(), email: 'ana@example.test' });
    expect(store.user()).toEqual({ id: 'user-1', email: 'ana@example.test' });

    sync.receive({ type: 'logout' });

    expect(store.status()).toBe('anonymous');
    expect(store.user()).toBeNull();
    expect(sync.posted).toEqual([]);
    expect(navigate).toHaveBeenCalledWith('/login');
  });
});
