import { HttpContext } from '@angular/common/http';
import { inject } from '@angular/core';
import {
  Api,
  CompanyUser,
  CreateUserBody,
  CreatedUser,
  UpdateCompanyUserBody,
  createUser,
  listCompanyUsers,
  updateCompanyUser,
} from '@impifact/api';
import { SKIP_ERROR_TOAST } from '@impifact/core';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';

export type CompanyUsersStatus = 'idle' | 'loading' | 'ready' | 'error';

interface CompanyUsersState {
  items: CompanyUser[];
  status: CompanyUsersStatus;
  /** Id of the user whose row is being changed; its actions are disabled meanwhile. */
  busyId: string | null;
}

/** The page shows its own messages for these calls, so core does not open a toast. */
const QUIET = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Users of the active company (NgRx SignalStore): `listCompanyUsers` returns them all, already sorted, so there is
 * no paging. Provided by the page: every visit reads the list again. Changes go through `updateCompanyUser` and
 * replace the row with what the API returns; errors are thrown as `AppError` for the page to explain.
 */
export const CompanyUsersStore = signalStore(
  withState<CompanyUsersState>({ items: [], status: 'idle', busyId: null }),
  withProps(() => ({ _api: inject(Api) })),
  withMethods((store) => {
    // A late answer of an older read does not overwrite a newer one.
    let generation = 0;

    return {
      async load(nit: string): Promise<void> {
        const mine = ++generation;
        patchState(store, { status: 'loading' });
        try {
          const list = await firstValueFrom(store._api.invoke(listCompanyUsers, { nit }, QUIET()));
          if (mine === generation) {
            patchState(store, { items: list.items ?? [], status: 'ready' });
          }
        } catch {
          if (mine === generation) {
            patchState(store, { items: [], status: 'error' });
          }
        }
      },

      async update(nit: string, userId: string, body: UpdateCompanyUserBody): Promise<CompanyUser> {
        patchState(store, { busyId: userId });
        try {
          const user = await firstValueFrom(
            store._api.invoke(updateCompanyUser, { nit, userId, body }, QUIET()),
          );
          patchState(store, {
            items: store.items().map((item) => (item.id === user.id ? user : item)),
          });
          return user;
        } finally {
          patchState(store, { busyId: null });
        }
      },

      create(body: CreateUserBody): Promise<CreatedUser> {
        return firstValueFrom(store._api.invoke(createUser, { body }, QUIET()));
      },
    };
  }),
);
