import { HttpContext } from '@angular/common/http';
import { effect, inject, untracked } from '@angular/core';
import { Api, CreateApiKeyBody, CreatedApiKey, createApiKey, revokeApiKey } from '@impifact/api';
import { SKIP_ERROR_TOAST, SessionStore } from '@impifact/core';
import {
  patchState,
  signalStore,
  withHooks,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';

/** Llave creada en esta sesión, **sin** su texto completo: ese solo lo recibe la página para mostrarlo una vez. */
export interface SessionApiKey {
  id: string;
  name: string;
  keyPrefix: string;
  scopes: string[];
  expiresAt: string | null;
  revoked: boolean;
}

interface ApiKeysState {
  keys: SessionApiKey[];
  /** Llave que se está revocando. */
  busyId: string | null;
}

/**
 * Llaves de API creadas en esta sesión del panel (NgRx SignalStore). El Api no tiene un endpoint para listar las
 * llaves, así que el store recuerda las que se crean aquí hasta recargar la página; se vacía al cambiar de empresa
 * o cerrar la sesión. Nunca guarda el texto completo de la llave (sección 6 de la arquitectura del frontend).
 */
export const ApiKeysStore = signalStore(
  { providedIn: 'root' },
  withState<ApiKeysState>({ keys: [], busyId: null }),
  withProps(() => ({ _api: inject(Api), _session: inject(SessionStore) })),
  withMethods((store) => ({
    /**
     * Crea la llave y devuelve la respuesta completa (con `key`) para mostrarla una sola vez. Los errores se lanzan
     * sin aviso automático: la página los muestra en el formulario.
     */
    async create(body: CreateApiKeyBody): Promise<CreatedApiKey> {
      const created = await firstValueFrom(
        store._api.invoke(createApiKey, { body }, new HttpContext().set(SKIP_ERROR_TOAST, true)),
      );
      const summary: SessionApiKey = {
        id: created.id,
        name: created.name,
        keyPrefix: created.key_prefix,
        scopes: created.scopes,
        expiresAt: created.expires_at,
        revoked: false,
      };
      patchState(store, { keys: [summary, ...store.keys()] });
      return created;
    },
    /** Revoca la llave; devuelve si salió bien (el error lo avisa core). */
    async revoke(id: string): Promise<boolean> {
      patchState(store, { busyId: id });
      try {
        await firstValueFrom(store._api.invoke(revokeApiKey, { id }), { defaultValue: null });
        patchState(store, {
          keys: store.keys().map((key) => (key.id === id ? { ...key, revoked: true } : key)),
        });
        return true;
      } catch {
        return false;
      } finally {
        patchState(store, { busyId: null });
      }
    },
  })),
  withHooks({
    onInit(store) {
      // Las llaves son de la empresa activa: otra empresa u otra sesión empiezan sin ninguna.
      effect(() => {
        store._session.companyId();
        store._session.isAuthenticated();
        untracked(() => patchState(store, { keys: [], busyId: null }));
      });
    },
  }),
);
