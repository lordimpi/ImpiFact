import { HttpClient, HttpParams, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { signalStore } from '@ngrx/signals';
import { map } from 'rxjs';
import { ServerListQuery, withServerList } from './with-server-list';

interface Doc {
  id: string;
}
interface DocFilters {
  status?: string;
}
interface DocPage {
  items: Doc[];
  next_cursor: string | null;
  total?: number;
}

// Convierte la consulta del store en la del Api, como lo haría el store de un feature con el cliente generado.
function toParams(query: ServerListQuery<DocFilters>): HttpParams {
  let params = new HttpParams().set('limit', query.pageSize);
  if (query.cursor) params = params.set('cursor', query.cursor);
  if (query.page > 1 && !query.cursor) params = params.set('page', query.page);
  if (query.sort) params = params.set('sort', `${query.sort.key}:${query.sort.direction}`);
  if (query.filters.status) params = params.set('status', query.filters.status);
  return params;
}

const DocumentsStore = signalStore(
  withServerList<Doc, DocFilters>({
    pageSize: 2,
    sort: { key: 'issued_at', direction: 'desc' },
    filters: {},
    loader: () => {
      const http = inject(HttpClient);
      return (query) =>
        http
          .get<DocPage>('/api/v1/documents', { params: toParams(query) })
          .pipe(
            map((page) => ({ items: page.items, nextCursor: page.next_cursor, total: page.total })),
          );
    },
  }),
);

const docs = (...ids: string[]) => ids.map((id) => ({ id }));

describe('withServerList', () => {
  let store: InstanceType<typeof DocumentsStore>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), DocumentsStore],
    });
    store = TestBed.inject(DocumentsStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const expectList = (params: string) =>
    http.expectOne((request) => request.urlWithParams === `/api/v1/documents?${params}`);

  it('loads the first page with the page size, sort and filters, and tracks loading', () => {
    store.load();
    expect(store.loading()).toBe(true);

    expectList('limit=2&sort=issued_at:desc').flush({ items: docs('a', 'b'), next_cursor: 'c2' });

    expect(store.loading()).toBe(false);
    expect(store.items()).toEqual(docs('a', 'b'));
    expect(store.hasNext()).toBe(true);
    expect(store.hasPrevious()).toBe(false);
    expect(store.empty()).toBe(false);
    expect(store.pagination()).toEqual({ kind: 'cursor', previousCursor: null, nextCursor: 'c2' });
  });

  it('shows exactly what the server returns and never pages locally', () => {
    store.load();
    expectList('limit=2&sort=issued_at:desc').flush({
      items: docs('a', 'b', 'c'),
      next_cursor: null,
    });

    expect(store.items()).toEqual(docs('a', 'b', 'c'));
    expect(store.hasNext()).toBe(false);
  });

  it('asks the server for the next page with its cursor and goes back through the visited cursors', () => {
    store.load();
    expectList('limit=2&sort=issued_at:desc').flush({ items: docs('a', 'b'), next_cursor: 'c2' });

    store.changePage({ kind: 'cursor', cursor: 'c2', direction: 'next' });
    expectList('limit=2&cursor=c2&sort=issued_at:desc').flush({
      items: docs('c', 'd'),
      next_cursor: 'c3',
    });
    expect(store.items()).toEqual(docs('c', 'd'));
    expect(store.hasPrevious()).toBe(true);

    store.nextPage();
    expectList('limit=2&cursor=c3&sort=issued_at:desc').flush({
      items: docs('e'),
      next_cursor: null,
    });

    store.previousPage();
    expectList('limit=2&cursor=c2&sort=issued_at:desc').flush({
      items: docs('c', 'd'),
      next_cursor: 'c3',
    });
    store.changePage({ kind: 'cursor', cursor: '', direction: 'previous' });
    expectList('limit=2&sort=issued_at:desc').flush({ items: docs('a', 'b'), next_cursor: 'c2' });
    expect(store.hasPrevious()).toBe(false);
  });

  it('goes back to the first page when the sort or the filters change', () => {
    store.load();
    expectList('limit=2&sort=issued_at:desc').flush({ items: docs('a', 'b'), next_cursor: 'c2' });
    store.nextPage();
    expectList('limit=2&cursor=c2&sort=issued_at:desc').flush({
      items: docs('c'),
      next_cursor: null,
    });

    store.setFilters({ status: 'Accepted' });
    expectList('limit=2&sort=issued_at:desc&status=Accepted').flush({
      items: docs('x'),
      next_cursor: null,
    });
    expect(store.filters()).toEqual({ status: 'Accepted' });
    expect(store.hasPrevious()).toBe(false);

    store.setSort({ key: 'number', direction: 'asc' });
    expectList('limit=2&sort=number:asc&status=Accepted').flush({ items: [], next_cursor: null });
    expect(store.empty()).toBe(true);
  });

  it('uses page numbers when the server gives the total', () => {
    store.load();
    expectList('limit=2&sort=issued_at:desc').flush({
      items: docs('a', 'b'),
      next_cursor: null,
      total: 5,
    });
    expect(store.pagination()).toEqual({ kind: 'pages', page: 1, pageSize: 2, total: 5 });

    store.changePage({ kind: 'pages', page: 3 });
    expectList('limit=2&page=3&sort=issued_at:desc').flush({
      items: docs('e'),
      next_cursor: null,
      total: 5,
    });
    expect(store.pagination()).toEqual({ kind: 'pages', page: 3, pageSize: 2, total: 5 });
  });

  it('keeps the error, cancels stale requests and refreshes the same page', () => {
    store.load();
    const stale = expectList('limit=2&sort=issued_at:desc');
    store.setFilters({ status: 'Rejected' });
    expect(stale.cancelled).toBe(true);

    expectList('limit=2&sort=issued_at:desc&status=Rejected').flush(null, {
      status: 503,
      statusText: 'Service Unavailable',
    });
    expect(store.loading()).toBe(false);
    expect(store.error()).toBeTruthy();
    expect(store.empty()).toBe(false);

    store.refresh();
    expect(store.error()).toBeNull();
    expectList('limit=2&sort=issued_at:desc&status=Rejected').flush({
      items: docs('r'),
      next_cursor: null,
    });
    expect(store.items()).toEqual(docs('r'));
  });
});
