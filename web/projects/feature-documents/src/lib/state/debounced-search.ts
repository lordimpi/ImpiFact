import { Signal, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  Observable,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  startWith,
  switchMap,
} from 'rxjs';

/** Pausa tras la última tecla antes de buscar en el Api. */
export const SEARCH_DEBOUNCE_MS = 250;
/** Letras mínimas para buscar. */
export const SEARCH_MIN_LENGTH = 2;

export interface SearchState<T> {
  term: string;
  items: T[];
  loading: boolean;
  error: boolean;
}

/**
 * Estado de una búsqueda en el Api con *debounce* (pausa tras la última tecla): menos de dos letras no consulta y
 * una respuesta vieja no pisa la nueva. Debe llamarse en un contexto de inyección.
 */
export function debouncedSearch<T>(
  term: Signal<string>,
  fetch: (term: string) => Observable<T[]>,
): Signal<SearchState<T>> {
  const state = signal<SearchState<T>>({ term: '', items: [], loading: false, error: false });
  toObservable(term)
    .pipe(
      debounceTime(SEARCH_DEBOUNCE_MS),
      map((value) => value.trim()),
      distinctUntilChanged(),
      switchMap((value): Observable<SearchState<T>> => {
        if (value.length < SEARCH_MIN_LENGTH) {
          return of({ term: value, items: [], loading: false, error: false });
        }
        return fetch(value).pipe(
          map((items) => ({ term: value, items, loading: false, error: false })),
          catchError(() => of({ term: value, items: [], loading: false, error: true })),
          startWith({ term: value, items: [], loading: true, error: false }),
        );
      }),
      takeUntilDestroyed(),
    )
    .subscribe((value) => state.set(value));
  return state.asReadonly();
}
