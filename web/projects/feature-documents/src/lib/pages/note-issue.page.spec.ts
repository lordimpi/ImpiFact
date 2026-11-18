import { IDEMPOTENCY_KEY } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderDocumentsRoute } from '../testing/documents-testing';

type Http = Awaited<ReturnType<typeof renderDocumentsRoute>>['http'];

const CUFE = 'a'.repeat(96);

const CUSTOMER = {
  identification_number: '800000001',
  dv: '1',
  type_document_identification_id: 6,
  name: 'CLIENTE DE PRUEBA GOLDEN SAS',
  email: 'cliente.golden@example.com',
};

const SUMMARY = {
  id: 'doc-1',
  kind: 'invoice',
  full_number: 'SETP990000001',
  issue_date: '2026-11-10',
  status: 'Accepted',
  cufe: CUFE,
  customer: CUSTOMER,
  totals: { payable_amount: '1785000.00' },
};

const DETAIL = {
  ...SUMMARY,
  document_type_id: 1,
  lines: [
    {
      line_number: 1,
      code: 'SERV01',
      description: 'Servicio de consultoría',
      invoiced_quantity: '2.000000',
      price_amount: '750000.00',
      unit_measure_id: 70,
      type_item_identification_id: 4,
      line_extension_amount: '1500000.00',
      tax_totals: [
        { tax_id: 1, percent: '19.00', tax_amount: '285000.00', taxable_amount: '1500000.00' },
      ],
    },
  ],
};

