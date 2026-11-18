import { DocumentTotalsCalculation } from '@impifact/api';
import {
  InvoiceModel,
  buildCalculationRequest,
  buildInvoiceBody,
  emptyLine,
  lineFromProduct,
} from './invoice.form';

const CUSTOMER = {
  id: 'cus-1',
  identification_number: '800000001',
  dv: '1',
  type_document_identification_id: 6,
  type_organization_id: 1,
  type_regime_id: 1,
  type_liability_id: 117,
  name: 'CLIENTE DE PRUEBA GOLDEN SAS',
  phone: '3000000001',
  address: 'CARRERA 2 # 2-02',
  email: 'cliente.golden@example.com',
  merchant_registration: null,
  municipality_id: 149,
};

const CALCULATION: DocumentTotalsCalculation = {
  allowance_charges: [],
  lines: [
    {
      allowance_charges: [],
      line_extension_amount: '1500000.00',
      tax_subtotals: [
        { tax_id: 1, percent: '19.00', tax_amount: '285000.00', taxable_amount: '1500000.00' },
      ],
    },
  ],
  tax_totals: [
    {
      tax_id: 1,
      tax_amount: '285000.00',
      tax_subtotals: [
        { tax_id: 1, percent: '19.00', tax_amount: '285000.00', taxable_amount: '1500000.00' },
      ],
    },
  ],
  legal_monetary_totals: {
    line_extension_amount: '1500000.00',
    tax_exclusive_amount: '1500000.00',
    tax_inclusive_amount: '1785000.00',
    allowance_total_amount: '0.00',
    charge_total_amount: '0.00',
    pre_paid_amount: '0.00',
    payable_amount: '1785000.00',
  },
};

function model(overrides: Partial<InvoiceModel> = {}): InvoiceModel {
  return {
    paymentFormId: '1',
    paymentMethodId: '10',
    paymentDueDate: '',
    durationDays: null,
    notes: '',
    lines: [
      {
        ...emptyLine(),
        code: 'SERV01',
        description: 'Servicio de consultoría',
        quantity: 1,
        unitPrice: 1500000,
      },
    ],
    ...overrides,
  };
}

describe('invoice form mapping', () => {
  it('asks the server to calculate only complete lines, with their tax', () => {
    expect(buildCalculationRequest(model())).toEqual({
      lines: [
        {
          quantity: 1,
          unit_price: 1500000,
          free_of_charge: false,
          taxes: [{ tax_id: 1, percent: 19 }],
        },
      ],
    });
  });

  it('does not calculate without lines or with an incomplete line', () => {
    expect(buildCalculationRequest(model({ lines: [] }))).toBeNull();
    expect(
      buildCalculationRequest(model({ lines: [{ ...emptyLine(), quantity: 0, unitPrice: 10 }] })),
    ).toBeNull();
    expect(
      buildCalculationRequest(model({ lines: [{ ...emptyLine(), quantity: 1, unitPrice: null }] })),
    ).toBeNull();
  });

  it('fills a line from a product of the catalogue', () => {
    expect(
      lineFromProduct({
        id: 'p-1',
        code: 'SERV01',
        name: 'Servicio de consultoría',
        unit_price: '1500000.00',
        unit_measure_id: 70,
        type_item_identification_id: 4,
        tax_id: 1,
        tax_percent: '5.00',
      }),
    ).toEqual({
      productId: 'p-1',
      code: 'SERV01',
      description: 'Servicio de consultoría',
      quantity: 1,
      unitPrice: 1500000,
      unitMeasureId: 70,
      typeItemIdentificationId: 4,
      taxId: 1,
      taxPercent: '5',
    });
  });

  it('builds the invoice body with the values the server calculated', () => {
    const body = buildInvoiceBody(model({ notes: 'Gracias' }), CUSTOMER, CALCULATION);

    expect(body).toEqual({
      type_document_id: 1,
      notes: 'Gracias',
      customer: {
        identification_number: '800000001',
        dv: '1',
        type_document_identification_id: 6,
        type_organization_id: 1,
        type_regime_id: 1,
        type_liability_id: 117,
        name: 'CLIENTE DE PRUEBA GOLDEN SAS',
        phone: '3000000001',
        address: 'CARRERA 2 # 2-02',
        email: 'cliente.golden@example.com',
        municipality_id: 149,
      },
      payment_form: [{ payment_form_id: 1, payment_method_id: 10 }],
      legal_monetary_totals: CALCULATION.legal_monetary_totals,
      tax_totals: [
        { tax_id: 1, percent: '19.00', tax_amount: '285000.00', taxable_amount: '1500000.00' },
      ],
      invoice_lines: [
        {
          unit_measure_id: 70,
          invoiced_quantity: 1,
          base_quantity: 1,
          line_extension_amount: '1500000.00',
          free_of_charge_indicator: false,
          description: 'Servicio de consultoría',
          code: 'SERV01',
          type_item_identification_id: 4,
          price_amount: 1500000,
          tax_totals: [
            { tax_id: 1, percent: '19.00', tax_amount: '285000.00', taxable_amount: '1500000.00' },
          ],
        },
      ],
    });
  });

  it('sends the due date and term of a credit sale', () => {
    const body = buildInvoiceBody(
      model({
        paymentFormId: '2',
        paymentMethodId: '47',
        paymentDueDate: '2026-12-14',
        durationDays: 30,
      }),
      CUSTOMER,
      CALCULATION,
    );

    expect(body.payment_form).toEqual([
      {
        payment_form_id: 2,
        payment_method_id: 47,
        payment_due_date: '2026-12-14',
        duration_measure: 30,
      },
    ]);
  });
});
