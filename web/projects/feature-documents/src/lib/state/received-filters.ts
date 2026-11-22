import { ParamMap } from '@angular/router';

/*
 * Filters of the received documents list and their shape in the URL, like the documents list (`list-filters.ts`): the
 * URL is the source of truth, so a search can be shared or reloaded. Parameter names are Spanish, like the routes.
 */

export interface ReceivedFilters {
  /** Exact NIT of the supplier, digits only, without check digit (`supplier_nit`). */
  supplierNit?: string;
  /** Issue date from (`YYYY-MM-DD`, inclusive). */
  from?: string;
  /** Issue date to (`YYYY-MM-DD`, inclusive). */
  to?: string;
}

const PARAMS = { supplierNit: 'proveedor', from: 'desde', to: 'hasta' } as const;

const DATE = /^\d{4}-\d{2}-\d{2}$/;
/** The API takes up to 20 digits (`supplier_nit debe tener solo dígitos`). */
const NIT = /^\d{1,20}$/;

function text(value: string | null | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

/** Reads the filters of the URL; values the API does not take are dropped. */
export function receivedFiltersFromParams(params: ParamMap): ReceivedFilters {
  const supplierNit = text(params.get(PARAMS.supplierNit));
  const from = text(params.get(PARAMS.from));
  const to = text(params.get(PARAMS.to));
  const filters: ReceivedFilters = {};
  if (supplierNit && NIT.test(supplierNit)) filters.supplierNit = supplierNit;
  if (from && DATE.test(from)) filters.from = from;
  if (to && DATE.test(to)) filters.to = to;
  return filters;
}

/** URL parameters for `router.navigate`; `null` removes the parameter. */
export function receivedFiltersToQueryParams(
  filters: ReceivedFilters,
): Record<string, string | null> {
  return {
    [PARAMS.supplierNit]: text(filters.supplierNit) ?? null,
    [PARAMS.from]: text(filters.from) ?? null,
    [PARAMS.to]: text(filters.to) ?? null,
  };
}
