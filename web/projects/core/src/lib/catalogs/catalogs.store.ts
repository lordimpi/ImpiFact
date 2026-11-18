import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Signal, computed, inject } from '@angular/core';
import {
  ApiConfiguration,
  CatalogItemResponse,
  CatalogListResponse,
  listCatalog,
} from '@impifact/api';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { SKIP_ERROR_TOAST } from '../http/api-request';

/*
 * Catálogos de la DIAN (Dirección de Impuestos y Aduanas Nacionales) que publica `GET /api/v1/catalogs/{nombre}`:
 * tipos de identificación, municipios, unidades de medida, impuestos y demás listas de códigos de los formularios.
 * Los catálogos cortos se piden enteros una vez por sesión; los dos largos (municipios y unidades) se buscan en el
 * servidor por páginas. Este archivo solo lo importan los features perezosos, así que no pesa en el bundle inicial.
 */

/** Los 16 catálogos que publica el Api. */
export const CATALOG_NAMES = [
  'identification-types',
  'organization-types',
  'regimes',
  'liabilities',
  'municipalities',
  'departments',
  'countries',
  'unit-measures',
  'taxes',
  'payment-forms',
  'payment-methods',
  'document-types',
  'currencies',
  'item-identification-types',
  'credit-note-discrepancy-responses',
  'debit-note-discrepancy-responses',
] as const;

export type CatalogName = (typeof CATALOG_NAMES)[number];

/** Catálogos largos: el Api los pagina por cursor y se buscan con `q`, nunca se descargan enteros. */
export type PagedCatalogName = 'municipalities' | 'unit-measures';

/** Catálogos cortos: el Api los devuelve completos en una sola respuesta. */
export type SmallCatalogName = Exclude<CatalogName, PagedCatalogName>;

/** Filas por página de un catálogo largo (el Api admite hasta 200). */
export const CATALOG_PAGE_SIZE = 100;

/** Fila de un catálogo, con el `id` que esperan los campos `*_id` del Api. */
export interface CatalogItem {
  id: number;
  /** Código DIAN. */
  code: string;
  name: string;
  /** Descripción (solo los impuestos). */
  description?: string | null;
  /** Departamento (solo los municipios). */
  departmentId?: number | null;
}

/** Página de un catálogo largo. */
export interface CatalogPage {
  items: readonly CatalogItem[];
  /** Cursor de la página siguiente; `null` en la última. */
  nextCursor: string | null;
}

/** Opción de un `<select>`: el id del catálogo y su nombre visible. */
export interface CatalogOption {
  id: number;
  name: string;
}

export type CatalogStatus = 'idle' | 'loading' | 'ready' | 'error';

interface CatalogEntry {
  status: CatalogStatus;
  items: readonly CatalogItem[];
}

interface CatalogsState {
  entries: Partial<Record<SmallCatalogName, CatalogEntry>>;
}

/** Copia guardada en `sessionStorage`: los catálogos son públicos, no llevan datos de la persona. */
interface SavedCatalog {
  version: string;
  items: CatalogItem[];
}

const STORAGE_PREFIX = 'impifact.catalog.';
const NOT_MODIFIED = 304;
const EMPTY: readonly CatalogItem[] = [];

/** Texto con que se muestra una fila en una lista, como la nombran los formularios de la DIAN. */
export function catalogLabel(name: CatalogName, item: Omit<CatalogItem, 'id'>): string {
  switch (name) {
    case 'liabilities':
      return `${item.code} ${item.name}`;
    case 'unit-measures':
      return `${item.name} (${item.code})`;
    case 'taxes':
      return item.description ? `${item.name} (${item.description})` : item.name;
    default:
      return item.name;
  }
}

/** «Medellín (Antioquia)»: el municipio con su departamento, si ya se conoce. */
export function municipalityLabel(item: CatalogItem, departments: readonly CatalogItem[]): string {
  const department = departments.find((row) => row.id === item.departmentId);
  return department ? `${item.name} (${department.name})` : item.name;
}

/** Opciones de un catálogo con su nombre visible (`catalogLabel`). */
export function catalogOptions(name: CatalogName, items: readonly CatalogItem[]): CatalogOption[] {
  return items.map((item) => ({ id: item.id, name: catalogLabel(name, item) }));
}

/**
 * Opciones de un `<select>` con el valor guardado aunque no esté en la lista: así editar un registro no borra un id
 * que llegó de otra parte (por ejemplo, de la emisión por el Api) o que todavía no se cargó.
 */
export function optionsWith(
  options: readonly CatalogOption[],
  current: string,
  otherName: (id: number) => string,
): readonly CatalogOption[] {
  const id = Number(current);
  if (current === '' || !Number.isInteger(id) || options.some((option) => option.id === id)) {
    return options;
  }
  return [...options, { id, name: otherName(id) }];
}

function statusOf(error: unknown): number | null {
  return typeof error === 'object' && error !== null && 'status' in error
    ? Number((error as { status: unknown }).status)
    : null;
}

function toItem(row: CatalogItemResponse): CatalogItem {
  const departmentId =
    row.department_id === null || row.department_id === undefined
      ? null
      : Number(row.department_id);
  return {
    id: Number(row.id),
    code: row.code ?? '',
    name: row.name ?? '',
    description: row.description ?? null,
    departmentId,
  };
}

