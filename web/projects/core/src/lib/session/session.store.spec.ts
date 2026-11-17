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
    request.flush(panelTokens());
    await Promise.all([first, second]);

    expect(store.status()).toBe('authenticated');
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

    expect(sync.posted).toEqual([{ type: 'session', tokens, email: null }]);
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
