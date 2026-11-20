// Invoice payloads for POST /api/v1/documents/invoices, built from the golden case
// backend/tests/golden/invoice/single-line-iva19/input.json (the same body Scalar shows as the first example).
//
// Differences from the golden file, all needed to issue many invoices in one run:
// - no `number`: the API takes the next number of the SETP resolution, as a real integration would;
// - `date`, `time` and `payment_due_date` are "now" in Colombia (UTC-5), so the invoice is inside the resolution dates;
// - the customer comes from a pool of CUSTOMER_POOL fictitious companies, so the list filter by customer has data;
//   their NITs start at 800100001;
// - the line price varies, and its 19 % IVA (exact to the cent) and the totals follow it.

import http from 'k6/http';
import { BASE_URL, authHeaders } from './config.js';

export const ISSUE_PATH = '/api/v1/documents/invoices';

export const CUSTOMER_POOL = 50;

/** Fictitious customer NITs start after this one. */
const CUSTOMER_BASE_NIT = 800100000;

/** Check digit (dígito de verificación) of a Colombian NIT, with the weights the DIAN publishes. */
export function nitCheckDigit(nit) {
  const weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
  const digits = String(nit).split('').reverse();
  let sum = 0;
  for (let i = 0; i < digits.length; i++) {
    sum += Number(digits[i]) * weights[i];
  }
  const rest = sum % 11;
  return rest > 1 ? String(11 - rest) : String(rest);
}

// NITs whose check digit is 0 are left out of the pool: the API takes `dv: "0"` as missing (see
// docs/Pruebas de carga.md, «Hallazgos»), and the scenario measures issuing, not that rejection.
const CUSTOMER_NITS = (() => {
  const nits = [];
  for (let nit = CUSTOMER_BASE_NIT + 1; nits.length < CUSTOMER_POOL; nit++) {
    if (nitCheckDigit(nit) !== '0') {
      nits.push(String(nit));
    }
  }
  return nits;
})();

export function customerNit(index) {
  return CUSTOMER_NITS[index % CUSTOMER_POOL];
}

export function customerName(index) {
  return `CLIENTE CARGA ${String((index % CUSTOMER_POOL) + 1).padStart(3, '0')} SAS`;
}

/** Date and time in Colombia (UTC-5 all year): { date: 'yyyy-MM-dd', time: 'HH:mm:ss' }. */
export function colombiaNow() {
  const local = new Date(Date.now() - 5 * 3600 * 1000).toISOString();
  return { date: local.slice(0, 10), time: local.slice(11, 19) };
}

function money(cents) {
  return `${Math.floor(cents / 100)}.${String(cents % 100).padStart(2, '0')}`;
}

/** A valid single-line invoice with 19 % IVA for customer `index` of the pool. */
export function invoiceBody(index) {
  const { date, time } = colombiaNow();
  const nit = customerNit(index);
  // Prices between 10.000 and 2.000.000 pesos, in whole pesos so the 19 % tax is exact to the cent.
  const priceCents = (10000 + ((index * 7919) % 1990001)) * 100;
  const taxCents = (priceCents * 19) / 100;
  const price = money(priceCents);
  const tax = money(taxCents);
  const total = money(priceCents + taxCents);

  return {
    type_document_id: 1,
    date,
    time,
    resolution_number: '18760000001',
    prefix: 'SETP',
    customer: {
      identification_number: nit,
      dv: nitCheckDigit(nit),
      type_document_identification_id: 6,
      type_organization_id: 1,
      type_regime_id: 1,
      type_liability_id: 117,
      name: customerName(index),
      phone: '3000000001',
      address: 'CARRERA 2 # 2-02',
      email: `cliente.carga.${(index % CUSTOMER_POOL) + 1}@example.com`,
      merchant_registration: '0000000-00',
      municipality_id: 149,
    },
    payment_form: {
      payment_form_id: 1,
      payment_method_id: 10,
      payment_due_date: date,
      duration_measure: '0',
    },
    legal_monetary_totals: {
      line_extension_amount: price,
      tax_exclusive_amount: price,
      tax_inclusive_amount: total,
      allowance_total_amount: '0.00',
      charge_total_amount: '0.00',
      payable_amount: total,
    },
    tax_totals: [{ tax_id: 1, tax_amount: tax, percent: '19.00', taxable_amount: price }],
    invoice_lines: [
      {
        unit_measure_id: 70,
        invoiced_quantity: '1',
        line_extension_amount: price,
        free_of_charge_indicator: false,
        description: 'Servicio de consultoría (prueba de carga)',
        code: 'SERV01',
        type_item_identification_id: 4,
        price_amount: price,
        base_quantity: '1',
        tax_totals: [{ tax_id: 1, tax_amount: tax, taxable_amount: price, percent: '19.00' }],
      },
    ],
  };
}

/** Issues one invoice; `tags.endpoint` names it in the metrics. A unique Idempotency-Key goes with every request. */
export function issueInvoice(index, endpoint = 'issue_invoice') {
  const key = `k6-${__VU}-${__ITER}-${index}-${Date.now()}`;
  return http.post(`${BASE_URL}${ISSUE_PATH}`, JSON.stringify(invoiceBody(index)), {
    headers: authHeaders({ 'Content-Type': 'application/json', 'Idempotency-Key': key }),
    tags: { endpoint, name: `POST ${ISSUE_PATH}` },
  });
}
