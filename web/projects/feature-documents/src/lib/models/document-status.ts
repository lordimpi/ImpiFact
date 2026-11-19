/*
 * Estados y tipos de documento que devuelve el Api (`DocumentStatus` y `kind` del backend). Solo sirven para mostrar
 * textos y decidir qué acciones ofrecer; el Api valida cada acción por su cuenta.
 */

/** Estados del documento en el orden del backend. */
export const DOCUMENT_STATUSES = [
  'Received',
  'Queued',
  'Sent',
  'Accepted',
  'AcceptedWithWarnings',
  'Rejected',
  'Unconfirmed',
  'Failed',
  'ContingencyPending',
] as const;

/** Tipos de documento que acepta el filtro `kind` de la lista. */
export const DOCUMENT_KINDS = ['invoice', 'credit_note', 'debit_note'] as const;

export type DocumentKind = (typeof DOCUMENT_KINDS)[number];

/** Estados en que la DIAN todavía no da una respuesta final: el detalle sigue consultando (sección 9). */
const PENDING_STATUSES = new Set(['Queued', 'Sent', 'Unconfirmed']);

/** Estados con la factura aceptada: ya existen el PDF, el AttachedDocument y el ZIP. */
const ACCEPTED_STATUSES = new Set(['Accepted', 'AcceptedWithWarnings']);

export function isPendingStatus(status: string | null | undefined): boolean {
  return PENDING_STATUSES.has(status ?? '');
}

export function filesAvailable(status: string | null | undefined): boolean {
  return ACCEPTED_STATUSES.has(status ?? '');
}

/** Clave del texto del estado (`documents.statuses.*`). */
export function statusKey(status: string | null | undefined): string {
  return (DOCUMENT_STATUSES as readonly string[]).includes(status ?? '')
    ? `statuses.${status}`
    : 'statuses.unknown';
}

/** Clave del texto del tipo (`documents.kinds.*`). */
export function kindKey(kind: string | null | undefined): string {
  return (DOCUMENT_KINDS as readonly string[]).includes(kind ?? '')
    ? `kinds.${kind}`
    : 'kinds.unknown';
}

/** Tono visual del estado: aceptado, rechazado o con error, o en proceso. */
export function statusTone(status: string | null | undefined): 'success' | 'danger' | 'pending' {
  if (ACCEPTED_STATUSES.has(status ?? '')) {
    return 'success';
  }
  return status === 'Rejected' || status === 'Failed' ? 'danger' : 'pending';
}
