import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MyCompany } from '../api-shell';
import { SKIP_ERROR_TOAST } from '../http/api-request';
import { SessionStore } from '../session/session.store';
import { MyCompaniesStore } from './my-companies.store';

const ACME: MyCompany = {
  id: 'company-1',
  name: 'Acme SAS',
  nit: '900123456',
  dv: '7',
  roles: ['owner'],
  is_active: true,
};
const BETA: MyCompany = {
  id: 'company-2',
  name: 'Beta Ltda',
  nit: '800111222',
  dv: '3',
  roles: ['seller'],
  is_active: false,
};

describe('MyCompaniesStore', () => {
  let http: HttpTestingController;
  const user = signal<{ id: string; email: string | null } | null>(null);
  const companyId = signal<string | null>(null);

  function setUp() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: { user, companyId } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(MyCompaniesStore);
  }

  beforeEach(() => {
    user.set(null);
    companyId.set(null);
  });

  afterEach(() => http.verify());

  it('loads the companies of the signed-in user without a toast on failure', async () => {
    const store = setUp();
    user.set({ id: 'user-1', email: null });
    TestBed.tick();

    const request = http.expectOne('/api/v1/me/companies');
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
    expect(store.status()).toBe('loading');
    request.flush({ items: [ACME, BETA] });
    await TestBed.inject(MyCompaniesStore).whenLoaded();

    expect(store.status()).toBe('ready');
    expect(store.items()).toEqual([ACME, BETA]);
    expect(store.byId('company-2')).toEqual(BETA);
    expect(store.byId('missing')).toBeNull();
  });

  it('exposes the active company of the session', async () => {
    const store = setUp();
    user.set({ id: 'user-1', email: null });
    TestBed.tick();
    http.expectOne('/api/v1/me/companies').flush({ items: [ACME, BETA] });
    await store.whenLoaded();

    expect(store.active()).toBeNull();
    companyId.set('company-1');
    expect(store.active()).toEqual(ACME);
  });

  it('keeps the list empty and marks the error when the API fails', async () => {
    const store = setUp();
    user.set({ id: 'user-1', email: null });
    TestBed.tick();
    http
      .expectOne('/api/v1/me/companies')
      .flush({ code: 'boom' }, { status: 500, statusText: 'Error' });
    await store.whenLoaded();

    expect(store.status()).toBe('error');
    expect(store.items()).toEqual([]);
  });

  it('forgets the list on logout and loads again for the next user', async () => {
    const store = setUp();
    user.set({ id: 'user-1', email: null });
    TestBed.tick();
    http.expectOne('/api/v1/me/companies').flush({ items: [ACME] });
    await store.whenLoaded();

    user.set(null);
    TestBed.tick();
    expect(store.items()).toEqual([]);
    expect(store.status()).toBe('idle');

    user.set({ id: 'user-2', email: null });
    TestBed.tick();
    http.expectOne('/api/v1/me/companies').flush({ items: [BETA] });
    await store.whenLoaded();
    expect(store.items()).toEqual([BETA]);
  });

  it('reloads on demand, for example after the company data changed', async () => {
    const store = setUp();
    user.set({ id: 'user-1', email: null });
    TestBed.tick();
    http.expectOne('/api/v1/me/companies').flush({ items: [ACME] });
    await store.whenLoaded();

    const reload = store.reload();
    http.expectOne('/api/v1/me/companies').flush({ items: [{ ...ACME, name: 'Acme Nueva SAS' }] });
    await reload;

    expect(store.byId('company-1')?.name).toBe('Acme Nueva SAS');
  });
});
