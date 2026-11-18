/*
 * Valores fijos de la DIAN (Dirección de Impuestos y Aduanas Nacionales) que usa el feature: ambientes, familias de
 * documentos, tipos de documento de las resoluciones y el ejemplo de habilitación. Solo sirven para mostrar textos y
 * armar solicitudes; el Api valida cada valor.
 */

/** Ambientes de la DIAN (catálogo `type_environments`). */
export const PRODUCTION = 1;
export const HABILITATION = 2;

/**
 * Familias de documentos (`document_kind` del Api) con el campo de `setCompanyEnvironment` que cambia el ambiente
 * de cada una.
 */
export const ENVIRONMENT_KINDS = [
  { kind: 1, field: 'type_environment_id' },
  { kind: 2, field: 'payroll_type_environment_id' },
  { kind: 3, field: 'eqdocs_type_environment_id' },
  { kind: 4, field: 'support_document_type_environment_id' },
  { kind: 5, field: 'event_type_environment_id' },
] as const;

export type EnvironmentKind = (typeof ENVIRONMENT_KINDS)[number]['kind'];
export type EnvironmentField = (typeof ENVIRONMENT_KINDS)[number]['field'];

/** Familia de la factura electrónica y sus notas. */
export const INVOICE_KIND = 1;

/** Tipos de documento con resolución de numeración (`type_document_id`). */
export const RESOLUTION_DOCUMENT_TYPES = [1, 2, 3, 4, 5, 11, 13] as const;

/**
 * Resolución de habilitación que la DIAN publica para todos los facturadores en pruebas (prefijo SETP). La clave
 * técnica es la del ambiente de habilitación.
 */
export const HABILITATION_RESOLUTION = {
  type_document_id: '1',
  prefix: 'SETP',
  resolution: '18760000001',
  resolution_date: '2019-01-19',
  technical_key: 'fc8eac422eba16e22ffd8c6f94b3f40a6e38162c',
  from: '990000000',
  to: '995000000',
  date_from: '2019-01-19',
  date_to: '2030-01-19',
} as const;

/** Familia de la factura en los textos (`companies.environments.kinds.*`). */
export function kindKey(kind: number | string | undefined): string {
  return `environments.kinds.${kind ?? ''}`;
}

/** Estados en que la DIAN aún no da respuesta final: el asistente sigue consultando. */
const PENDING_STATUSES = new Set(['Received', 'Queued', 'Sent', 'Unconfirmed']);
const ACCEPTED_STATUSES = new Set(['Accepted', 'AcceptedWithWarnings']);
const KNOWN_STATUSES = new Set([
  ...PENDING_STATUSES,
  ...ACCEPTED_STATUSES,
  'Rejected',
  'Failed',
  'ContingencyPending',
]);

export function isPendingStatus(status: string | null | undefined): boolean {
  return PENDING_STATUSES.has(status ?? '');
}

export function isAcceptedStatus(status: string | null | undefined): boolean {
  return ACCEPTED_STATUSES.has(status ?? '');
}

/** Clave del texto del estado (`companies.wizard.steps.testSet.statuses.*`). */
export function statusKey(status: string | null | undefined): string {
  return `wizard.steps.testSet.statuses.${KNOWN_STATUSES.has(status ?? '') ? status : 'unknown'}`;
}
