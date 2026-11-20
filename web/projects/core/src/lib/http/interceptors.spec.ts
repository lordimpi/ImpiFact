import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiConfiguration } from '../api-shell';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { firstValueFrom, of } from 'rxjs';
import { AppError } from '../errors/app-error';
import { ToastStore } from '../notifications/toast.store';
import { SessionStore } from '../session/session.store';
import { FakeSessionSync, fakeJwt, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { CORRELATION_HEADER } from './api-request';
import { coreInterceptors } from './interceptors';

const API = 'https://api.example.test';
const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve));

class InMemoryLoader implements TranslocoLoader {
  getTranslation() {
    return of({ errors: { network: 'Sin conexión con el servidor.' } } as Translation);
  }
}

describe('core interceptors', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let session: InstanceType<typeof SessionStore>;
  let toasts: InstanceType<typeof ToastStore>;
  let sync: FakeSessionSync;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    const fake = provideFakeSessionSync();
    sync = fake.sync;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors(coreInterceptors)),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTransloco({
          config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
          loader: InMemoryLoader,
        }),
        fake.provider,
      ],
    });
    TestBed.inject(ApiConfiguration).rootUrl = API;
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    session = TestBed.inject(SessionStore);
    toasts = TestBed.inject(ToastStore);
  });

  afterEach(() => http.verify());

  function signIn(token = fakeJwt({ sub: 'user-1', n: 1 })) {
    sync.receive({ type: 'session', tokens: panelTokens({ access_token: token }), email: null });
    return token;
  }

  describe('authInterceptor and correlationInterceptor', () => {
    it('adds the bearer token and a correlation id to API requests', () => {
      const token = signIn();
      void firstValueFrom(client.get(`${API}/api/v1/plans`));

      const request = http.expectOne(`${API}/api/v1/plans`);
      expect(request.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
      expect(request.request.headers.get(CORRELATION_HEADER)).toMatch(/^[0-9a-f-]{36}$/);
      expect(request.request.withCredentials).toBe(false);
      request.flush({});
    });

    it('sends session requests with credentials and without a bearer token', () => {
      signIn();
      void firstValueFrom(client.post(`${API}/api/v1/auth/refresh`, null));

      const request = http.expectOne(`${API}/api/v1/auth/refresh`);
      expect(request.request.withCredentials).toBe(true);
      expect(request.request.headers.has('Authorization')).toBe(false);
      request.flush({});
    });

    it('leaves requests outside the API untouched', () => {
      signIn();
      void firstValueFrom(client.get('i18n/es-CO.json'));

      const request = http.expectOne('i18n/es-CO.json');
      expect(request.request.headers.has('Authorization')).toBe(false);
      expect(request.request.headers.has(CORRELATION_HEADER)).toBe(false);
      request.flush({});
    });
  });

  describe('refreshInterceptor', () => {
    it('refreshes once on 401 and retries the request once with the new token', async () => {
      signIn(fakeJwt({ sub: 'user-1', n: 1 }));
      const renewed = panelTokens({ access_token: fakeJwt({ sub: 'user-1', n: 2 }) });
      const response = firstValueFrom(client.get(`${API}/api/v1/plans`));

      http
        .expectOne(`${API}/api/v1/plans`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      http.expectOne(`${API}/api/v1/auth/refresh`).flush(renewed);
      await flushMicrotasks();
      const retry = http.expectOne(`${API}/api/v1/plans`);
      expect(retry.request.headers.get('Authorization')).toBe(`Bearer ${renewed.access_token}`);
      retry.flush({ plans: [] });

      await expect(response).resolves.toEqual({ plans: [] });
    });

    it('shares a single refresh between concurrent 401 responses', async () => {
      signIn();
      const renewed = panelTokens({ access_token: fakeJwt({ sub: 'user-1', n: 2 }) });
      const first = firstValueFrom(client.get(`${API}/api/v1/plans`));
      const second = firstValueFrom(client.get(`${API}/api/v1/documents`));

      http
        .expectOne(`${API}/api/v1/plans`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      http
        .expectOne(`${API}/api/v1/documents`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      http.expectOne(`${API}/api/v1/auth/refresh`).flush(renewed);
      await flushMicrotasks();
      http.expectOne(`${API}/api/v1/plans`).flush({ id: 'plans' });
      http.expectOne(`${API}/api/v1/documents`).flush({ id: 'documents' });

      await expect(first).resolves.toEqual({ id: 'plans' });
      await expect(second).resolves.toEqual({ id: 'documents' });
    });

    it('logs out when the retried request answers 401 again', async () => {
      signIn();
      const response = firstValueFrom(client.get(`${API}/api/v1/plans`));
      const failure = response.catch((error: unknown) => error);

      http
        .expectOne(`${API}/api/v1/plans`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      http
        .expectOne(`${API}/api/v1/auth/refresh`)
        .flush(panelTokens({ access_token: fakeJwt({ sub: 'user-1', n: 2 }) }));
      await flushMicrotasks();
      http
        .expectOne(`${API}/api/v1/plans`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      http
        .expectOne(`${API}/api/v1/auth/logout`)
        .flush(null, { status: 204, statusText: 'No Content' });
      await flushMicrotasks();

      const error = await failure;
      expect(error).toBeInstanceOf(AppError);
      expect((error as AppError).status).toBe(401);
      expect(session.status()).toBe('anonymous');
      expect(navigate).toHaveBeenCalledWith('/login');
      http.expectNone(`${API}/api/v1/auth/refresh`);
    });

    it('logs out without retrying when the refresh itself answers 401', async () => {
      signIn();
      const failure = firstValueFrom(client.get(`${API}/api/v1/plans`)).catch((e: unknown) => e);

      http
        .expectOne(`${API}/api/v1/plans`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      http
        .expectOne(`${API}/api/v1/auth/refresh`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      http
        .expectOne(`${API}/api/v1/auth/logout`)
        .flush(null, { status: 204, statusText: 'No Content' });
      await flushMicrotasks();

      expect(await failure).toBeInstanceOf(AppError);
      http.expectNone(`${API}/api/v1/plans`);
      expect(navigate).toHaveBeenCalledWith('/login');
    });
  });

  describe('refreshInterceptor with several requests', () => {
    // Sonda P5 del verificador (nota 2).
    it('logs out once when concurrent 401 responses share a failed refresh', async () => {
      signIn();
      const first = firstValueFrom(client.get(`${API}/api/v1/plans`)).catch((e: unknown) => e);
      const second = firstValueFrom(client.get(`${API}/api/v1/companies`)).catch((e: unknown) => e);

      http
        .expectOne(`${API}/api/v1/plans`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      http
        .expectOne(`${API}/api/v1/companies`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      http
        .expectOne(`${API}/api/v1/auth/refresh`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      await flushMicrotasks();
      const logouts = http.match(`${API}/api/v1/auth/logout`);
      logouts.forEach((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      await Promise.all([first, second]);

      expect(logouts).toHaveLength(1);
      expect(sync.posted.filter((message) => message.type === 'logout')).toHaveLength(1);
      expect(navigate).toHaveBeenCalledOnce();
      expect(session.status()).toBe('anonymous');
    });
  });

  describe('errorInterceptor', () => {
    it('maps a ProblemDetails response to an AppError with field errors and correlation id', async () => {
      signIn();
      const failure = firstValueFrom(client.post(`${API}/api/v1/companies/1/2`, {})).catch(
        (error: unknown) => error,
      );

      http.expectOne(`${API}/api/v1/companies/1/2`).flush(
        {
          title: 'Datos no procesables',
          status: 422,
          code: 'validation.failed',
          message: 'La solicitud tiene datos inválidos.',
          errors: { nit: ['El NIT es obligatorio.'] },
          correlation_id: 'abc-123',
        },
        { status: 422, statusText: 'Unprocessable Entity' },
      );

      const error = (await failure) as AppError;
      expect(error).toBeInstanceOf(AppError);
      expect(error.code).toBe('validation.failed');
      expect(error.message).toBe('La solicitud tiene datos inválidos.');
      expect(error.status).toBe(422);
      expect(error.fieldErrors).toEqual({ nit: ['El NIT es obligatorio.'] });
      expect(error.correlationId).toBe('abc-123');
      // Los errores por campo los muestra el formulario: no hay toast.
      expect(toasts.toasts()).toEqual([]);
    });

    it('shows a toast with the correlation id for errors without field errors', async () => {
      signIn();
      const failure = firstValueFrom(client.get(`${API}/api/v1/plans`)).catch((e: unknown) => e);

      http.expectOne(`${API}/api/v1/plans`).flush(
        { code: 'plan.limit_reached', message: 'Se agotó el plan.' },
        {
          status: 409,
          statusText: 'Conflict',
          headers: { 'X-Correlation-Id': 'from-header' },
        },
      );

      const error = (await failure) as AppError;
      expect(error.code).toBe('plan.limit_reached');
      expect(error.correlationId).toBe('from-header');
      expect(toasts.toasts()).toEqual([
        expect.objectContaining({
          kind: 'error',
          text: 'Se agotó el plan.',
          correlationId: 'from-header',
        }),
      ]);
    });

    it('maps a network failure to network.unavailable with the correlation id that was sent', async () => {
      signIn();
      const failure = firstValueFrom(client.get(`${API}/api/v1/plans`)).catch((e: unknown) => e);

      const request = http.expectOne(`${API}/api/v1/plans`);
      const sent = request.request.headers.get(CORRELATION_HEADER);
      request.error(new ProgressEvent('error'), { status: 0, statusText: '' });

      const error = (await failure) as AppError;
      expect(error.code).toBe('network.unavailable');
      expect(error.status).toBe(0);
      expect(error.correlationId).toBe(sent);
      expect(toasts.toasts()).toHaveLength(1);
    });
  });
});
