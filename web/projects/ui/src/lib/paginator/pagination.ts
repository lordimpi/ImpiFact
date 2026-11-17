/**
 * Estado de paginación que entrega el Api. La paginación siempre ocurre en el servidor:
 * - `cursor`: cursores opacos para ir a la página anterior o a la siguiente (`null` si no hay).
 * - `pages`: página actual (desde 1), tamaño de página y total de registros; muestra números de página.
 */
export type UiPagination =
  | { kind: 'cursor'; previousCursor: string | null; nextCursor: string | null }
  | { kind: 'pages'; page: number; pageSize: number; total: number };

/** Página pedida por el usuario; quien usa el componente la convierte en una consulta al Api. */
export type UiPageChange =
  | { kind: 'cursor'; cursor: string; direction: 'previous' | 'next' }
  | { kind: 'pages'; page: number };

/** Textos del paginador, ya traducidos. `page` lleva el marcador `{page}`, por ejemplo «Página {page}». */
export interface UiPaginatorLabels {
  navigation: string;
  previous: string;
  next: string;
  page: string;
}
