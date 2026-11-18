import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SKIP_ERROR_TOAST } from '../http/api-request';
import { CATALOG_PAGE_SIZE, CatalogsStore, catalogLabel, optionsWith } from './catalogs.store';

const url = (name: string) => `/api/v1/catalogs/${name}`;

const REGIMES = {
  name: 'regimes',
  version: 'v1',
  next_cursor: null,
  items: [
    { id: 1, code: '48', name: 'Responsable de IVA', is_active: true },
    { id: 2, code: '49', name: 'No Responsable de IVA', is_active: true },
  ],
};

function municipalities(ids: number[], next: string | null) {
  return {
    name: 'municipalities',
    version: 'm1',
    next_cursor: next,
    items: ids.map((id) => ({
      id,
      code: String(id).padStart(5, '0'),
      name: `Municipio ${id}`,
      department_id: 2,
      is_active: true,
    })),
  };
}

describe('CatalogsStore', () => {
  let http: HttpTestingController;
  let store: InstanceType<typeof CatalogsStore>;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(CatalogsStore);
  });

  afterEach(() => http.verify());

  it('loads a small catalog once per session and maps its rows', async () => {
    const first = store.load('regimes');
    const request = http.expectOne(url('regimes'));
    expect(request.request.headers.has('If-None-Match')).toBe(false);
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
    request.flush(REGIMES);

    const items = await first;
    expect(items.map((item) => [item.id, item.code, item.name])).toEqual([
      [1, '48', 'Responsable de IVA'],
      [2, '49', 'No Responsable de IVA'],
    ]);
    expect(store.items('regimes')().length).toBe(2);
    expect(store.status('regimes')()).toBe('ready');

    // La segunda vez sale de la memoria: no hay otra solicitud.
    await expect(store.load('regimes')).resolves.toBe(items);
    http.expectNone(url('regimes'));
  });

  it('revalidates the copy saved in the session with If-None-Match and keeps it on 304', async () => {
    sessionStorage.setItem(
      'impifact.catalog.regimes',
      JSON.stringify({ version: 'v1', items: [{ id: 1, code: '48', name: 'Guardado' }] }),
    );

    const loading = store.load('regimes');
    const request = http.expectOne(url('regimes'));
    expect(request.request.headers.get('If-None-Match')).toBe('"v1"');
    request.flush(null, { status: 304, statusText: 'Not Modified' });

    const items = await loading;
    expect(items.map((item) => item.name)).toEqual(['Guardado']);
  });

  it('replaces the saved copy when the catalog changed', async () => {
    sessionStorage.setItem(
      'impifact.catalog.regimes',
      JSON.stringify({ version: 'v0', items: [{ id: 1, code: '48', name: 'Viejo' }] }),
    );

    const loading = store.load('regimes');
    http.expectOne(url('regimes')).flush(REGIMES);

    expect((await loading).length).toBe(2);
    const saved = JSON.parse(sessionStorage.getItem('impifact.catalog.regimes') ?? '{}');
    expect(saved.version).toBe('v1');
    expect(saved.items.length).toBe(2);
  });

  it('marks the catalog as failed and lets the next load retry', async () => {
    const loading = store.load('regimes');
    http.expectOne(url('regimes')).flush(null, { status: 500, statusText: 'Error' });
    await expect(loading).resolves.toEqual([]);
    expect(store.status('regimes')()).toBe('error');

    const retry = store.load('regimes');
    http.expectOne(url('regimes')).flush(REGIMES);
    expect((await retry).length).toBe(2);
  });

  it('searches a long catalog on the server, page by page, and caches each page', async () => {
    const first = store.page('municipalities', { q: 'bogo' });
    const request = http.expectOne((r) => r.url === url('municipalities'));
    expect(request.request.params.get('q')).toBe('bogo');
    expect(request.request.params.get('limit')).toBe(String(CATALOG_PAGE_SIZE));
    expect(request.request.params.has('cursor')).toBe(false);
    request.flush(municipalities([149], 'c2'));

    const page = await first;
    expect(page.items.map((item) => item.id)).toEqual([149]);
    expect(page.nextCursor).toBe('c2');

    const next = store.page('municipalities', { q: 'bogo', cursor: 'c2' });
    const more = http.expectOne((r) => r.url === url('municipalities'));
    expect(more.request.params.get('cursor')).toBe('c2');
    more.flush(municipalities([150], null));
    expect((await next).nextCursor).toBeNull();

    await store.page('municipalities', { q: 'bogo' });
    http.expectNone((r) => r.url === url('municipalities'));
  });

  it('finds a row of a long catalog by its id walking the pages in id order', async () => {
    const finding = store.find('municipalities', 149);
    http.expectOne((r) => r.url === url('municipalities')).flush(municipalities([1, 2], 'c2'));
    await vi.waitFor(() =>
      http
        .expectOne((r) => r.url === url('municipalities') && r.params.get('cursor') === 'c2')
        .flush(municipalities([148, 149, 150], 'c3')),
    );
    expect((await finding)?.name).toBe('Municipio 149');

    // Un id que ya quedó atrás en el orden no existe: no se piden más páginas.
    await expect(store.find('municipalities', 3)).resolves.toBeNull();
  });
});

describe('catalogLabel', () => {
  const item = { id: 7, code: 'O-13', name: 'Gran contribuyente', description: null };

  it('shows each catalog the way the DIAN forms name it', () => {
    expect(catalogLabel('liabilities', item)).toBe('O-13 Gran contribuyente');
    expect(catalogLabel('unit-measures', { ...item, code: '94', name: 'unidad' })).toBe(
      'unidad (94)',
    );
    expect(
      catalogLabel('taxes', { ...item, code: '01', name: 'IVA', description: 'Impuesto' }),
    ).toBe('IVA (Impuesto)');
    expect(catalogLabel('regimes', { ...item, name: 'Responsable de IVA' })).toBe(
      'Responsable de IVA',
    );
  });
});

describe('optionsWith', () => {
  const options = [{ id: 1, name: 'Uno' }];

  it('keeps a saved id that is not in the list', () => {
    const other = (id: number) => `Otro (id ${id})`;
    expect(optionsWith(options, '1', other)).toBe(options);
    expect(optionsWith(options, '', other)).toBe(options);
    expect(optionsWith(options, '9', other)).toEqual([...options, { id: 9, name: 'Otro (id 9)' }]);
  });
});
