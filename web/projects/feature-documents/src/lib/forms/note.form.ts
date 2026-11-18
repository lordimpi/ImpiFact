import { WritableSignal, signal } from '@angular/core';
import { FieldTree, applyEach, form, required } from '@angular/forms/signals';
import {
  DocumentDetailResponse,
  DocumentTotalsCalculation,
  IssueCreditNote$Params,
  IssueDebitNote$Params,
} from '@impifact/api';
import {
  IVA_TAX_ID,
  InvoiceLineModel,
  buyerOf,
  emptyLine,
  lineRules,
  linesBody,
  taxEntry,
} from './invoice.form';

/*
 * Formulario de las notas crédito y débito con Signal Forms. Una nota corrige una factura aceptada por la DIAN
 * (Dirección de Impuestos y Aduanas Nacionales): la crédito la anula o la rebaja, la débito le suma cargos. Como en
 * la factura, aquí no se calcula ningún valor fiscal: las líneas se mandan a `calculateDocumentTotals` y el cuerpo
 * lleva los totales que devolvió el servidor. El CUDE (código único de la nota) lo calcula el Api.
 */

export type NoteKind = 'credit' | 'debit';

/** Tipo de documento de cada nota en las tablas de la DIAN. */
const NOTE_DOCUMENT_TYPE: Record<NoteKind, number> = { credit: 4, debit: 5 };

export interface NoteModel {
  /** Concepto de corrección (id del catálogo de discrepancias), como texto de la lista desplegable. */
  discrepancyCode: string;
  notes: string;
  lines: InvoiceLineModel[];
}

export type NoteForm = FieldTree<NoteModel>;

export function emptyNote(): NoteModel {
  return { discrepancyCode: '', notes: '', lines: [] };
}

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createNoteForm(model: WritableSignal<NoteModel> = signal(emptyNote())): NoteForm {
  return form(model, (path) => {
    required(path.discrepancyCode);
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

/**
 * Cuerpo de `issueCreditNote` o `issueDebitNote`: la referencia a la factura (número, CUFE y fecha), el concepto, el
 * comprador de la factura y las líneas con los totales que calculó el servidor (`calculation`). La débito lleva los
 * totales en `requested_monetary_totals`, como el contrato de ApiDian.
 */
export function buildNoteBody(
  kind: NoteKind,
  model: NoteModel,
  original: DocumentDetailResponse,
  calculation: DocumentTotalsCalculation,
): CreditNoteBody | DebitNoteBody {
  const lines = linesBody(model.lines, calculation);
  const body: CreditNoteBody & DebitNoteBody = {
    type_document_id: NOTE_DOCUMENT_TYPE[kind],
    discrepancyresponsecode: Number(model.discrepancyCode),
    billing_reference: {
      number: original.full_number ?? '',
      uuid: original.cufe ?? '',
      issue_date: original.issue_date ?? '',
    },
    customer: buyerOf(original.customer ?? {}),
    tax_totals: calculation.tax_totals.flatMap((total) => total.tax_subtotals.map(taxEntry)),
  };
  if (kind === 'credit') {
    body.legal_monetary_totals = calculation.legal_monetary_totals;
    body.credit_note_lines = lines;
  } else {
    body.requested_monetary_totals = calculation.legal_monetary_totals;
    body.debit_note_lines = lines;
  }
  if (model.notes.trim()) {
    body.notes = model.notes.trim();
  }
  return body;
}
