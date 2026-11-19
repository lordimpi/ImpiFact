import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, runInInjectionContext, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CATALOG_SEARCH_DEBOUNCE_MS, catalogSearch } from './catalog-search';

const URL = '/api/v1/catalogs/municipalities';

function page(ids: number[], next: string | null) {
  return {
    name: 'municipalities',
    version: 'm1',
    next_cursor: next,
    items: ids.map((id) => ({ id, code: String(id), name: `Municipio ${id}`, is_active: true })),
  };
}

describe('catalogSearch', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const settle = () => vi.advanceTimersByTimeAsync(0);
  const pending = () => http.match((request) => request.url === URL);

  function create(current = signal('')) {
    const search = runInInjectionContext(TestBed.inject(Injector), () =>
      catalogSearch('municipalities', {
        current,
        otherName: (id) => `Otro (id ${id})`,
      }),
    );
    TestBed.tick();
    return { search, current };
  }

  it('shows the first page at once and searches the server after a pause', async () => {
    const { search } = create();
    await settle();
    const [first] = pending();
    expect(first!.request.params.has('q')).toBe(false);
    first!.flush(page([1, 2], 'c2'));
    await settle();
    expect(search.options().map((option) => option.id)).toEqual([1, 2]);
    expect(search.state().status).toBe('ready');
    expect(search.hasMore()).toBe(true);

    search.search('bo');
    search.search('bogo');
    await vi.advanceTimersByTimeAsync(CATALOG_SEARCH_DEBOUNCE_MS);
    const requests = pending();
    expect(requests.length).toBe(1);
    expect(requests[0]!.request.params.get('q')).toBe('bogo');
    expect(search.state().status).toBe('loading');
    requests[0]!.flush(page([149], null));
    await settle();
    expect(search.options().map((option) => option.name)).toEqual(['Municipio 149']);
    expect(search.hasMore()).toBe(false);
  });

  it('appends the next page with the cursor of the current search', async () => {
    const { search } = create();
    await settle();
    pending()[0]!.flush(page([1, 2], 'c2'));
    await settle();

    search.loadMore();
    await settle();
    const [more] = pending();
    expect(more!.request.params.get('cursor')).toBe('c2');
    more!.flush(page([3], null));
    await settle();
    expect(search.options().map((option) => option.id)).toEqual([1, 2, 3]);
    expect(search.hasMore()).toBe(false);
  });

  it('keeps the saved value as an option and resolves its name', async () => {
    const { search } = create(signal('149'));
    await settle();
    // Mientras no se conoce el nombre, la opción guarda el id.
    expect(search.options().map((option) => option.name)).toEqual(['Otro (id 149)']);
    const [first] = pending();
    first!.flush(page([1, 149], null));
    await settle();
    expect(search.options().map((option) => option.name)).toEqual(['Municipio 1', 'Municipio 149']);

    // Otra búsqueda que no lo trae lo conserva con su nombre.
    search.search('medel');
    await vi.advanceTimersByTimeAsync(CATALOG_SEARCH_DEBOUNCE_MS);
    pending()[0]!.flush(page([1], null));
    await settle();
    expect(search.options().map((option) => option.name)).toEqual(['Municipio 1', 'Municipio 149']);
  });

  it('waits for a department and then searches only its municipalities', async () => {
    const department = signal<number | null>(null);
    const search = runInInjectionContext(TestBed.inject(Injector), () =>
      catalogSearch('municipalities', {
        current: () => '',
        otherName: (id) => `Otro (id ${id})`,
        filter: () => (department() === null ? null : { departmentId: department() }),
      }),
    );
    TestBed.tick();
    await settle();
    expect(pending()).toEqual([]);
    expect(search.state().status).toBe('idle');
    expect(search.options()).toEqual([]);

    department.set(11);
    TestBed.tick();
    await settle();
    const [cauca] = pending();
    expect(cauca!.request.params.get('department_id')).toBe('11');
    expect(cauca!.request.params.has('q')).toBe(false);
    cauca!.flush(page([362, 363], 'c2'));
    await settle();
    expect(search.options().map((option) => option.id)).toEqual([362, 363]);

    search.search('popa');
    await vi.advanceTimersByTimeAsync(CATALOG_SEARCH_DEBOUNCE_MS);
    const [typed] = pending();
    expect(typed!.request.params.get('department_id')).toBe('11');
    expect(typed!.request.params.get('q')).toBe('popa');
    typed!.flush(page([362], null));
    await settle();

    // Otro departamento empieza una búsqueda nueva, sin el texto anterior.
    department.set(5);
    TestBed.tick();
    await settle();
    const [bogota] = pending();
    expect(bogota!.request.params.get('department_id')).toBe('5');
    expect(bogota!.request.params.has('q')).toBe(false);
    expect(search.state().term).toBe('');
    bogota!.flush(page([149], null));
    await settle();
    expect(search.options().map((option) => option.id)).toEqual([149]);
  });

  it('reports a failed search and retries it', async () => {
    const { search } = create();
    await settle();
    pending()[0]!.flush(null, { status: 500, statusText: 'Error' });
    await settle();
    expect(search.state().status).toBe('error');

    search.retry();
    await settle();
    pending()[0]!.flush(page([1], null));
    await settle();
    expect(search.state().status).toBe('ready');
  });
});
