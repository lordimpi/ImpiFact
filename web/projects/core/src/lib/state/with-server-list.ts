import { computed } from '@angular/core';
import {
  patchState,
  signalStoreFeature,
  withComputed,
  withMethods,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, Observable, catchError, pipe, switchMap, tap } from 'rxjs';

/*
 * Feature reutilizable de NgRx SignalStore para listas paginadas en el servidor (sección 4 de la arquitectura):
 * cada cambio de página, orden o filtros es una consulta nueva al Api, que devuelve solo la página visible. El
 * store nunca ordena, filtra ni pagina en el navegador: muestra exactamente lo que llega.
 *
 * Los tipos de orden, filtros, paginación y cambio de página coinciden por forma con los de `ui-table` y
 * `ui-paginator` (`UiSort`, `UiFilters`, `UiPagination`, `UiPageChange`), así que se conectan directamente sin que
 * core importe ui.
 */

export interface ServerListSort {
  key: string;
  direction: 'asc' | 'desc';
}

/** Lo que el store pide al Api; el `loader` del feature lo traduce a los parámetros de su endpoint. */
export interface ServerListQuery<Filters> {
  /** Cursor opaco de la página pedida; `null` en la primera página o al paginar por número. */
  cursor: string | null;
  /** Número de página desde 1; solo cuenta cuando el Api da el total (paginación por número). */
  page: number;
  pageSize: number;
  sort: ServerListSort | null;
  filters: Filters;
}

/** Respuesta del Api ya convertida: los registros de la página y, según el endpoint, el cursor siguiente o el total. */
export interface ServerListPage<Item> {
  items: Item[];
  nextCursor?: string | null;
  total?: number | null;
  /**
   * Total informativo de registros cuando el Api lo da sin dejar de paginar por cursor (por ejemplo, la lista de
   * documentos con `include_total`): queda en `totalCount` y no cambia la paginación. Sin valor se conserva el que
   * ya había, porque el Api lo da solo en la primera página.
   */
  count?: number | null;
}

export type ServerListLoader<Item, Filters> = (
  query: ServerListQuery<Filters>,
) => Observable<ServerListPage<Item>>;

export type ServerListPagination =
  | { kind: 'cursor'; previousCursor: string | null; nextCursor: string | null }
  | { kind: 'pages'; page: number; pageSize: number; total: number };

export type ServerListPageChange =
  | { kind: 'cursor'; cursor: string; direction: 'previous' | 'next' }
  | { kind: 'pages'; page: number };

export interface ServerListConfig<Item, Filters> {
  /**
   * Fábrica del cargador. Corre en el contexto de inyección del store, así que puede usar `inject()` (por ejemplo,
   * `inject(Api)` y una función del cliente generado).
   */
  loader: () => ServerListLoader<Item, Filters>;
  filters: Filters;
  pageSize?: number;
  sort?: ServerListSort | null;
}

export interface ServerListState<Item, Filters> {
  items: Item[];
  /** Cursor de la página visible. */
  cursor: string | null;
  /** Cursores de las páginas anteriores ya visitadas, para volver atrás con paginación por cursor. */
  previousCursors: (string | null)[];
  nextCursor: string | null;
  page: number;
  pageSize: number;
  /** Total de registros si el Api lo da; `null` con paginación por cursor. */
  total: number | null;
  /** Total informativo (ver `ServerListPage.count`); se borra al volver a la primera página. */
  totalCount: number | null;
  sort: ServerListSort | null;
  filters: Filters;
  loading: boolean;
  /** Error de la última carga (un `AppError` si pasó por `errorInterceptor`). */
  error: unknown;
}

const DEFAULT_PAGE_SIZE = 20;

