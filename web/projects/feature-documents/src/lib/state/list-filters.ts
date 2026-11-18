import { ParamMap } from '@angular/router';
import { DOCUMENT_KINDS, DOCUMENT_STATUSES } from '../models/document-status';

/*
 * Filtros de la lista de documentos y su forma en la URL (sección 8.3 de la arquitectura del frontend): la URL es
 * la fuente de verdad, así una búsqueda se puede compartir o recargar. Los nombres de los parámetros van en español,
 * como las rutas del panel.
 */

export interface DocumentFilters {
  /** Fecha de emisión desde (`YYYY-MM-DD`). */
  from?: string;
  /** Fecha de emisión hasta (`YYYY-MM-DD`). */
  to?: string;
  kind?: string;
  /** Uno o varios estados en la DIAN. */
  status?: string[];
  /** Texto en la identificación o el nombre del cliente. */
  customer?: string;
  /** Número con prefijo o solo los dígitos. */
  number?: string;
  /** Si la persona pidió ver cuántos documentos hay (`include_total`, una consulta más en el Api). */
  withTotal?: boolean;
}

const PARAMS = {
  from: 'desde',
  to: 'hasta',
  kind: 'tipo',
  status: 'estado',
  customer: 'cliente',
  number: 'numero',
  withTotal: 'total',
} as const;

const DATE = /^\d{4}-\d{2}-\d{2}$/;

function text(value: string | null): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

/** Lee los filtros de la URL; los valores que el Api no acepta se descartan. */
export function filtersFromParams(params: ParamMap): DocumentFilters {
  const from = text(params.get(PARAMS.from));
  const to = text(params.get(PARAMS.to));
  const kind = text(params.get(PARAMS.kind));
  const status = (params.get(PARAMS.status) ?? '')
    .split(',')
    .map((value) => value.trim())
    .filter((value) => (DOCUMENT_STATUSES as readonly string[]).includes(value));
  const customer = text(params.get(PARAMS.customer));
  const number = text(params.get(PARAMS.number));

  const filters: DocumentFilters = {};
  if (from && DATE.test(from)) filters.from = from;
  if (to && DATE.test(to)) filters.to = to;
  if (kind && (DOCUMENT_KINDS as readonly string[]).includes(kind)) filters.kind = kind;
  if (status.length > 0) filters.status = status;
  if (customer) filters.customer = customer;
  if (number) filters.number = number;
  if (params.get(PARAMS.withTotal) === '1') filters.withTotal = true;
  return filters;
}

/** Parámetros de la URL para `router.navigate`; `null` borra el parámetro. */
export function filtersToQueryParams(filters: DocumentFilters): Record<string, string | null> {
  return {
    [PARAMS.from]: text(filters.from ?? null) ?? null,
    [PARAMS.to]: text(filters.to ?? null) ?? null,
    [PARAMS.kind]: text(filters.kind ?? null) ?? null,
    [PARAMS.status]: filters.status?.length ? filters.status.join(',') : null,
    [PARAMS.customer]: text(filters.customer ?? null) ?? null,
    [PARAMS.number]: text(filters.number ?? null) ?? null,
    [PARAMS.withTotal]: filters.withTotal ? '1' : null,
  };
}
