import { IDEMPOTENCY_KEY } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderDocumentsRoute } from '../testing/documents-testing';

const CUSTOMER = {
  id: 'cus-1',
  identification_number: '800000001',
  dv: '1',
  type_document_identification_id: 6,
  name: 'CLIENTE DE PRUEBA GOLDEN SAS',
  email: 'cliente.golden@example.com',
};

const PRODUCT = {
  id: 'p-1',
  code: 'SERV01',
  name: 'Servicio de consultoría',
  unit_price: '1500000.00',
  unit_measure_id: 70,
  type_item_identification_id: 4,
  tax_id: 1,
  tax_percent: '19.00',
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

/** El flujo completo recorre muchas búsquedas por rol en jsdom; con la suite en paralelo pasa de 5 s. */
const LONG_FLOW_MS = 15_000;

const isCalculate = (r: { url: string }) => r.url === '/api/v1/documents/calculate';

async function chooseCustomer(http: Awaited<ReturnType<typeof renderDocumentsRoute>>['http']) {
  fireEvent.input(await screen.findByLabelText(/Buscar cliente/), { target: { value: 'golden' } });
  const search = await waitFor(() => http.expectOne((r) => r.url === '/api/v1/customers'));
  expect(search.request.params.get('search')).toBe('golden');
  search.flush({ items: [CUSTOMER], next_cursor: null });
  fireEvent.click(
    await screen.findByRole('button', { name: /Elegir CLIENTE DE PRUEBA GOLDEN SAS/ }),
  );
}

async function addProduct(http: Awaited<ReturnType<typeof renderDocumentsRoute>>['http']) {
  fireEvent.input(screen.getByLabelText(/Agregar un producto/), { target: { value: 'serv' } });
  const search = await waitFor(() => http.expectOne((r) => r.url === '/api/v1/products'));
  expect(search.request.params.get('search')).toBe('serv');
  search.flush({ items: [PRODUCT], next_cursor: null });
  fireEvent.click(await screen.findByRole('button', { name: /Agregar Servicio de consultoría/ }));
}

/** Opens the Optimus select named `label` and clicks its option `option`. */
async function choose(label: RegExp, option: string): Promise<void> {
  fireEvent.click(screen.getByRole('combobox', { name: label }));
  fireEvent.click(await screen.findByRole('option', { name: option }));
}

describe('InvoiceIssuePage', () => {
  // jsdom has no matchMedia nor scrollIntoView; the Optimus select and date picker overlays use them.
  beforeAll(() => {
    window.matchMedia ??= ((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    })) as typeof window.matchMedia;
    Element.prototype.scrollIntoView ??= () => undefined;
  });

  it(
    'takes the totals from the server and issues with them and an Idempotency-Key',
    async () => {
      const { http, router } = await renderDocumentsRoute('/documentos/nuevo');
      expect(await screen.findByRole('heading', { level: 1, name: 'Emitir factura' })).toBeTruthy();
      expect(
        screen.getByRole('link', { name: 'Crear un cliente nuevo' }).getAttribute('href'),
      ).toBe('/clientes/nuevo');

      await chooseCustomer(http);
      expect(screen.getByText('Cliente elegido')).toBeTruthy();
      await addProduct(http);

      const calculate = await waitFor(() => http.expectOne(isCalculate));
      expect(calculate.request.body).toEqual({
        lines: [
          {
            quantity: 1,
            unit_price: 1500000,
            free_of_charge: false,
            taxes: [{ tax_id: 1, percent: 19 }],
          },
        ],
      });
      calculate.flush(CALCULATION);

      const payable = await screen.findByTestId('payable');
      expect(payable.textContent).toMatch(/1\.785\.000,00/);

      fireEvent.click(screen.getByRole('button', { name: 'Emitir factura' }));
      const issue = await waitFor(() => http.expectOne('/api/v1/documents/invoices'));
      const key = issue.request.context.get(IDEMPOTENCY_KEY);
      expect(key).toMatch(/^[0-9a-f-]{36}$/);
      expect(issue.request.body.legal_monetary_totals).toEqual(CALCULATION.legal_monetary_totals);
      expect(issue.request.body.invoice_lines[0].line_extension_amount).toBe('1500000.00');
      expect(issue.request.body.invoice_lines[0].tax_totals).toEqual([SUBTOTAL]);
      expect(issue.request.body.tax_totals).toEqual([SUBTOTAL]);
      expect(issue.request.body.customer.identification_number).toBe('800000001');
      issue.flush({
        message: 'ok',
        QRStr: '',
        urlinvoiceattached: '',
        urlinvoicepdf: '',
        urlinvoicexml: '',
        document: { id: 'doc-9', full_number: 'SETP990000009', status: 'Queued' },
      });

      await waitFor(() => expect(router.url).toBe('/documentos/doc-9'));
    },
    LONG_FLOW_MS,
  );

  it(
    'issues on credit with the payment method, the due date of the date picker and the duration',
    async () => {
      const { http } = await renderDocumentsRoute('/documentos/nuevo');
      await chooseCustomer(http);
      await addProduct(http);
      (await waitFor(() => http.expectOne(isCalculate))).flush(CALCULATION);
      await screen.findByTestId('payable');

      expect(screen.getByRole('combobox', { name: /^Forma de pago/ }).textContent?.trim()).toBe(
        'Contado',
      );
      await choose(/^Forma de pago/, 'Crédito');
      await choose(/^Medio de pago/, 'Transferencia débito bancaria');
      const dueDate = await screen.findByRole('combobox', { name: /^Fecha de vencimiento/ });
      expect(dueDate.getAttribute('placeholder')).toBe('dd/mm/aaaa');
      fireEvent.keyDown(dueDate, { key: '1' });
      fireEvent.input(dueDate, { target: { value: '15/12/2026' } });
      fireEvent.input(screen.getByLabelText(/Plazo en días/), { target: { value: '30' } });
      http.match(isCalculate).forEach((request) => request.flush(CALCULATION));

      const submit = screen.getByRole('button', { name: 'Emitir factura' }) as HTMLButtonElement;
      await waitFor(() => expect(submit.disabled).toBe(false));
      fireEvent.click(submit);
      const issue = await waitFor(() => http.expectOne('/api/v1/documents/invoices'));
      expect(issue.request.body.payment_form).toEqual([
        {
          payment_form_id: 2,
          payment_method_id: 47,
          payment_due_date: '2026-12-15',
          duration_measure: 30,
        },
      ]);
    },
    LONG_FLOW_MS,
  );

  it('recalculates when a line changes and blocks the issue until the totals are current', async () => {
    const { http } = await renderDocumentsRoute('/documentos/nuevo');
    await chooseCustomer(http);
    await addProduct(http);
    (await waitFor(() => http.expectOne(isCalculate))).flush(CALCULATION);
    await screen.findByTestId('payable');

    const line = await screen.findByRole('group', { name: 'Línea 1' });
    fireEvent.input(within(line).getByLabelText(/Cantidad/), { target: { value: '2' } });

    expect(
      (screen.getByRole('button', { name: 'Emitir factura' }) as HTMLButtonElement).disabled,
    ).toBe(true);
    const again = await waitFor(() => http.expectOne(isCalculate));
    expect(again.request.body.lines[0].quantity).toBe(2);
    again.flush(
      { code: 'validation.failed', errors: { 'lines[0].quantity': ['Mal'] } },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    expect(await screen.findByText(/No se pudieron calcular los totales/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Calcular de nuevo' }));
    (await waitFor(() => http.expectOne(isCalculate))).flush(CALCULATION);
    await screen.findByTestId('payable');
  });

  it('does not issue without a customer and explains why', async () => {
    const { http } = await renderDocumentsRoute('/documentos/nuevo');
    await screen.findByRole('heading', { name: 'Emitir factura' });
    fireEvent.click(screen.getByRole('button', { name: 'Agregar línea libre' }));
    const line = await screen.findByRole('group', { name: 'Línea 1' });
    fireEvent.input(within(line).getByLabelText(/Código/), { target: { value: 'X1' } });
    fireEvent.input(within(line).getByLabelText(/Descripción/), { target: { value: 'Libre' } });
    fireEvent.input(within(line).getByLabelText(/Precio unitario/), { target: { value: '100' } });
    (await waitFor(() => http.expectOne(isCalculate))).flush(CALCULATION);
    await screen.findByTestId('payable');

    expect(
      (screen.getByRole('button', { name: 'Emitir factura' }) as HTMLButtonElement).disabled,
    ).toBe(true);
    expect(screen.getByText(/Para emitir elija el cliente/)).toBeTruthy();
    http.expectNone('/api/v1/documents/invoices');
  });

  describe('preview', () => {
    beforeEach(() => {
      vi.spyOn(window, 'open');
      URL.createObjectURL = vi.fn(() => 'blob:preview');
      URL.revokeObjectURL = vi.fn();
    });

    afterEach(() => vi.restoreAllMocks());

    async function readyToIssue() {
      const rendered = await renderDocumentsRoute('/documentos/nuevo');
      await chooseCustomer(rendered.http);
      await addProduct(rendered.http);
      (await waitFor(() => rendered.http.expectOne(isCalculate))).flush(CALCULATION);
      await screen.findByTestId('payable');
      return rendered;
    }

    it('shows the PDF of the current invoice in a dialog without issuing it', async () => {
      const { http } = await readyToIssue();

      fireEvent.click(screen.getByRole('button', { name: 'Vista previa' }));
      const preview = await waitFor(() => http.expectOne('/api/v1/documents/invoices/preview'));
      expect(preview.request.method).toBe('POST');
      expect(preview.request.responseType).toBe('blob');
      expect(preview.request.body.legal_monetary_totals).toEqual(CALCULATION.legal_monetary_totals);
      expect(preview.request.body.invoice_lines[0].line_extension_amount).toBe('1500000.00');
      preview.flush(new Blob(['%PDF-1.7'], { type: 'application/pdf' }));

      // El PDF se muestra dentro del panel, sin ventanas emergentes que el navegador pueda bloquear.
      const dialog = await screen.findByRole('dialog', { name: 'Vista previa del PDF' });
      expect(dialog.querySelector('iframe')?.getAttribute('src')).toBe('blob:preview');
      expect(window.open).not.toHaveBeenCalled();
      expect(screen.getByRole('button', { name: 'Descargar PDF' })).toBeTruthy();
      http.expectNone('/api/v1/documents/invoices');

      fireEvent.click(screen.getByRole('button', { name: 'Cerrar la vista previa' }));
      await waitFor(() => expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview'));
    });

    it('explains a 503 when the PDF service is not available', async () => {
      const { http } = await readyToIssue();

      fireEvent.click(screen.getByRole('button', { name: 'Vista previa' }));
      const preview = await waitFor(() => http.expectOne('/api/v1/documents/invoices/preview'));
      preview.flush(
        new Blob(
          [JSON.stringify({ status: 503, code: 'pdf.unavailable', message: 'No responde.' })],
          { type: 'application/problem+json' },
        ),
        { status: 503, statusText: 'Service Unavailable' },
      );

      expect(
        await screen.findByText(
          'El servicio que genera el PDF no está disponible en este momento. Intente de nuevo en unos minutos.',
        ),
      ).toBeTruthy();
      expect(screen.queryByRole('dialog', { name: 'Vista previa del PDF' })).toBeNull();
    });

    it('is disabled until the invoice can be issued', async () => {
      await renderDocumentsRoute('/documentos/nuevo');
      const button = (await screen.findByRole('button', {
        name: 'Vista previa',
      })) as HTMLButtonElement;
      expect(button.disabled).toBe(true);
    });
  });

  it('is not reachable without the documents:write scope', async () => {
    const { router } = await renderDocumentsRoute('/documentos/nuevo', {
      scopes: ['documents:read'],
    });

    await waitFor(() => expect(router.url).toBe('/'));
  });
});