function readSaved(name: CatalogName): SavedCatalog | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_PREFIX + name);
    const saved = raw ? (JSON.parse(raw) as SavedCatalog) : null;
    return saved && typeof saved.version === 'string' && Array.isArray(saved.items) ? saved : null;
  } catch {
    return null;
  }
}

function writeSaved(name: CatalogName, saved: SavedCatalog): void {
  try {
    sessionStorage.setItem(STORAGE_PREFIX + name, JSON.stringify(saved));
  } catch {
    // Sin almacenamiento (modo privado, cuota llena) el catálogo sigue en memoria.
  }
}

/**
 * Catálogos de la DIAN (NgRx SignalStore). `load` e `items` dan un catálogo corto entero: se pide una vez por sesión
 * y la copia queda en `sessionStorage` con su versión; al recargar la página se vuelve a pedir con `If-None-Match` y
 * un 304 (sin cambios) reutiliza la copia. `page` busca en un catálogo largo y `find` resuelve el nombre de un id ya
 * guardado; los municipios se pueden pedir solo de un departamento (`departmentId`). Los errores no abren aviso: el formulario sigue con el id que tenga («Otro (id N)»).
 */
export const CatalogsStore = signalStore(
  { providedIn: 'root' },
  withState<CatalogsState>({ entries: {} }),
  withMethods((store, http = inject(HttpClient), config = inject(ApiConfiguration)) => {
    const loads = new Map<SmallCatalogName, Promise<readonly CatalogItem[]>>();
    const pages = new Map<string, Promise<CatalogPage>>();

    const urlOf = (name: CatalogName) =>
      `${config.rootUrl}${listCatalog.PATH.replace('{name}', encodeURIComponent(name))}`;
    const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

    function setEntry(name: SmallCatalogName, entry: CatalogEntry): void {
      patchState(store, (state) => ({ entries: { ...state.entries, [name]: entry } }));
    }

    async function fetchWhole(name: SmallCatalogName): Promise<readonly CatalogItem[]> {
      const saved = readSaved(name);
      setEntry(name, { status: 'loading', items: store.entries()[name]?.items ?? EMPTY });
      try {
        const headers = saved
          ? new HttpHeaders({ 'If-None-Match': `"${saved.version}"` })
          : undefined;
        const body = await firstValueFrom(
          http.get<CatalogListResponse>(urlOf(name), { headers, context: silent() }),
        );
        const items = (body.items ?? []).map(toItem);
        writeSaved(name, { version: body.version ?? '', items });
        setEntry(name, { status: 'ready', items });
        return items;
      } catch (error) {
        // El 304 llega como error (no es 2xx), sea `AppError` del interceptor o `HttpErrorResponse`.
        if (saved && statusOf(error) === NOT_MODIFIED) {
          setEntry(name, { status: 'ready', items: saved.items });
          return saved.items;
        }
        loads.delete(name);
        setEntry(name, { status: 'error', items: EMPTY });
        return EMPTY;
      }
    }

    function load(name: SmallCatalogName): Promise<readonly CatalogItem[]> {
      let pending = loads.get(name);
      if (!pending) {
        pending = fetchWhole(name);
        loads.set(name, pending);
      }
      return pending;
    }

    function page(
      name: PagedCatalogName,
      query: { q?: string; cursor?: string | null; departmentId?: number | null } = {},
    ): Promise<CatalogPage> {
      const q = query.q?.trim() ?? '';
      const cursor = query.cursor ?? null;
      const departmentId = query.departmentId ?? null;
      const key = JSON.stringify([name, q, cursor, departmentId]);
      const cached = pages.get(key);
      if (cached) {
        return cached;
      }
      let params = new HttpParams().set('limit', CATALOG_PAGE_SIZE);
      if (q) params = params.set('q', q);
      if (cursor) params = params.set('cursor', cursor);
      if (departmentId !== null) params = params.set('department_id', departmentId);
      const request = firstValueFrom(
        http.get<CatalogListResponse>(urlOf(name), { params, context: silent() }),
      ).then((body): CatalogPage => ({
        items: (body.items ?? []).map(toItem),
        nextCursor: body.next_cursor ?? null,
      }));
      // Una página que falló no queda guardada: la siguiente vez se pide de nuevo.
      request.catch(() => pages.delete(key));
      pages.set(key, request);
      return request;
    }

    /**
     * Fila de un catálogo largo por su id. El Api no busca por id, pero ordena por id: se recorren las páginas sin
     * filtro (las mismas que ya usó la lista, guardadas en memoria) hasta encontrarlo o pasar de largo.
     */
    async function find(name: PagedCatalogName, id: number): Promise<CatalogItem | null> {
      let cursor: string | null = null;
      do {
        const current: CatalogPage = await page(name, { cursor });
        const found = current.items.find((item) => item.id === id);
        if (found) return found;
        const last = current.items.at(-1);
        if (!last || last.id > id) return null;
        cursor = current.nextCursor;
      } while (cursor);
      return null;
    }

    return {
      load,
      page,
      find,
      /** Filas de un catálogo corto; lo pide si todavía no está. */
      items(name: SmallCatalogName): Signal<readonly CatalogItem[]> {
        void load(name);
        return computed(() => store.entries()[name]?.items ?? EMPTY);
      },
      status(name: SmallCatalogName): Signal<CatalogStatus> {
        return computed(() => store.entries()[name]?.status ?? 'idle');
      },
    };
  }),
);
