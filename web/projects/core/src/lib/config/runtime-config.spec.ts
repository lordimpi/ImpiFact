import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DEFAULT_RUNTIME_CONFIG, RuntimeConfigService } from './runtime-config';

describe('RuntimeConfigService', () => {
  let service: RuntimeConfigService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RuntimeConfigService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads config.json once and exposes it, without a trailing slash in the API url', async () => {
    const first = service.load();
    const second = service.load();
    http.expectOne('config.json').flush({
      apiBaseUrl: 'https://api.example.test/',
      inactivity: { warnMinutes: 10, logoutMinutes: 12 },
      features: { dashboard: true },
    });

    const config = await first;
    expect(await second).toBe(config);
    expect(config).toEqual({
      apiBaseUrl: 'https://api.example.test',
      inactivity: { warnMinutes: 10, logoutMinutes: 12 },
      features: { dashboard: true },
    });
    expect(service.config()).toEqual(config);
    expect(service.isEnabled('dashboard')).toBe(true);
    expect(service.isEnabled('other')).toBe(false);
  });

  it('fills missing or invalid values with the defaults', async () => {
    const loaded = service.load();
    http.expectOne('config.json').flush({
      apiBaseUrl: 'http://127.0.0.1:5080',
      inactivity: { warnMinutes: 40, logoutMinutes: 30 },
    });

    expect(await loaded).toEqual({
      apiBaseUrl: 'http://127.0.0.1:5080',
      inactivity: DEFAULT_RUNTIME_CONFIG.inactivity,
      features: {},
    });
  });

  // Sonda P7 del verificador (nota 4): solo URL absolutas http(s); http solo en el equipo local.
  it.each([
    'javascript:alert(1)//',
    'ftp://api.example.test',
    'http://api.example.test',
    '/api',
    'no es una url',
  ])('rejects %s as API url, keeping the default and logging without the value', async (url) => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const loaded = service.load();
    http.expectOne('config.json').flush({ apiBaseUrl: url });

    expect((await loaded).apiBaseUrl).toBe('');
    expect(error).toHaveBeenCalledOnce();
    expect(JSON.stringify(error.mock.calls)).not.toContain(url);
  });

  it.each([
    ['https://api.example.test/', 'https://api.example.test'],
    ['http://localhost:5080', 'http://localhost:5080'],
    ['http://127.0.0.1:5080', 'http://127.0.0.1:5080'],
    ['http://[::1]:5080', 'http://[::1]:5080'],
    ['', ''],
  ])('accepts %s as API url', async (url, expected) => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const loaded = service.load();
    http.expectOne('config.json').flush({ apiBaseUrl: url });

    expect((await loaded).apiBaseUrl).toBe(expected);
    expect(error).not.toHaveBeenCalled();
  });

  it('starts with the defaults when config.json cannot be read', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const loaded = service.load();
    http.expectOne('config.json').flush('missing', { status: 404, statusText: 'Not Found' });

    expect(await loaded).toEqual(DEFAULT_RUNTIME_CONFIG);
    expect(DEFAULT_RUNTIME_CONFIG.inactivity).toEqual({ warnMinutes: 25, logoutMinutes: 30 });
    expect(error).toHaveBeenCalledOnce();
  });
});
