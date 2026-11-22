import { HttpContext } from '@angular/common/http';
import { inject } from '@angular/core';
import { Api, PlatformCompanyItem, listPlatformCompanies } from '@impifact/api';
import { SKIP_ERROR_TOAST } from '@impifact/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, debounceTime, map, pipe, switchMap, tap } from 'rxjs';

/** Pausa tras la última tecla antes de buscar en el servidor. */
export const COMPANY_SEARCH_DEBOUNCE_MS = 300;

const PAGE_SIZE = 20;

interface PlatformCompaniesState {
  items: PlatformCompanyItem[];
  /** Texto buscado (sin espacios a los lados). */
  term: string;
  nextCursor: string | null;
  status: 'loading' | 'ready' | 'error';
  /** Se está trayendo la página siguiente («Cargar más»). */
  loadingMore: boolean;
}

interface PageRequest {
  term: string;
  cursor: string | null;
}

/**
 * Empresas de la plataforma (NgRx SignalStore) para el administrador: el Api busca por nombre o NIT (`search`) y
 * pagina por cursor (`listPlatformCompanies`); «Cargar más» suma la página siguiente a la lista. El navegador nunca
 * descarga la lista completa ni filtra. Se provee en la página.
 */
export const PlatformCompaniesStore = signalStore(
  withState<PlatformCompaniesState>({
    items: [],
    term: '',
    nextCursor: null,
    status: 'loading',
    loadingMore: false,
  }),
  withMethods((store, api = inject(Api)) => {
    // switchMap: una consulta nueva cancela la anterior, así una respuesta vieja nunca pisa la vigente.
    const fetch = rxMethod<PageRequest>(
      pipe(
        tap(({ term, cursor }) =>
          patchState(
            store,
            cursor
              ? { loadingMore: true }
              : { term, items: [], nextCursor: null, status: 'loading' },
          ),
        ),
        switchMap(({ term, cursor }) =>
          api
            .invoke(
              listPlatformCompanies,
              {
                ...(term ? { search: term } : {}),
                ...(cursor ? { cursor } : {}),
                limit: PAGE_SIZE,
              },
              new HttpContext().set(SKIP_ERROR_TOAST, true),
            )
            .pipe(
              tap((page) =>
                patchState(store, {
                  items: cursor ? [...store.items(), ...(page.items ?? [])] : (page.items ?? []),
                  nextCursor: page.next_cursor ?? null,
                  status: 'ready',
                  loadingMore: false,
                }),
              ),
              catchError(() => {
                patchState(store, { status: 'error', loadingMore: false });
                return EMPTY;
              }),
            ),
        ),
      ),
    );

    const typed = rxMethod<string>(
      pipe(
        debounceTime(COMPANY_SEARCH_DEBOUNCE_MS),
        map((term) => term.trim()),
        tap((term) => fetch({ term, cursor: null })),
      ),
    );

    return {
      /** Primera página sin búsqueda. */
      load(): void {
        fetch({ term: '', cursor: null });
      },
      /** Busca en el servidor después de una pausa al escribir. */
      search(term: string): void {
        typed(term);
      },
      loadMore(): void {
        const cursor = store.nextCursor();
        if (cursor && !store.loadingMore()) {
          fetch({ term: store.term(), cursor });
        }
      },
      /** Vuelve a pedir la primera página de la búsqueda actual. */
      retry(): void {
        fetch({ term: store.term(), cursor: null });
      },
      /** Muestra en la fila el plan recién asignado, sin volver a pedir la lista. */
      setPlan(companyId: string, planId: number, planName: string): void {
        patchState(store, {
          items: store
            .items()
            .map((company) =>
              company.id === companyId
                ? { ...company, plan_id: planId, plan_name: planName }
                : company,
            ),
        });
      },
      /** Muestra en la fila el estado que devolvió el Api al desactivar o reactivar la empresa. */
      setActive(companyId: string, isActive: boolean): void {
        patchState(store, {
          items: store
            .items()
            .map((company) =>
              company.id === companyId ? { ...company, is_active: isActive } : company,
            ),
        });
      },
    };
  }),
);
