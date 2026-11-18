import {
  CalculateDocumentTotalsRequest,
  CalculatedTaxSubtotal,
  DocumentTotalsCalculation,
  IssueTestSetInvoice$Params,
} from '@impifact/api';

/*
 * Factura de ejemplo para el set de pruebas de habilitación: una unidad de un producto de 10.000 pesos con IVA
 * (impuesto al valor agregado) del 19 %, de contado y en efectivo, a consumidor final. El panel no calcula nada: los
 * totales e impuestos salen de `calculateDocumentTotals` y el número lo asigna el Api con la resolución activa.
 */

const UNIT_PRICE = 10_000;
const IVA_TAX_ID = 1;
const IVA_PERCENT = 19;
/** Unidad «94» del catálogo de unidades de medida. */
const UNIT_MEASURE_ID = 70;
/** Código estándar del producto: 999 (de uso interno del vendedor). */
const OWN_ITEM_IDENTIFICATION = 4;
/** Forma de pago de contado y medio de pago en efectivo. */
const CASH_PAYMENT_FORM = 1;
const CASH_PAYMENT_METHOD = 10;

/** Comprador «consumidor final» que la DIAN acepta en habilitación. */
const FINAL_CONSUMER = {
  identification_number: '222222222222',
  type_document_identification_id: 3,
  type_organization_id: 2,
  type_regime_id: 2,
  type_liability_id: 117,
  municipality_id: 149,
  name: 'Consumidor final',
  address: 'No informada',
};

export const TEST_INVOICE_CALCULATION: CalculateDocumentTotalsRequest = {
  lines: [
    {
      quantity: 1,
      unit_price: UNIT_PRICE,
      free_of_charge: false,
      taxes: [{ tax_id: IVA_TAX_ID, percent: IVA_PERCENT }],
    },
  ],
};

function taxEntry(tax: CalculatedTaxSubtotal) {
  const entry: Record<string, number | string> = {
    tax_id: tax.tax_id,
    tax_amount: tax.tax_amount,
    taxable_amount: tax.taxable_amount,
  };
  if (tax.percent !== null && tax.percent !== undefined) {
    entry['percent'] = tax.percent;
  }
  return entry;
}

/** Cuerpo de `issueTestSetInvoice` con los valores que calculó el servidor. */
export function buildTestInvoiceBody(
  calculation: DocumentTotalsCalculation,
): IssueTestSetInvoice$Params['body'] {
  const line = calculation.lines[0];
  return {
    type_document_id: 1,
    customer: { ...FINAL_CONSUMER },
    payment_form: [{ payment_form_id: CASH_PAYMENT_FORM, payment_method_id: CASH_PAYMENT_METHOD }],
    legal_monetary_totals: calculation.legal_monetary_totals,
    tax_totals: calculation.tax_totals.flatMap((total) => total.tax_subtotals.map(taxEntry)),
    invoice_lines: [
      {
        unit_measure_id: UNIT_MEASURE_ID,
        invoiced_quantity: 1,
        base_quantity: 1,
        line_extension_amount: line?.line_extension_amount ?? null,
        free_of_charge_indicator: false,
        description: 'Producto de prueba de habilitación',
        code: 'PRUEBA-001',
        type_item_identification_id: OWN_ITEM_IDENTIFICATION,
        price_amount: UNIT_PRICE,
        tax_totals: (line?.tax_subtotals ?? []).map(taxEntry),
      },
    ],
  } as IssueTestSetInvoice$Params['body'];
}
