import { inject } from '@angular/core';
import { Api, NotificationResponse, listNotifications } from '@impifact/api';
import { withServerList } from '@impifact/core';
import { signalStore } from '@ngrx/signals';
import { map } from 'rxjs';

export interface NotificationFilters {
  /** Solo los sin leer (`unread=true`); sin él, todos. */
  unread: boolean;
}

/**
 * Listado de avisos de la empresa activa (NgRx SignalStore con `withServerList`): el Api pagina por cursor, del
 * más reciente al más antiguo, y filtra los sin leer; el navegador nunca descarga la lista completa.
 */
export const NotificationListStore = signalStore(
  withServerList<NotificationResponse, NotificationFilters>({
    filters: { unread: false },
    pageSize: 20,
    loader: () => {
      const api = inject(Api);
      return (query) =>
        api
          .invoke(listNotifications, {
            ...(query.filters.unread ? { unread: true } : {}),
            ...(query.cursor ? { cursor: query.cursor } : {}),
            limit: query.pageSize,
          })
          .pipe(map((page) => ({ items: page.items ?? [], nextCursor: page.next_cursor ?? null })));
    },
  }),
);
