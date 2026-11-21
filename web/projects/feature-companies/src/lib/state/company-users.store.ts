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
  listPlatformCompanies,
  updateCompanyUser,
} from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST } from '@impifact/core';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { Observable, firstValueFrom, map } from 'rxjs';

export type CompanyUsersStatus = 'idle' | 'loading' | 'ready' | 'error';

interface CompanyUsersState {
  items: CompanyUser[];
  status: CompanyUsersStatus;
  /** Id of the user whose row is being changed; its actions are disabled meanwhile. */
  busyId: string | null;
  /** The last read answered 404: the company does not exist (only the platform admin can ask for any NIT). */
  missing: boolean;
}

/** The company whose users the page shows: the active one of the owner, or the one of the URL for the platform. */
export interface UsersCompany {
  id: string;
  nit: string;
  name: string;
}

/** Longest page of `listPlatformCompanies`: the search by a whole NIT only matches more than once by chance. */
const LOOKUP_LIMIT = 100;

/** The page shows its own messages for these calls, so core does not open a toast. */
const QUIET = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Users of one company (NgRx SignalStore): `listCompanyUsers` returns them all, already sorted, so there is
 * no paging. Provided by the page: every visit reads the list again. For the platform admin it also finds the
 * company of the URL among the platform companies (`findCompany`). Changes go through `updateCompanyUser` and
 * replace the row with what the API returns; errors are thrown as `AppError` for the page to explain.
 */
export const CompanyUsersStore = signalStore(
  withState<CompanyUsersState>({ items: [], status: 'idle', busyId: null, missing: false }),
  withProps(() => ({ _api: inject(Api) })),
  withMethods((store) => {
    // A late answer of an older read does not overwrite a newer one.
    let generation = 0;

    return {
      async load(nit: string): Promise<void> {
        const mine = ++generation;
        patchState(store, { status: 'loading', missing: false });
        try {
          const list = await firstValueFrom(store._api.invoke(listCompanyUsers, { nit }, QUIET()));
          if (mine === generation) {
            patchState(store, { items: list.items ?? [], status: 'ready' });
          }
        } catch (error) {
          if (mine === generation) {
            const missing = error instanceof AppError && error.status === 404;
            patchState(store, { items: [], status: 'error', missing });
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

      /** The platform company with exactly this NIT (the search also matches parts of it), or `null`. */
      findCompany(nit: string): Observable<UsersCompany | null> {
        return store._api
          .invoke(listPlatformCompanies, { search: nit, limit: LOOKUP_LIMIT }, QUIET())
          .pipe(
            map((list) => {
              const found = (list.items ?? []).find((item) => item.nit === nit);
              return found ? { id: found.id ?? '', nit, name: found.name ?? nit } : null;
            }),
          );
      },
    };
  }),
);
