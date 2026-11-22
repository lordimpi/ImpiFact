import { WritableSignal, signal } from '@angular/core';
import { FieldTree, applyEach, form, pattern, required } from '@angular/forms/signals';
import {
  DocumentDetailResponse,
  DocumentTotalsCalculation,
  IssueCreditNote$Params,
  IssueDebitNote$Params,
  IssueSupportDocumentAdjustmentNote$Params,
} from '@impifact/api';
import {
  IVA_TAX_ID,
  InvoiceLineModel,
  buyerOf,
  emptyLine,
  lineRules,
  linesBody,
  taxEntry,
  emailRequest,
} from './invoice.form';

/*
 * Formulario de las notas crédito y débito con Signal Forms. Una nota corrige una factura aceptada por la DIAN
 * (Dirección de Impuestos y Aduanas Nacionales): la crédito la anula o la rebaja, la débito le suma cargos. Como en
 * la factura, aquí no se calcula ningún valor fiscal: las líneas se mandan a `calculateDocumentTotals` y el cuerpo
 * lleva los totales que devolvió el servidor. El CUDE (código único de la nota) lo calcula el Api. La nota de ajuste
 * corrige un documento soporte aceptado: lleva al proveedor (`seller`) en lugar del cliente y su código es el CUDS.
 */

export type NoteKind = 'credit' | 'debit' | 'adjustment';

/** Tipo de documento de cada nota en las tablas de la DIAN. */
const NOTE_DOCUMENT_TYPE: Record<NoteKind, number> = { credit: 4, debit: 5, adjustment: 13 };

/** Conceptos de la nota de ajuste al documento soporte (catálogo de la DIAN, del 1 al 5); el Api no los publica. */
export const ADJUSTMENT_CONCEPTS = ['1', '2', '3', '4', '5'] as const;

/** Código postal: solo dígitos, como pide el Api para el proveedor. */
const POSTAL_CODE = /^\d{1,10}$/;

export interface NoteModel {
  /** Concepto de corrección (id del catálogo de discrepancias), como texto de la lista desplegable. */
  discrepancyCode: string;
  notes: string;
  /** Pedir al Api que envíe la nota por correo al cliente cuando la DIAN la acepte (`sendmail`). */
  sendEmail: boolean;
  /**
   * Código postal del proveedor (solo la nota de ajuste): el Api lo exige y el documento soporte guardado no lo
   * devuelve, así que la persona lo escribe.
   */
  sellerPostalCode: string;
  lines: InvoiceLineModel[];
}

export type NoteForm = FieldTree<NoteModel>;

export function emptyNote(): NoteModel {
  return { discrepancyCode: '', notes: '', sendEmail: true, sellerPostalCode: '', lines: [] };
}

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createNoteForm(
  model: WritableSignal<NoteModel> = signal(emptyNote()),
  kind: () => NoteKind = () => 'credit',
): NoteForm {
  return form(model, (path) => {
    required(path.discrepancyCode);
    required(path.sellerPostalCode, { when: () => kind() === 'adjustment' });
    pattern(path.sellerPostalCode, POSTAL_CODE);
    applyEach(path.lines, lineRules);
  });
}

function toNumber(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Porcentaje como texto de la lista (`'19.00'` pasa a `'19'`). */
function percentText(value: number | string | null | undefined): string {
  const percent = toNumber(value);
  return percent === null ? '0' : String(percent);
}

/**
 * Líneas de la factura original para empezar la nota: mismo código, descripción, cantidad, precio, unidad e impuesto.
 * La persona baja cantidades o quita líneas (devolución parcial) o las deja (anulación).
 */
export function linesFromDocument(document: DocumentDetailResponse): InvoiceLineModel[] {
  return (document.lines ?? []).map((line) => {
    const tax = line.tax_totals?.[0];
    const base = emptyLine();
    return {
      ...base,
      code: line.code ?? '',
      description: line.description ?? '',
      quantity: toNumber(line.invoiced_quantity) ?? 1,
      unitPrice: toNumber(line.price_amount),
      unitMeasureId: toNumber(line.unit_measure_id) ?? base.unitMeasureId,
      typeItemIdentificationId:
        toNumber(line.type_item_identification_id) ?? base.typeItemIdentificationId,
      taxId: toNumber(tax?.tax_id) ?? IVA_TAX_ID,
      taxPercent: tax ? percentText(tax.percent) : '0',
    };
  });
}

type CreditNoteBody = IssueCreditNote$Params['body'];
type DebitNoteBody = IssueDebitNote$Params['body'];
type AdjustmentNoteBody = IssueSupportDocumentAdjustmentNote$Params['body'];

/**
 * Cuerpo de `issueCreditNote` o `issueDebitNote`: la referencia a la factura (número, CUFE y fecha), el concepto, el
 * comprador de la factura y las líneas con los totales que calculó el servidor (`calculation`). La débito lleva los
 * totales en `requested_monetary_totals`, como el contrato de ApiDian. La de ajuste lleva al proveedor del documento
 * soporte (`seller`, con el código postal que escribió la persona); su correo va al del proveedor.
 */
export function buildNoteBody(
  kind: NoteKind,
  model: NoteModel,
  original: DocumentDetailResponse,
  calculation: DocumentTotalsCalculation,
): CreditNoteBody | DebitNoteBody | AdjustmentNoteBody {
  const lines = linesBody(model.lines, calculation);
  const party = buyerOf(original.customer ?? {});
  const body: CreditNoteBody & DebitNoteBody & AdjustmentNoteBody = {
    type_document_id: NOTE_DOCUMENT_TYPE[kind],
    discrepancyresponsecode: Number(model.discrepancyCode),
    billing_reference: {
      number: original.full_number ?? '',
      uuid: original.cufe ?? '',
      issue_date: original.issue_date ?? '',
    },
    tax_totals: calculation.tax_totals.flatMap((total) => total.tax_subtotals.map(taxEntry)),
  };
  if (model.notes.trim()) {
    body.notes = model.notes.trim();
  }
  if (kind === 'adjustment') {
    body.seller = { ...party, postal_zone_code: model.sellerPostalCode.trim() };
    body.legal_monetary_totals = calculation.legal_monetary_totals;
    body.credit_note_lines = lines;
    return { ...body, ...emailRequest(model.sendEmail, original.customer) };
  }
  body.customer = party;
  if (kind === 'credit') {
    body.legal_monetary_totals = calculation.legal_monetary_totals;
    body.credit_note_lines = lines;
  } else {
    body.requested_monetary_totals = calculation.legal_monetary_totals;
    body.debit_note_lines = lines;
  }
  return { ...body, ...emailRequest(model.sendEmail, original.customer) };
}
