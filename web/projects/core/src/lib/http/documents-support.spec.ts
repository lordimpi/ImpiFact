import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiConfiguration } from '../api-shell';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { firstValueFrom, of } from 'rxjs';
import { AppError } from '../errors/app-error';
import { ToastStore } from '../notifications/toast.store';
import { FakeSessionSync, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { IDEMPOTENCY_HEADER, IDEMPOTENCY_KEY } from './api-request';
import { coreInterceptors } from './interceptors';

/*
 * Lo que agregó la tarea de documentos a los interceptores: el encabezado `Idempotency-Key` desde el contexto de la
 * solicitud y la lectura de los errores que llegan como archivo (descargas de PDF, XML y CSV).
 */

const API = 'https://api.example.test';

class InMemoryLoader implements TranslocoLoader {
  getTranslation() {
    return of({ errors: { unexpected: 'Error inesperado.' } } as Translation);
  }
}

describe('core interceptors for documents', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let sync: FakeSessionSync;

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
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    sync.receive({ type: 'session', tokens: panelTokens(), email: null });
  });

  afterEach(() => http.verify());

  it('sends the Idempotency-Key header that the request context carries', () => {
    const context = new HttpContext().set(IDEMPOTENCY_KEY, 'key-123');
    void firstValueFrom(client.post(`${API}/api/v1/documents/invoices`, {}, { context }));

    const request = http.expectOne(`${API}/api/v1/documents/invoices`);
    expect(IDEMPOTENCY_HEADER).toBe('Idempotency-Key');
    expect(request.request.headers.get('Idempotency-Key')).toBe('key-123');
  });

  it('sends no Idempotency-Key header when the context has none', () => {
    void firstValueFrom(client.post(`${API}/api/v1/documents/calculate`, {}));

    const request = http.expectOne(`${API}/api/v1/documents/calculate`);
    expect(request.request.headers.has('Idempotency-Key')).toBe(false);
  });

  it('reads the ProblemDetails of an error that arrives as a blob', async () => {
    const failure = firstValueFrom(
      client.get(`${API}/api/v1/documents/export.csv`, { responseType: 'blob' }),
    ).catch((error: unknown) => error);

    const problem = {
      code: 'documents.export_too_large',
      message: 'La exportación supera 50.000 filas.',
      correlation_id: 'csv-1',
    };
    http
      .expectOne(`${API}/api/v1/documents/export.csv`)
      .flush(new Blob([JSON.stringify(problem)], { type: 'application/problem+json' }), {
        status: 422,
        statusText: 'Unprocessable Entity',
      });

    const error = (await failure) as AppError;
    expect(error).toBeInstanceOf(AppError);
    expect(error.code).toBe('documents.export_too_large');
    expect(error.message).toBe('La exportación supera 50.000 filas.');
    expect(error.correlationId).toBe('csv-1');
    expect(TestBed.inject(ToastStore).toasts()).toEqual([
      expect.objectContaining({ kind: 'error', text: 'La exportación supera 50.000 filas.' }),
    ]);
  });
});
