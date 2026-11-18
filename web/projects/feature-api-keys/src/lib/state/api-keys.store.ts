import { HttpContext } from '@angular/common/http';
import { inject } from '@angular/core';
import {
  Api,
  ApiKeyItem,
  CreateApiKeyBody,
  CreatedApiKey,
  createApiKey,
  listApiKeys,
  revokeApiKey,
} from '@impifact/api';
import { SKIP_ERROR_TOAST, withServerList } from '@impifact/core';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { firstValueFrom, map } from 'rxjs';

/** El listado no tiene filtros: solo pagina por cursor. */
type ApiKeyFilters = Record<string, never>;

/**
 * Llaves de API de la empresa activa (NgRx SignalStore con `withServerList`): el Api las pagina por cursor
 * (`listApiKeys`) y nunca devuelve la llave completa ni su hash. Crear una llave devuelve la respuesta completa (con
 * `key`) solo a la página, que la muestra una vez; el store nunca la guarda (sección 6 de la arquitectura del
 * frontend). Se provee en la página: cada visita, empresa o sesión empieza con una lectura nueva.
 */
export const ApiKeysStore = signalStore(
  withServerList<ApiKeyItem, ApiKeyFilters>({
    filters: {},
    pageSize: 20,
    loader: () => {
      const api = inject(Api);
      return (query) =>
        api
          .invoke(listApiKeys, {
            ...(query.cursor ? { cursor: query.cursor } : {}),
            limit: query.pageSize,
          })
          .pipe(map((page) => ({ items: page.items ?? [], nextCursor: page.next_cursor ?? null })));
    },
  }),
  withState<{ busyId: string | null }>({ busyId: null }),
  withProps(() => ({ _api: inject(Api) })),
  withMethods((store) => ({
    /**
     * Crea la llave y devuelve la respuesta completa (con `key`) para mostrarla una sola vez; luego vuelve a la
     * primera página, donde aparece la nueva. Los errores se lanzan sin aviso automático: la página los muestra en
     * el formulario.
     */
    async create(body: CreateApiKeyBody): Promise<CreatedApiKey> {
      const created = await firstValueFrom(
        store._api.invoke(createApiKey, { body }, new HttpContext().set(SKIP_ERROR_TOAST, true)),
      );
      store.goToPage(1);
      return created;
    },
    /** Revoca la llave y la marca revocada en la página visible; devuelve si salió bien (el error lo avisa core). */
    async revoke(id: string): Promise<boolean> {
      patchState(store, { busyId: id });
      try {
        await firstValueFrom(store._api.invoke(revokeApiKey, { id }), { defaultValue: null });
        patchState(store, {
          items: store
            .items()
            .map((key) =>
              key.id === id
                ? { ...key, is_revoked: true, revoked_at: new Date().toISOString() }
                : key,
            ),
        });
        return true;
      } catch {
        return false;
      } finally {
        patchState(store, { busyId: null });
      }
    },
  })),
);
