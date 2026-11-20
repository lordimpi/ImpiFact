import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { APP_BOOTSTRAP_LISTENER, ApplicationInitStatus, ComponentRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiConfiguration } from './api-shell';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import { CORRELATION_HEADER } from './http/api-request';
import { coreInterceptors } from './http/interceptors';
import { ToastStore } from './notifications/toast.store';
import { provideCore } from './provide-core';
import { CORE_ROUTE_PATHS } from './session/route-paths';
import { SessionStore } from './session/session.store';
import { provideFakeSessionSync } from './testing/fakes';

class InMemoryLoader implements TranslocoLoader {
  getTranslation() {
    return of({} as Translation);
  }
}

const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve));

describe('provideCore', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors(coreInterceptors)),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTransloco({
          config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
          loader: InMemoryLoader,
        }),
        provideCore({ routePaths: { login: '/ingresar' } }),
        provideFakeSessionSync().provider,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function start() {
    const done = TestBed.inject(ApplicationInitStatus).donePromise;
    await flushMicrotasks();
    http.expectOne('config.json').flush({ apiBaseUrl: 'http://127.0.0.1:5080' });
    await done;
    TestBed.inject(APP_BOOTSTRAP_LISTENER).forEach((listener) =>
      listener({} as ComponentRef<unknown>),
    );
    await flushMicrotasks();
  }

  it('sets the API url from config.json before the app starts', async () => {
    await start();
    http.expectOne('http://127.0.0.1:5080/api/v1/auth/refresh').flush(null, {
      status: 401,
      statusText: 'Unauthorized',
    });

    expect(TestBed.inject(ApiConfiguration).rootUrl).toBe('http://127.0.0.1:5080');
    expect(TestBed.inject(CORE_ROUTE_PATHS)).toEqual({
      login: '/ingresar',
      home: '/',
      selectCompany: '/empresas/seleccionar',
    });
  });

  it('restores the session once at startup and stays anonymous without errors when there is none', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    await start();

    const refresh = http.expectOne('http://127.0.0.1:5080/api/v1/auth/refresh');
    expect(refresh.request.withCredentials).toBe(true);
    expect(refresh.request.headers.get('X-Client')).toBe('panel');
    expect(refresh.request.headers.has(CORRELATION_HEADER)).toBe(true);
    refresh.flush(null, { status: 401, statusText: 'Unauthorized' });
    await flushMicrotasks();

    expect(TestBed.inject(SessionStore).status()).toBe('anonymous');
    expect(TestBed.inject(ToastStore).toasts()).toEqual([]);
    expect(navigate).not.toHaveBeenCalled();
  });
});
