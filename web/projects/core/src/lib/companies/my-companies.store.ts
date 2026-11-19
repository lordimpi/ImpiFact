import { HttpContext } from '@angular/common/http';
import { computed, effect, inject, untracked } from '@angular/core';
import { Api, MyCompany, listMyCompanies } from '@impifact/api';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { SKIP_ERROR_TOAST } from '../http/api-request';
import { SessionStore } from '../session/session.store';

export type MyCompaniesStatus = 'idle' | 'loading' | 'ready' | 'error';

interface MyCompaniesState {
  items: MyCompany[];
  status: MyCompaniesStatus;
}

/**
 * Empresas del usuario con su razón social, NIT y DV (dígito de verificación), de `GET /api/v1/me/companies`
 * (NgRx SignalStore). Se cargan solas al iniciar sesión y se olvidan al cerrarla. Un error no abre aviso: quien
 * las muestra vuelve a los identificadores de la sesión.
 */
export const MyCompaniesStore = signalStore(
  { providedIn: 'root' },
  withState<MyCompaniesState>({ items: [], status: 'idle' }),
  withComputed((store, session = inject(SessionStore)) => ({
    /** Empresa activa de la sesión, si ya se conoce su nombre. */
    active: computed(() => {
      const id = session.companyId();
      return store.items().find((company) => company.id === id) ?? null;
    }),
  })),
  withMethods((store, api = inject(Api)) => {
    let pending: Promise<void> = Promise.resolve();
    // Cada carga tiene un número; una respuesta vieja (de otro usuario) no pisa la más nueva.
    let generation = 0;

    async function fetch(): Promise<void> {
      const mine = ++generation;
      patchState(store, { status: 'loading' });
      try {
        const page = await firstValueFrom(
          api.invoke(listMyCompanies, {}, new HttpContext().set(SKIP_ERROR_TOAST, true)),
        );
        if (mine === generation) {
          patchState(store, { items: page.items ?? [], status: 'ready' });
        }
      } catch {
        if (mine === generation) {
          patchState(store, { items: [], status: 'error' });
        }
      }
    }

    return {
      byId(id: string | null | undefined): MyCompany | null {
        return store.items().find((company) => company.id === id) ?? null;
      },
      /** Vuelve a pedir la lista (por ejemplo, después de cambiar la razón social). */
      reload(): Promise<void> {
        pending = fetch();
        return pending;
      },
      /** Espera la carga en curso, si hay una. */
      whenLoaded(): Promise<void> {
        return pending;
      },
      _reset(): void {
        generation++;
        pending = Promise.resolve();
        patchState(store, { items: [], status: 'idle' });
      },
    };
  }),
  withHooks((store, session = inject(SessionStore)) => ({
    onInit() {
      effect(() => {
        const userId = session.user()?.id ?? null;
        untracked(() => (userId ? void store.reload() : store._reset()));
      });
    },
  })),
);
