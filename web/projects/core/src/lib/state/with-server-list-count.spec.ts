import { TestBed } from '@angular/core/testing';
import { signalStore } from '@ngrx/signals';
import { Subject } from 'rxjs';
import { ServerListPage, ServerListQuery, withServerList } from './with-server-list';

/*
 * Conteo informativo (`count`): el Api da el total de registros pero sigue paginando por cursor (por ejemplo la
 * lista de documentos con `include_total`). El store lo guarda en `totalCount` sin pasar a páginas numeradas.
 */

interface Doc {
  id: string;
}

const queries: ServerListQuery<{ status?: string }>[] = [];
let responses: Subject<ServerListPage<Doc>>;

const CountedStore = signalStore(
  withServerList<Doc, { status?: string }>({
    filters: {},
    loader: () => (query) => {
      queries.push(query);
      responses = new Subject<ServerListPage<Doc>>();
      return responses;
    },
  }),
);

describe('withServerList count', () => {
  let store: InstanceType<typeof CountedStore>;

  beforeEach(() => {
    queries.length = 0;
    TestBed.configureTestingModule({ providers: [CountedStore] });
    store = TestBed.inject(CountedStore);
  });

  function answer(page: ServerListPage<Doc>) {
    responses.next(page);
    responses.complete();
  }

  it('keeps a count from the first page without switching to numbered pages', () => {
    store.load();
    answer({ items: [{ id: 'a' }], nextCursor: 'c2', count: 41 });

    expect(store.totalCount()).toBe(41);
    expect(store.pagination()).toEqual({ kind: 'cursor', previousCursor: null, nextCursor: 'c2' });
  });

  it('keeps the count while moving with the cursor and drops it when the filters change', () => {
    store.load();
    answer({ items: [{ id: 'a' }], nextCursor: 'c2', count: 41 });

    store.nextPage();
    answer({ items: [{ id: 'b' }], nextCursor: null });
    expect(queries.at(-1)?.cursor).toBe('c2');
    expect(store.totalCount()).toBe(41);

    store.setFilters({ status: 'Accepted' });
    expect(store.totalCount()).toBeNull();
    answer({ items: [], nextCursor: null });
    expect(store.totalCount()).toBeNull();
  });
});