const SUBTOTAL = {
  tax_id: 1,
  percent: '19.00',
  tax_amount: '285000.00',
  taxable_amount: '1500000.00',
};
const CALCULATION = {
  allowance_charges: [],
  lines: [
    { allowance_charges: [], line_extension_amount: '1500000.00', tax_subtotals: [SUBTOTAL] },
  ],
  tax_totals: [{ tax_id: 1, tax_amount: '285000.00', tax_subtotals: [SUBTOTAL] }],
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

const ISSUED = {
  message: 'ok',
  QRStr: '',
  urlinvoiceattached: '',
  urlinvoicepdf: '',
  urlinvoicexml: '',
  document: { id: 'note-9', full_number: 'NC1', status: 'Queued' },
};

/** El flujo completo recorre muchas búsquedas por rol en jsdom; con la suite en paralelo pasa de 5 s. */
const LONG_FLOW_MS = 15_000;

const isCalculate = (r: { url: string }) => r.url === '/api/v1/documents/calculate';

function answerConcepts(http: Http, kind: 'credit' | 'debit') {
  const concepts =
    kind === 'credit'
      ? [
          { id: 1, code: '1', name: 'Devolución parcial de los bienes' },
          { id: 2, code: '2', name: 'Anulación de factura electrónica' },
        ]
      : [
          { id: 1, code: '1', name: 'Intereses' },
          { id: 2, code: '2', name: 'Gastos por cobrar' },
        ];
  return waitFor(() =>
    http
      .expectOne(`/api/v1/catalogs/${kind}-note-discrepancy-responses`)
      .flush({ name: 'concepts', version: 'v1', next_cursor: null, items: concepts }),
  );
}

const choose = (label: RegExp, value: string) => {
  const select = screen.getByLabelText(label) as HTMLSelectElement;
  fireEvent.change(select, { target: { value } });
  fireEvent.input(select, { target: { value } });
};

describe('NoteIssuePage', () => {
  beforeEach(() => sessionStorage.clear());

  it(
    'issues a credit note for an accepted invoice with the lines of the invoice and the server totals',
    async () => {
      const { http, router } = await renderDocumentsRoute('/documentos/nota-credito');
      expect(
        await screen.findByRole('heading', { level: 1, name: 'Emitir nota crédito' }),
      ).toBeTruthy();
      await answerConcepts(http, 'credit');
      // Las notas aún no tienen vista previa en el Api.
      expect(screen.queryByRole('button', { name: 'Vista previa' })).toBeNull();

      fireEvent.input(screen.getByLabelText(/Buscar factura/), { target: { value: '990000001' } });
      const search = await waitFor(() => http.expectOne((r) => r.url === '/api/v1/documents'));
      expect(search.request.params.get('kind')).toBe('invoice');
      expect(search.request.params.getAll('status')).toEqual(['Accepted', 'AcceptedWithWarnings']);
      expect(search.request.params.get('number')).toBe('990000001');
      search.flush({ items: [SUMMARY], next_cursor: null });
      fireEvent.click(await screen.findByRole('button', { name: /Elegir SETP990000001/ }));

      (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush(DETAIL);
      const line = await screen.findByRole('group', { name: 'Línea 1' });
      expect((within(line).getByLabelText(/Código/) as HTMLInputElement).value).toBe('SERV01');
      expect((within(line).getByLabelText(/Cantidad/) as HTMLInputElement).value).toBe('2');

      const calculate = await waitFor(() => http.expectOne(isCalculate));
      expect(calculate.request.body).toEqual({
        lines: [
          {
            quantity: 2,
            unit_price: 750000,
            free_of_charge: false,
            taxes: [{ tax_id: 1, percent: 19 }],
          },
        ],
      });
      calculate.flush(CALCULATION);
      expect((await screen.findByTestId('payable')).textContent).toMatch(/1\.785\.000,00/);

      const submit = screen.getByRole('button', {
        name: 'Emitir nota crédito',
      }) as HTMLButtonElement;
      expect(submit.disabled).toBe(true);
      choose(/^Concepto de la corrección/, '2');
      await waitFor(() => expect(submit.disabled).toBe(false));
      fireEvent.click(submit);

      const issue = await waitFor(() => http.expectOne('/api/v1/documents/credit-notes'));
      expect(issue.request.context.get(IDEMPOTENCY_KEY)).toMatch(/^[0-9a-f-]{36}$/);
      expect(issue.request.body).toMatchObject({
        type_document_id: 4,
        discrepancyresponsecode: 2,
        billing_reference: { number: 'SETP990000001', uuid: CUFE, issue_date: '2026-11-10' },
        customer: { identification_number: '800000001', dv: '1', name: CUSTOMER.name },
        legal_monetary_totals: CALCULATION.legal_monetary_totals,
        tax_totals: [SUBTOTAL],
      });
      expect(issue.request.body.credit_note_lines[0]).toMatchObject({
        code: 'SERV01',
        invoiced_quantity: 2,
        line_extension_amount: '1500000.00',
        tax_totals: [SUBTOTAL],
      });
      expect(issue.request.body.requested_monetary_totals).toBeUndefined();
      issue.flush(ISSUED);

      await waitFor(() => expect(router.url).toBe('/documentos/note-9'));
    },
    LONG_FLOW_MS,
  );

  it('issues a debit note for the invoice in the link and shows why the API rejects it', async () => {
    const { http } = await renderDocumentsRoute('/documentos/nota-debito?factura=doc-1');
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Emitir nota débito' }),
    ).toBeTruthy();
    await answerConcepts(http, 'debit');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush(DETAIL);
    expect(await screen.findByText('SETP990000001')).toBeTruthy();

    // La débito empieza con una línea libre (intereses, gastos); las de la factura se copian con un botón.
    const line = await screen.findByRole('group', { name: 'Línea 1' });
    expect((within(line).getByLabelText(/Código/) as HTMLInputElement).value).toBe('');
    fireEvent.click(screen.getByRole('button', { name: 'Copiar las líneas de la factura' }));
    await waitFor(() =>
      expect((within(line).getByLabelText(/Código/) as HTMLInputElement).value).toBe('SERV01'),
    );
    (await waitFor(() => http.expectOne(isCalculate))).flush(CALCULATION);
    await screen.findByTestId('payable');
    choose(/^Concepto de la corrección/, '1');

    const submit = screen.getByRole('button', { name: 'Emitir nota débito' }) as HTMLButtonElement;
    await waitFor(() => expect(submit.disabled).toBe(false));
    fireEvent.click(submit);
    const issue = await waitFor(() => http.expectOne('/api/v1/documents/debit-notes'));
    expect(issue.request.body).toMatchObject({
      type_document_id: 5,
      discrepancyresponsecode: 1,
      requested_monetary_totals: CALCULATION.legal_monetary_totals,
    });
    expect(issue.request.body.debit_note_lines).toHaveLength(1);
    expect(issue.request.body.legal_monetary_totals).toBeUndefined();
    issue.flush(
      {
        status: 422,
        code: 'document.reference_not_accepted',
        message: 'La factura referenciada no está aceptada por la DIAN.',
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('La factura referenciada no está aceptada por la DIAN.');
  });

  it('warns when the invoice of the link is not accepted yet', async () => {
    const { http } = await renderDocumentsRoute('/documentos/nota-credito?factura=doc-1');
    await answerConcepts(http, 'credit');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush({
      ...DETAIL,
      status: 'Sent',
    });

    expect(await screen.findByText(/La DIAN todavía no aceptó esta factura/)).toBeTruthy();
  });

  it('does not accept a document that is not an invoice', async () => {
    const { http } = await renderDocumentsRoute('/documentos/nota-credito?factura=doc-1');
    await answerConcepts(http, 'credit');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush({
      ...DETAIL,
      kind: 'credit_note',
    });

    expect(await screen.findByText(/Solo se puede corregir una factura/)).toBeTruthy();
    expect(screen.getByLabelText(/Buscar factura/)).toBeTruthy();
  });

  it('is not reachable without the documents:write scope', async () => {
    const { router } = await renderDocumentsRoute('/documentos/nota-debito', {
      scopes: ['documents:read'],
    });

    await waitFor(() => expect(router.url).toBe('/'));
  });
});
