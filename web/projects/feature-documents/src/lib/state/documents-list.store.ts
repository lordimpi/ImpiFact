import { inject } from '@angular/core';
import { Api, DocumentSummaryResponse, ListDocuments$Params, listDocuments } from '@impifact/api';
import { ServerListQuery, withServerList } from '@impifact/core';
import { signalStore } from '@ngrx/signals';
import { map } from 'rxjs';
import { DocumentFilters } from './list-filters';

/*
 * Store de la lista de documentos (NgRx SignalStore con `withServerList` de core): cada página, filtro o cambio de
 * cursor es una consulta a `listDocuments`; el navegador nunca filtra ni ordena (el Api ordena por `issued_at, id`,
 * del más reciente al más antiguo). Se provee en la página, así cada visita empieza limpia.
 */

const PAGE_SIZE = 20;

/** Traduce la consulta del store a los parámetros de `listDocuments`. */
export function listParams(query: ServerListQuery<DocumentFilters>): ListDocuments$Params {
  const { filters } = query;
  const params: ListDocuments$Params = { limit: query.pageSize };
  if (query.cursor) params.cursor = query.cursor;
  if (filters.from) params.from = filters.from;
  if (filters.to) params.to = filters.to;
  if (filters.kind) params.kind = filters.kind;
  if (filters.status?.length) params.status = filters.status;
  if (filters.customer) params.customer = filters.customer;
  if (filters.number) params.number = filters.number;
  // El total cuesta una consulta más: solo si la persona lo pidió y solo en la primera página.
  if (filters.withTotal && !query.cursor) params.include_total = true;
  return params;
}

export const DocumentsListStore = signalStore(
  withServerList<DocumentSummaryResponse, DocumentFilters>({
    filters: {},
    pageSize: PAGE_SIZE,
    loader: () => {
      const api = inject(Api);
      return (query) =>
        api.invoke(listDocuments, listParams(query)).pipe(
          map((page) => ({
            items: page.items ?? [],
            nextCursor: page.next_cursor ?? null,
            ...(page.total_count == null ? {} : { count: Number(page.total_count) }),
          })),
        );
    },
  }),
);
