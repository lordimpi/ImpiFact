import { ApplicationInitStatus } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import {
  Api,
  CompanySavedResponse,
  createCompany,
  PanelSessionTokens,
  refreshSession,
} from '../public-api';
import { provideApiRootUrl } from './api-root-url';

// El cliente generado funciona con la inyección de dependencias de Angular y pasa por HttpClient (y por lo tanto por
// los interceptores de core): se comprueba la URL, el método y el cuerpo con HttpTestingController, sin red.
describe('generated api client', () => {
  async function setUp(rootUrl: () => string | Promise<string>) {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideApiRootUrl(rootUrl)],
    });
    await TestBed.inject(ApplicationInitStatus).donePromise;
    return { api: TestBed.inject(Api), http: TestBed.inject(HttpTestingController) };
  }

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('builds the createCompany request from the root url set at startup', async () => {
    const { api, http } = await setUp(() => Promise.resolve('https://api.example.test'));
    const saved: CompanySavedResponse = {
      company: { nit: '900123456' } as CompanySavedResponse['company'],
    };

    const response = firstValueFrom(
      api.invoke(createCompany, { nit: '900123456', dv: '7', body: { business_name: 'Empresa' } }),
    );
    const request = http.expectOne('https://api.example.test/api/v1/companies/900123456/7');
    request.flush(saved);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ business_name: 'Empresa' });
    await expect(response).resolves.toEqual(saved);
  });

  // La respuesta se tipifica como SessionTokens | PanelSessionTokens; el panel la usa como PanelSessionTokens sin
  // conversiones (si el contrato cambiara y eso dejara de valer, esta prueba no compilaría).
  it('sends the panel header on refreshSession and types the panel session', async () => {
    const { api, http } = await setUp(() => '');
    const panel: PanelSessionTokens = {
      access_token: 'jwt',
      expires_at: '2026-11-17T12:15:00Z',
      refresh_token_expires_at: '2026-12-01T12:00:00Z',
      company_id: null,
      roles: ['owner'],
      companies: [],
      email: 'ana@example.test',
    };

    const response = firstValueFrom(api.invoke(refreshSession, { 'X-Client': 'panel' }));
    const request = http.expectOne('/api/v1/auth/refresh');
    request.flush(panel);
    const session: PanelSessionTokens = await response;

    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-Client')).toBe('panel');
    expect(session).toEqual(panel);
  });
});
