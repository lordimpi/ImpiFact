import { DestroyRef, Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Observable,
  Subject,
  debounceTime,
  from,
  map,
  merge,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  CatalogItem,
  CatalogOption,
  CatalogsStore,
  PagedCatalogName,
  catalogLabel,
} from './catalogs.store';

/** Pausa tras la última tecla antes de buscar en el servidor. */
export const CATALOG_SEARCH_DEBOUNCE_MS = 300;

export interface CatalogSearchState {
  /** Texto buscado (sin espacios a los lados). */
  term: string;
  items: readonly CatalogItem[];
  nextCursor: string | null;
  /** `idle`: todavía no se busca porque falta el filtro (por ejemplo, el departamento). */
  status: 'idle' | 'loading' | 'ready' | 'error';
}

/** Filtro de la búsqueda: el departamento de los municipios. */
export interface CatalogSearchFilter {
  departmentId?: number | null;
}

export interface CatalogSearchOptions {
  /** Valor actual del campo (el id como texto): siempre queda entre las opciones, con su nombre si se conoce. */
  current: () => string;
  /** Nombre de un id que todavía no se conoce («Otro (id N)»). */
  otherName: (id: number) => string;
  /** Texto de cada fila; por defecto `catalogLabel`. Puede leer signals (por ejemplo, los departamentos). */
  format?: (item: CatalogItem) => string;
  /**
   * Filtro del servidor; puede leer signals. `null` deja la lista vacía y sin pedir nada hasta que haya filtro. Al
   * cambiar, la búsqueda empieza de nuevo sin texto.
   */
  filter?: () => CatalogSearchFilter | null;
}

export interface CatalogSearch {
  state: Signal<CatalogSearchState>;
  /** Opciones del `<select>`: los resultados y, al final, el valor actual si no está entre ellos. */
  options: Signal<readonly CatalogOption[]>;
  /** Si el servidor tiene más filas para la búsqueda actual (la lista las pide al llegar al final). */
  hasMore: Signal<boolean>;
  search(term: string): void;
  loadMore(): void;
  retry(): void;
}

interface SearchRequest {
  term: string;
  cursor: string | null;
  append: boolean;
}

/**
 * Búsqueda en un catálogo largo (municipios o unidades de medida) para un combobox (`ui-combobox`): muestra la
 * primera página al abrir, busca en el servidor con `q` tras una pausa, suma páginas con el cursor y puede filtrar
 * en el servidor (los municipios de un departamento). El valor guardado no se pierde
 * aunque la búsqueda no lo traiga: su nombre se resuelve una vez (`CatalogsStore.find`). Debe llamarse en un
 * contexto de inyección (inicializador de un campo del componente).
 */
export function catalogSearch(
  name: PagedCatalogName,
  options: CatalogSearchOptions,
): CatalogSearch {
  const store = inject(CatalogsStore);
  const destroyRef = inject(DestroyRef);
  const format = options.format ?? ((item: CatalogItem) => catalogLabel(name, item));

  const state = signal<CatalogSearchState>({
    term: '',
    items: [],
    nextCursor: null,
    status: 'loading',
  });
  /** Filas vistas en cualquier página o resueltas por id: el valor actual conserva su nombre. */
  const known = signal<ReadonlyMap<number, CatalogItem>>(new Map());
  const remember = (items: readonly CatalogItem[]) => {
    if (items.length === 0) return;
    known.update((current) => {
      const next = new Map(current);
      for (const item of items) next.set(item.id, item);
      return next;
    });
  };

  const typed = new Subject<string>();
  const direct = new Subject<SearchRequest>();

  const filterOf = () => (options.filter ? untracked(options.filter) : {});

  const fetch = (request: SearchRequest): Observable<CatalogSearchState> => {
    const before = untracked(state);
    const filter = filterOf();
    if (filter === null) {
      return of<CatalogSearchState>({ term: '', items: [], nextCursor: null, status: 'idle' });
    }
    const query = { q: request.term, cursor: request.cursor, departmentId: filter.departmentId };
    return from(store.page(name, query)).pipe(
      map((page): CatalogSearchState => ({
        term: request.term,
        items: request.append ? [...before.items, ...page.items] : page.items,
        nextCursor: page.nextCursor,
        status: 'ready',
      })),
      catchError(() => of<CatalogSearchState>({ ...before, term: request.term, status: 'error' })),
      startWith<CatalogSearchState>({
        ...before,
        term: request.term,
        items: request.append ? before.items : [],
        status: 'loading',
      }),
    );
  };

  merge(
    typed.pipe(
      debounceTime(CATALOG_SEARCH_DEBOUNCE_MS),
      map((term): SearchRequest => ({ term: term.trim(), cursor: null, append: false })),
    ),
    direct,
  )
    .pipe(
      startWith<SearchRequest>({ term: '', cursor: null, append: false }),
      switchMap(fetch),
      takeUntilDestroyed(destroyRef),
    )
    .subscribe((next) => {
      state.set(next);
      remember(next.items);
    });

  // Un filtro nuevo (otro departamento) empieza la búsqueda de cero; el filtro inicial ya lo usó `startWith`.
  if (options.filter) {
    const filter = options.filter;
    let previous = JSON.stringify(untracked(filter));
    effect(() => {
      const key = JSON.stringify(filter());
      if (key !== previous) {
        previous = key;
        untracked(() => direct.next({ term: '', cursor: null, append: false }));
      }
    });
  }

  // Si el valor actual no está entre las filas conocidas, se busca su nombre una sola vez.
  const resolving = new Set<number>();
  effect(() => {
    const id = Number(options.current());
    if (options.current() === '' || !Number.isInteger(id) || known().has(id) || resolving.has(id)) {
      return;
    }
    resolving.add(id);
    void store
      .find(name, id)
      .then((item) => item && remember([item]))
      .catch(() => undefined);
  });

  const optionsSignal = computed<readonly CatalogOption[]>(() => {
    const results = state().items.map((item) => ({ id: item.id, name: format(item) }));
    const current = options.current();
    const id = Number(current);
    if (current === '' || !Number.isInteger(id) || results.some((option) => option.id === id)) {
      return results;
    }
    const item = known().get(id);
    return [...results, { id, name: item ? format(item) : options.otherName(id) }];
  });

  return {
    state: state.asReadonly(),
    options: optionsSignal,
    hasMore: computed(() => state().status === 'ready' && state().nextCursor !== null),
    search: (term) => typed.next(term),
    loadMore: () => {
      const { term, nextCursor } = untracked(state);
      if (nextCursor) direct.next({ term, cursor: nextCursor, append: true });
    },
    retry: () => {
      const { term, nextCursor, items } = untracked(state);
      // Si falló «Cargar más», se repite esa página; si no, la búsqueda entera.
      const append = items.length > 0 && nextCursor !== null;
      direct.next({ term, cursor: append ? nextCursor : null, append });
    },
  };
}
