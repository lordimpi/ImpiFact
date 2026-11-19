import { WritableSignal, signal } from '@angular/core';
import {
  FieldTree,
  SchemaPathTree,
  applyEach,
  form,
  min,
  required,
  validate,
} from '@angular/forms/signals';
import {
  CalculateDocumentTotalsRequest,
  CalculatedTaxSubtotal,
  CustomerResponse,
  DocumentTotalsCalculation,
  IssueInvoice$Params,
  ProductResponse,
} from '@impifact/api';

/*
 * Formulario de emisión de factura con Signal Forms (`@angular/forms/signals`). Aquí no se calcula ningún valor
 * fiscal: el formulario solo guarda lo que la persona escribe, `buildCalculationRequest` lo manda al Api para que
 * calcule (`POST /api/v1/documents/calculate`) y `buildInvoiceBody` arma la emisión con los valores que devolvió el
 * servidor. Así el CUFE (código único de la factura) no depende de la aritmética de JavaScript.
 */

/** IVA (impuesto al valor agregado) en las tablas de la DIAN. */
export const IVA_TAX_ID = 1;
/** Unidad de medida por defecto (unidad) y tipo de código del ítem (estándar del contribuyente). */
const DEFAULT_UNIT_MEASURE_ID = 70;
const DEFAULT_ITEM_IDENTIFICATION_ID = 4;

export const PAYMENT_FORMS = ['1', '2'] as const;
export const PAYMENT_METHODS = ['10', '20', '42', '47', '48', '49', '1'] as const;
export const IVA_PERCENTS = ['19', '5', '0'] as const;
/** Forma de pago a crédito: exige fecha de vencimiento y plazo. */
export const CREDIT_PAYMENT_FORM = '2';

export interface InvoiceLineModel {
  /** Producto del catálogo del que salió la línea; `null` en una línea libre. */
  productId: string | null;
  code: string;
  description: string;
  quantity: number | null;
  /** Precio unitario sin impuestos. */
  unitPrice: number | null;
  unitMeasureId: number;
  typeItemIdentificationId: number;
  taxId: number;
  /** Porcentaje del impuesto como texto (`'19'`, `'5'`, `'0'`), el valor de la lista desplegable. */
  taxPercent: string;
}

export interface InvoiceModel {
  paymentFormId: string;
  paymentMethodId: string;
  /** Fecha de vencimiento `YYYY-MM-DD` (solo a crédito). */
  paymentDueDate: string;
  /** Plazo en días (solo a crédito). */
  durationDays: number | null;
  notes: string;
  /** Pedir al Api que envíe la factura por correo al cliente cuando la DIAN la acepte (`sendmail`). */
  sendEmail: boolean;
  lines: InvoiceLineModel[];
}

export type InvoiceForm = FieldTree<InvoiceModel>;

export function emptyLine(): InvoiceLineModel {
  return {
    productId: null,
    code: '',
    description: '',
    quantity: 1,
    unitPrice: null,
    unitMeasureId: DEFAULT_UNIT_MEASURE_ID,
    typeItemIdentificationId: DEFAULT_ITEM_IDENTIFICATION_ID,
    taxId: IVA_TAX_ID,
    taxPercent: '19',
  };
}

/** Identificación del consumidor final (comprador sin identificar) en la DIAN. */
export const FINAL_CONSUMER_IDENTIFICATION = '222222222222';

export function emptyInvoice(): InvoiceModel {
  return {
    paymentFormId: '1',
    paymentMethodId: '10',
    paymentDueDate: '',
    durationDays: null,
    notes: '',
    sendEmail: true,
    lines: [],
  };
}

