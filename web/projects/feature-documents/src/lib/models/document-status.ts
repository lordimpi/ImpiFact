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
export const DOCUMENT_KINDS = [
  'invoice',
  'credit_note',
  'debit_note',
  'support_document',
  'support_document_adjustment_note',
] as const;

export type DocumentKind = (typeof DOCUMENT_KINDS)[number];

/** Tipo de documento DIAN (`document_type_id`) del documento soporte y de su nota de ajuste. */
export const SUPPORT_DOCUMENT_TYPE = 11;
export const SUPPORT_ADJUSTMENT_NOTE_TYPE = 13;

/**
 * Tipo del documento para mostrar: el `kind` del Api o, si llega nulo (el Api no lo da para el documento soporte ni
 * para su nota de ajuste), el que corresponde a su `document_type_id`.
 */
export function documentKindOf(
  document: { kind?: string | null; document_type_id?: number | string | null } | null | undefined,
): string | null {
  if (document?.kind) {
    return document.kind;
  }
  switch (Number(document?.document_type_id)) {
    case SUPPORT_DOCUMENT_TYPE:
      return 'support_document';
    case SUPPORT_ADJUSTMENT_NOTE_TYPE:
      return 'support_document_adjustment_note';
    default:
      return null;
  }
}

/** Si el documento es de la familia del documento soporte (la empresa es el comprador y el tercero, el proveedor). */
export function isSupportDocumentFamily(
  document: { kind?: string | null; document_type_id?: number | string | null } | null | undefined,
): boolean {
  const kind = documentKindOf(document);
  return kind === 'support_document' || kind === 'support_document_adjustment_note';
}

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
