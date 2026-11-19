import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { FakeSessionSync, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { SessionStore } from './session.store';

describe('SessionStore.selectCompany regressions', () => {
  let store: InstanceType<typeof SessionStore>;
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
        fake.provider,
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    store = TestBed.inject(SessionStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());
  const tick = () => new Promise((resolve) => setTimeout(resolve));
  const signIn = () =>
    sync.receive({ type: 'session', tokens: panelTokens({ company_id: null }), email: null });

  it('a logout from another tab during the switch discards the answer and posts nothing', async () => {
    signIn();
    const switching = store.selectCompany('company-2');
    await tick();
    const request = http.expectOne('/api/v1/auth/refresh');
    sync.receive({ type: 'logout' });
    request.flush(panelTokens({ company_id: 'company-2' }));
    await switching;
    expect(store.status()).toBe('anonymous');
    expect(store.companyId()).toBeNull();
    expect(sync.posted).toEqual([]);
  });

  it('waits for the cross-tab lock before calling the API', async () => {
    signIn();
    const release = sync.holdLock();
    const switching = store.selectCompany('company-2');
    await tick();
    http.expectNone('/api/v1/auth/refresh');
    release();
    await tick();
    http.expectOne('/api/v1/auth/refresh').flush(panelTokens({ company_id: 'company-2' }));
    await switching;
    expect(store.companyId()).toBe('company-2');
  });

  it('a logout while the switch waits for the lock: no switch call, one logout', async () => {
    signIn();
    const release = sync.holdLock();
    const switching = store.selectCompany('company-2');
    await tick();
    const loggingOut = store.logout();
    release();
    await switching;
    await tick();
    http.expectNone('/api/v1/auth/refresh');
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await loggingOut;
    expect(store.status()).toBe('anonymous');
  });

  it('waits for an in-flight refresh and never sends two renewals at once', async () => {
    signIn();
    const refreshing = store.refresh(store.accessToken());
    await tick();
    const first = http.expectOne('/api/v1/auth/refresh');
    expect(first.request.body).toBeNull();
    const switching = store.selectCompany('company-2');
    await tick();
    http.expectNone('/api/v1/auth/refresh');
    first.flush(panelTokens({ company_id: null }));
    await refreshing;
    await tick();
    const second = http.expectOne('/api/v1/auth/refresh');
    expect(second.request.body).toEqual({ company_id: 'company-2', refresh_token: null });
    second.flush(panelTokens({ company_id: 'company-2' }));
    await switching;
    expect(store.companyId()).toBe('company-2');
  });

  it('a refresh asked during the switch shares it', async () => {
    signIn();
    const switching = store.selectCompany('company-2');
    await tick();
    const shared = store.refresh('stale');
    await tick();
    http.expectOne('/api/v1/auth/refresh').flush(panelTokens({ company_id: 'company-2' }));
    await switching;
    expect(await shared).toBe(true);
  });

  it('retries once on 409', async () => {
    signIn();
    const switching = store.selectCompany('company-2');
    await tick();
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 409, statusText: 'Conflict' });
    await tick();
    http.expectOne('/api/v1/auth/refresh').flush(panelTokens({ company_id: 'company-2' }));
    await switching;
    expect(store.companyId()).toBe('company-2');
  });

  it('a network failure keeps the session and rethrows', async () => {
    signIn();
    const switching = store.selectCompany('company-2');
    await tick();
    http.expectOne('/api/v1/auth/refresh').error(new ProgressEvent('error'));
    await expect(switching).rejects.toBeTruthy();
    expect(store.status()).toBe('authenticated');
  });

  it('without a session it does not call the API', async () => {
    await store.selectCompany('company-2');
    await tick();
    http.expectNone('/api/v1/auth/refresh');
  });

  it('a late switch answer never overrides a new login after a logout', async () => {
    signIn();
    const switching = store.selectCompany('company-2');
    await tick();
    const request = http.expectOne('/api/v1/auth/refresh');
    const loggingOut = store.logout();
    const login = store.login({ email: 'b@example.test', password: 'x' });
    http.expectOne('/api/v1/auth/login').flush(panelTokens({ company_id: 'company-1' }));
    await login;
    request.flush(panelTokens({ company_id: 'company-2' }));
    await switching;
    await tick();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await loggingOut;
    expect(store.companyId()).toBe('company-1');
    expect(store.email()).toBe('b@example.test');
  });

  it('another tab switching company propagates here', () => {
    signIn();
    sync.receive({
      type: 'session',
      tokens: panelTokens({ company_id: 'company-2' }),
      email: null,
    });
    expect(store.companyId()).toBe('company-2');
  });
});
