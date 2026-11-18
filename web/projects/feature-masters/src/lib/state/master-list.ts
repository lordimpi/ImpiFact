import { HttpClient, HttpContext } from '@angular/common/http';
import { inject } from '@angular/core';
import { Api, StrictHttpResponse } from '@impifact/api';
import { ServerListLoader, ServerListSort } from '@impifact/core';
import { Observable, map } from 'rxjs';

/*
 * Piezas comunes de los listados de clientes y productos. Toda lista pagina, ordena y busca en el servidor (C5): el
 * cargador solo traduce la consulta del store a los parámetros del endpoint y devuelve la página tal como llega.
 */

/** Estado que filtra el listado (`status` del Api). */
export type MasterStatus = 'active' | 'inactive' | 'all';

export const MASTER_STATUSES: readonly MasterStatus[] = ['active', 'inactive', 'all'];

export interface MasterFilters {
  /** Texto de búsqueda; vacío para no filtrar. */
  search: string;
  status: MasterStatus;
}

export const DEFAULT_MASTER_FILTERS: MasterFilters = { search: '', status: 'active' };

/** Lee el estado de la URL; cualquier otro valor vuelve al de por defecto. */
export function parseMasterStatus(value: string | null | undefined): MasterStatus {
  return MASTER_STATUSES.find((status) => status === value) ?? 'active';
}

/** Parámetros comunes de `listCustomers` y `listProducts`. */
export interface MasterListParams {
  search?: string;
  status?: string;
  sort?: string;
  cursor?: string;
  limit?: number | string;
}

export interface MasterListResponse<Item> {
  items?: Item[];
  next_cursor?: string | null;
}

/** Forma de `listCustomers` y `listProducts` en el cliente generado. */
export type MasterListFn<Item> = (
  http: HttpClient,
  rootUrl: string,
  params?: MasterListParams,
  context?: HttpContext,
) => Observable<StrictHttpResponse<MasterListResponse<Item>>>;

/** Orden del Api: el nombre de la columna, con `-` delante para el orden descendente. */
export function sortParam(sort: ServerListSort | null): string | undefined {
  if (!sort) {
    return undefined;
  }
  return sort.direction === 'desc' ? `-${sort.key}` : sort.key;
}

/**
 * Fábrica del cargador de un listado de maestros para `withServerList`. Corre en el contexto de inyección del store.
 */
export function masterListLoader<Item>(
  list: MasterListFn<Item>,
): () => ServerListLoader<Item, MasterFilters> {
  return () => {
    const api = inject(Api);
    return (query) => {
      const search = query.filters.search.trim();
      return api
        .invoke(list, {
          ...(search ? { search } : {}),
          status: query.filters.status,
          ...(query.sort ? { sort: sortParam(query.sort) } : {}),
          ...(query.cursor ? { cursor: query.cursor } : {}),
          limit: query.pageSize,
        })
        .pipe(map((page) => ({ items: page.items ?? [], nextCursor: page.next_cursor ?? null })));
    };
  };
}