export function withServerList<Item, Filters extends object>(
  config: ServerListConfig<Item, Filters>,
) {
  const initial: ServerListState<Item, Filters> = {
    items: [],
    cursor: null,
    previousCursors: [],
    nextCursor: null,
    page: 1,
    pageSize: config.pageSize ?? DEFAULT_PAGE_SIZE,
    total: null,
    totalCount: null,
    sort: config.sort ?? null,
    filters: config.filters,
    loading: false,
    error: null,
  };

  return signalStoreFeature(
    withState<ServerListState<Item, Filters>>(initial),
    withComputed((store) => ({
      empty: computed(
        () => !store.loading() && store.error() === null && store.items().length === 0,
      ),
      hasNext: computed(() => {
        const total = store.total();
        return total === null
          ? store.nextCursor() !== null
          : store.page() * store.pageSize() < total;
      }),
      hasPrevious: computed(() =>
        store.total() === null ? store.previousCursors().length > 0 : store.page() > 1,
      ),
      pagination: computed<ServerListPagination>(() => {
        const total = store.total();
        if (total !== null) {
          return { kind: 'pages', page: store.page(), pageSize: store.pageSize(), total };
        }
        const previous = store.previousCursors();
        return {
          kind: 'cursor',
          // La primera página no tiene cursor; se representa con '' para que «anterior» siga habilitado.
          previousCursor: previous.length > 0 ? (previous.at(-1) ?? '') : null,
          nextCursor: store.nextCursor(),
        };
      }),
    })),
    withMethods((store) => {
      const loader = config.loader();

      // switchMap: una consulta nueva cancela la anterior, así una respuesta vieja nunca pisa la vigente.
      const run = rxMethod<ServerListQuery<Filters>>(
        pipe(
          tap(() => patchState(store, { loading: true, error: null })),
          switchMap((query) =>
            loader(query).pipe(
              tap((page) =>
                patchState(store, {
                  items: page.items,
                  nextCursor: page.nextCursor ?? null,
                  total: page.total ?? null,
                  totalCount: page.count !== undefined ? page.count : store.totalCount(),
                  loading: false,
                }),
              ),
              catchError((error: unknown) => {
                patchState(store, { loading: false, error });
                return EMPTY;
              }),
            ),
          ),
        ),
      );

      function load(): void {
        run({
          cursor: store.cursor(),
          page: store.page(),
          pageSize: store.pageSize(),
          sort: store.sort(),
          filters: store.filters(),
        });
      }

      function firstPage(): Partial<ServerListState<Item, Filters>> {
        return { cursor: null, previousCursors: [], page: 1, totalCount: null };
      }

      function nextPage(): void {
        if (store.total() !== null) {
          goToPage(store.page() + 1);
          return;
        }
        const next = store.nextCursor();
        if (next === null) {
          return;
        }
        patchState(store, {
          previousCursors: [...store.previousCursors(), store.cursor()],
          cursor: next,
          page: store.page() + 1,
        });
        load();
      }

      function previousPage(): void {
        if (store.total() !== null) {
          goToPage(store.page() - 1);
          return;
        }
        const previous = store.previousCursors();
        if (previous.length === 0) {
          return;
        }
        patchState(store, {
          cursor: previous.at(-1) ?? null,
          previousCursors: previous.slice(0, -1),
          page: store.page() - 1,
        });
        load();
      }

      function goToPage(page: number): void {
        patchState(store, { cursor: null, previousCursors: [], page: Math.max(1, page) });
        load();
      }

      return {
        /** Carga la página vigente. */
        load,
        /** Vuelve a pedir la página vigente (por ejemplo, tras crear o borrar un registro). */
        refresh: load,
        nextPage,
        previousPage,
        goToPage,
        /** Recibe el `pageChange` de `ui-paginator` o `ui-table`. */
        changePage(change: ServerListPageChange): void {
          if (change.kind === 'pages') {
            goToPage(change.page);
          } else if (change.direction === 'next') {
            nextPage();
          } else {
            previousPage();
          }
        },
        /** Cambia el orden y vuelve a la primera página. */
        setSort(sort: ServerListSort | null): void {
          patchState(store, { sort, ...firstPage() });
          load();
        },
        /** Cambia los filtros y vuelve a la primera página. */
        setFilters(filters: Filters): void {
          patchState(store, { filters, ...firstPage() });
          load();
        },
        setPageSize(pageSize: number): void {
          patchState(store, { pageSize, ...firstPage() });
          load();
        },
      };
    }),
  );
}