/** Texto decimal del Api a número para el campo; un valor que no es número queda vacío. */
function toNumber(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Línea nueva con los datos del producto (precio e impuesto del catálogo). */
export function lineFromProduct(product: ProductResponse): InvoiceLineModel {
  const percent = toNumber(product.tax_percent);
  return {
    productId: product.id ?? null,
    code: product.code ?? '',
    description: product.name ?? '',
    quantity: 1,
    unitPrice: toNumber(product.unit_price),
    unitMeasureId: toNumber(product.unit_measure_id) ?? DEFAULT_UNIT_MEASURE_ID,
    typeItemIdentificationId:
      toNumber(product.type_item_identification_id) ?? DEFAULT_ITEM_IDENTIFICATION_ID,
    taxId: toNumber(product.tax_id) ?? IVA_TAX_ID,
    taxPercent: percent === null ? '19' : String(percent),
  };
}

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createInvoiceForm(
  model: WritableSignal<InvoiceModel> = signal(emptyInvoice()),
): InvoiceForm {
  return form(model, (path) => {
    required(path.paymentFormId);
    required(path.paymentMethodId);
    const onCredit = ({ valueOf }: { valueOf: (p: typeof path.paymentFormId) => string }) =>
      valueOf(path.paymentFormId) === CREDIT_PAYMENT_FORM;
    required(path.paymentDueDate, { when: onCredit });
    required(path.durationDays, { when: onCredit });
    applyEach(path.lines, lineRules);
  });
}

/** Reglas de una línea (las comparten la factura y las notas): código, descripción, precio y cantidad. */
export function lineRules(line: SchemaPathTree<InvoiceLineModel>): void {
  required(line.code);
  required(line.description);
  required(line.unitPrice);
  min(line.unitPrice, 0);
  validate(line.quantity, ({ value }) => {
    const quantity = value();
    return quantity !== null && quantity > 0 ? undefined : { kind: 'quantity' };
  });
}

/** Una línea se puede calcular cuando tiene cantidad mayor que cero y precio no negativo. */
function isComplete(line: InvoiceLineModel): boolean {
  return (
    line.quantity !== null &&
    line.quantity > 0 &&
    line.unitPrice !== null &&
    line.unitPrice >= 0 &&
    line.taxPercent !== ''
  );
}

/**
 * Lo que se pide a `calculateDocumentTotals`; `null` si no hay líneas o alguna está incompleta (no se calcula a
 * medias, así los totales siempre corresponden a todas las líneas).
 */
export function buildCalculationRequest(
  model: InvoiceModel,
): CalculateDocumentTotalsRequest | null {
  return buildLinesCalculation(model.lines);
}

/** Lo mismo para cualquier documento con líneas (factura o nota). */
export function buildLinesCalculation(
  lines: readonly InvoiceLineModel[],
): CalculateDocumentTotalsRequest | null {
  if (lines.length === 0 || !lines.every(isComplete)) {
    return null;
  }
  return {
    lines: lines.map((line) => ({
      quantity: line.quantity,
      unit_price: line.unitPrice,
      free_of_charge: false,
      taxes: [{ tax_id: line.taxId, percent: Number(line.taxPercent) }],
    })),
  };
}

type IssueBody = IssueInvoice$Params['body'];

export function taxEntry(tax: CalculatedTaxSubtotal) {
  const entry: Record<string, number | string> = {
    tax_id: tax.tax_id,
    tax_amount: tax.tax_amount,
    taxable_amount: tax.taxable_amount,
  };
  for (const key of [
    'percent',
    'unit_measure_id',
    'per_unit_amount',
    'base_unit_measure',
  ] as const) {
    const value = tax[key];
    if (value !== null && value !== undefined) entry[key] = value;
  }
  return entry;
}

const CUSTOMER_FIELDS = [
  'identification_number',
  'dv',
  'type_document_identification_id',
  'type_organization_id',
  'type_regime_id',
  'type_liability_id',
  'name',
  'phone',
  'address',
  'email',
  'merchant_registration',
  'municipality_id',
] as const;

/** Datos del comprador para el cuerpo de la emisión, sin los campos vacíos. */
export function buyerOf(
  customer: Partial<Record<(typeof CUSTOMER_FIELDS)[number], number | string | null>>,
): Record<string, number | string> {
  const buyer: Record<string, number | string> = {};
  for (const field of CUSTOMER_FIELDS) {
    const value = customer[field];
    if (value !== null && value !== undefined && value !== '') buyer[field] = value;
  }
  return buyer;
}

/** Líneas del cuerpo con el valor y los impuestos que calculó el servidor para cada una. */
export function linesBody(
  lines: readonly InvoiceLineModel[],
  calculation: DocumentTotalsCalculation,
) {
  return lines.map((line, index) => {
    const calculated = calculation.lines[index];
    return {
      unit_measure_id: line.unitMeasureId,
      invoiced_quantity: line.quantity,
      base_quantity: line.quantity,
      line_extension_amount: calculated?.line_extension_amount ?? null,
      free_of_charge_indicator: false,
      description: line.description,
      code: line.code,
      type_item_identification_id: line.typeItemIdentificationId,
      price_amount: line.unitPrice,
      tax_totals: (calculated?.tax_subtotals ?? []).map(taxEntry),
    };
  });
}

/**
 * Cuerpo de `issueInvoice`: los datos del formulario y del cliente con los totales, impuestos y valores de línea
 * que calculó el servidor (`calculation`, la respuesta a `buildCalculationRequest(model)`).
 */
export function buildInvoiceBody(
  model: InvoiceModel,
  customer: CustomerResponse,
  calculation: DocumentTotalsCalculation,
): IssueBody {
  const buyer = buyerOf(customer);

  const onCredit = model.paymentFormId === CREDIT_PAYMENT_FORM;
  const payment: Record<string, number | string> = {
    payment_form_id: Number(model.paymentFormId),
    payment_method_id: Number(model.paymentMethodId),
  };
  if (onCredit) {
    payment['payment_due_date'] = model.paymentDueDate;
    payment['duration_measure'] = model.durationDays ?? 0;
  }

  const body: IssueBody = {
    type_document_id: 1,
    customer: buyer,
    payment_form: [payment],
    legal_monetary_totals: calculation.legal_monetary_totals,
    tax_totals: calculation.tax_totals.flatMap((total) => total.tax_subtotals.map(taxEntry)),
    invoice_lines: linesBody(model.lines, calculation),
  };
  if (model.notes.trim()) {
    body.notes = model.notes.trim();
  }
  return { ...body, ...emailRequest(model.sendEmail, customer) };
}

/**
 * Campos del correo del cuerpo de emisión: `sendmail` si la persona lo pidió y el cliente tiene correo y, para el
 * consumidor final, ese correo en `email_pos_customer` (sin él el Api no le escribe, como el PHP).
 */
export function emailRequest(
  sendEmail: boolean,
  customer: { email?: unknown; identification_number?: unknown } | null | undefined,
): { sendmail?: true; email_pos_customer?: string } {
  const email = typeof customer?.email === 'string' ? customer.email.trim() : '';
  if (!sendEmail || !email) {
    return {};
  }
  const finalConsumer =
    String(customer?.identification_number ?? '').trim() === FINAL_CONSUMER_IDENTIFICATION;
  return finalConsumer ? { sendmail: true, email_pos_customer: email } : { sendmail: true };
}
