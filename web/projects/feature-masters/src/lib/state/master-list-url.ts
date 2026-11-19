import { DestroyRef, Signal, computed, effect, inject, untracked } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { MasterFilters, MasterStatus, parseMasterStatus } from './master-list';

/** Pausa al escribir antes de buscar, para no consultar el Api en cada tecla. */
export const SEARCH_DEBOUNCE_MS = 300;

/**
 * Enlaza la búsqueda y el estado de un listado con la URL (`?q=…&estado=…`), así el enlace se puede compartir: la URL
 * es la fuente de verdad y cada cambio de ella pide al store la primera página con esos filtros. La búsqueda escrita
 * espera una pausa antes de cambiar la URL. Debe llamarse en el contexto de inyección de la página.
 */
export function bindFiltersToUrl(
  store: { setFilters(filters: MasterFilters): void },
  query: { q: Signal<string | undefined>; estado: Signal<string | undefined> },
) {
  const router = inject(Router);
  const route = inject(ActivatedRoute);
  const filters = computed<MasterFilters>(() => ({
    search: query.q() ?? '',
    status: parseMasterStatus(query.estado()),
  }));

  effect(() => {
    const current = filters();
    untracked(() => store.setFilters(current));
  });

  const navigate = (queryParams: Record<string, string | null>) =>
    router.navigate([], {
      relativeTo: route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });

  const typed = new Subject<string>();
  const subscription = typed
    .pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged())
    .subscribe((text) => {
      if (text.trim() !== filters().search) {
        void navigate({ q: text.trim() || null });
      }
    });
  inject(DestroyRef).onDestroy(() => subscription.unsubscribe());

  return {
    filters,
    /** Texto escrito en la búsqueda (cada tecla). */
    typeSearch: (text: string) => typed.next(text),
    changeStatus: (status: MasterStatus) =>
      void navigate({ estado: status === 'active' ? null : status }),
  };
}
