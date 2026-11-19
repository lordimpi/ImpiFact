import { UiPaginatorLabels } from '../paginator/pagination';

/** Columna de `ui-table`. `key` es el campo de la fila que se muestra y el que viaja en orden y filtros. */
export interface UiTableColumn {
  key: string;
  /** Encabezado visible (ya traducido). */
  header: string;
  /** Si el Api permite ordenar por esta columna. */
  sortable?: boolean;
  /** Si el Api permite filtrar por esta columna: muestra un campo de texto bajo el encabezado. */
  filterable?: boolean;
  /** Etiqueta accesible del filtro; por defecto, el encabezado. */
  filterLabel?: string;
  /** Alineación de la celda; `end` para importes y cantidades. */
  align?: 'start' | 'end';
}

/** Orden pedido al Api. */
export interface UiSort {
  key: string;
  direction: 'asc' | 'desc';
}

/** Filtros pedidos al Api, por `key` de columna. Solo lleva los filtros con valor. */
export type UiFilters = Readonly<Record<string, string>>;

/** Textos de la tabla, ya traducidos. */
export interface UiTableLabels {
  loading: string;
  emptyTitle: string;
  emptyDescription?: string;
  errorTitle: string;
  errorDescription?: string;
  retry: string;
  paginator: UiPaginatorLabels;
}
