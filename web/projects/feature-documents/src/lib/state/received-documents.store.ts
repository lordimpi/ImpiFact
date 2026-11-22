import { inject } from '@angular/core';
import {
  Api,
  ListReceivedDocuments$Params,
  ReceivedDocumentResponse,
  listReceivedDocuments,
} from '@impifact/api';
import { ServerListQuery, withServerList } from '@impifact/core';
import { signalStore } from '@ngrx/signals';
import { map } from 'rxjs';
import { ReceivedFilters } from './received-filters';

/*
 * Store of the received documents list (`withServerList` of core). The API pages by `limit` and `offset` and gives
 * `total_count`, so the table shows page numbers; the browser never filters nor sorts (the API orders by issue date,
 * newest first). Provided in the page, so each visit starts clean.
 */

const PAGE_SIZE = 20;

/** Translates the store query into the parameters of `listReceivedDocuments`. */
export function receivedListParams(
  query: ServerListQuery<ReceivedFilters>,
): ListReceivedDocuments$Params {
  const { filters } = query;
  const params: ListReceivedDocuments$Params = {
    limit: query.pageSize,
    offset: (query.page - 1) * query.pageSize,
  };
  if (filters.supplierNit) params.supplier_nit = filters.supplierNit;
  if (filters.from) params.from = filters.from;
  if (filters.to) params.to = filters.to;
  return params;
}

export const ReceivedDocumentsStore = signalStore(
  withServerList<ReceivedDocumentResponse, ReceivedFilters>({
    filters: {},
    pageSize: PAGE_SIZE,
    loader: () => {
      const api = inject(Api);
      return (query) =>
        api
          .invoke(listReceivedDocuments, receivedListParams(query))
          .pipe(map((page) => ({ items: page.data, total: Number(page.total_count) })));
    },
  }),
);
