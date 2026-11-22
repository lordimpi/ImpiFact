import { IDEMPOTENCY_KEY } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderDocumentsRoute } from '../testing/documents-testing';

type Http = Awaited<ReturnType<typeof renderDocumentsRoute>>['http'];

const SUBTOTAL = { tax_id: 1, percent: '0.00', tax_amount: '0.00', taxable_amount: '200.00' };
const CALCULATION = {
  allowance_charges: [],
  lines: [{ allowance_charges: [], line_extension_amount: '200.00', tax_subtotals: [SUBTOTAL] }],
  tax_totals: [{ tax_id: 1, tax_amount: '0.00', tax_subtotals: [SUBTOTAL] }],
  legal_monetary_totals: {
    line_extension_amount: '200.00',
    tax_exclusive_amount: '200.00',
    tax_inclusive_amount: '200.00',
    allowance_total_amount: '0.00',
    charge_total_amount: '0.00',
    pre_paid_amount: '0.00',
    payable_amount: '200.00',
  },
};

/** El flujo completo recorre muchas búsquedas por rol en jsdom; con la suite en paralelo pasa de 5 s. */
const LONG_FLOW_MS = 20_000;

const isCalculate = (r: { url: string }) => r.url === '/api/v1/documents/calculate';
const ISSUE_URL = '/api/v1/documents/support-documents';

function answerCatalog(http: Http, name: string, items: object[]) {
  return waitFor(() =>
    http
      .expectOne(`/api/v1/catalogs/${name}`)
      .flush({ name, version: 'v1', next_cursor: null, items }),
  );
}

const answerIdentificationTypes = (http: Http) =>
  answerCatalog(http, 'identification-types', [
    { id: 3, code: '13', name: 'Cédula de ciudadanía' },
    { id: 5, code: '22', name: 'Cédula de extranjería' },
    { id: 6, code: '31', name: 'NIT' },
    { id: 7, code: '41', name: 'Pasaporte' },
  ]);

/** Opens the Optimus select named `label` and clicks its option `option`. */
async function choose(label: RegExp, option: string): Promise<void> {
  fireEvent.click(screen.getByRole('combobox', { name: label }));
  fireEvent.click(await screen.findByRole('option', { name: option }));
}

const type = (label: RegExp, value: string, scope: HTMLElement = document.body) =>
  fireEvent.input(within(scope).getByLabelText(label), { target: { value } });

async function fillSeller(): Promise<void> {
  await choose(/^Tipo de identificación/, 'NIT');
  type(/^Número de identificación/, '800000003');
  type(/^Dígito de verificación/, '4');
  type(/^Nombre o razón social/, 'PROVEEDOR NO OBLIGADO');
  type(/^Dirección/, 'CALLE 3 # 3-03');
  type(/^Teléfono/, '3000000003');
  type(/^Correo/, 'proveedor@example.com');
  type(/^Matrícula mercantil/, '0000-00');
  type(/^Código postal/, '630001');
}

async function fillLine(): Promise<void> {
  const line = await screen.findByRole('group', { name: 'Línea 1' });
  type(/Código/, 'SERV01', line);
  type(/Descripción/, 'SERVICIO DE EJEMPLO', line);
  type(/Cantidad/, '2', line);
  type(/Precio unitario/, '100', line);
}

describe('SupportDocumentIssuePage', () => {
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

  beforeEach(() => sessionStorage.clear());

  it(
    'issues a support document to a resident seller with the server totals and an Idempotency-Key',
    async () => {
      const { http, router } = await renderDocumentsRoute('/documentos/documento-soporte');
      expect(
        await screen.findByRole('heading', { level: 1, name: 'Emitir documento soporte' }),
      ).toBeTruthy();
      await answerIdentificationTypes(http);
      expect(screen.getByRole('heading', { name: 'Proveedor (sujeto no obligado)' })).toBeTruthy();

      // Only the identification types the API accepts for a seller (4, 5, 6, 7, 8, 9 and 11).
      fireEvent.click(screen.getByRole('combobox', { name: /^Tipo de identificación/ }));
      const options = (await screen.findAllByRole('option')).map((o) => o.textContent?.trim());
      expect(options).toEqual(['Cédula de extranjería', 'NIT', 'Pasaporte']);
      fireEvent.keyDown(screen.getByRole('combobox', { name: /^Tipo de identificación/ }), {
        key: 'Escape',
      });

      await fillSeller();
      await fillLine();
      expect(
        screen.getByText('Se enviará a proveedor@example.com, con el PDF y el XML.'),
      ).toBeTruthy();

      const calculate = await waitFor(() => http.expectOne(isCalculate));
      expect(calculate.request.body).toEqual({
        lines: [
          {
            quantity: 2,
            unit_price: 100,
            free_of_charge: false,
            taxes: [{ tax_id: 1, percent: 0 }],
          },
        ],
      });
      calculate.flush(CALCULATION);
      expect((await screen.findByTestId('payable')).textContent).toMatch(/200,00/);
      // No preview: the API has no preview of the support document.
      expect(screen.queryByRole('button', { name: 'Vista previa' })).toBeNull();

      fireEvent.click(screen.getByRole('button', { name: 'Emitir documento soporte' }));
      const issue = await waitFor(() => http.expectOne(ISSUE_URL));
      expect(issue.request.context.get(IDEMPOTENCY_KEY)).toMatch(/^[0-9a-f-]{36}$/);
      const body = issue.request.body;
      expect(body.type_document_id).toBe(11);
      expect(body.type_operation_id).toBe(23);
      expect(body.customer).toBeUndefined();
      expect(body.seller).toEqual({
        type_document_identification_id: 6,
        identification_number: '800000003',
        dv: '4',
        name: 'PROVEEDOR NO OBLIGADO',
        address: 'CALLE 3 # 3-03',
        phone: '3000000003',
        email: 'proveedor@example.com',
        merchant_registration: '0000-00',
        postal_zone_code: '630001',
      });
      expect(body.payment_form).toEqual([{ payment_form_id: 1, payment_method_id: 10 }]);
      // The email goes to the seller once the DIAN accepts the document (box checked by default).
      expect(body.sendmail).toBe(true);
      expect(body.legal_monetary_totals).toEqual(CALCULATION.legal_monetary_totals);
      expect(body.tax_totals).toEqual([SUBTOTAL]);
      expect(body.invoice_lines).toEqual([
        expect.objectContaining({
          code: 'SERV01',
          description: 'SERVICIO DE EJEMPLO',
          invoiced_quantity: 2,
          price_amount: 100,
          line_extension_amount: '200.00',
          tax_totals: [SUBTOTAL],
          type_generation_transmition_id: 1,
          start_date: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
        }),
      ]);
      issue.flush({
        message: 'ok',
        cuds: 'cuds-1',
        document: { id: 'ds-9', full_number: 'SEDS984000009', status: 'Queued' },
      });

      await waitFor(() => expect(router.url).toBe('/documentos/ds-9'));
    },
    LONG_FLOW_MS,
  );

  it(
    'sends the country, state and city of a non-resident seller',
    async () => {
      const { http } = await renderDocumentsRoute('/documentos/documento-soporte');
      await screen.findByRole('heading', { level: 1, name: 'Emitir documento soporte' });
      await answerIdentificationTypes(http);
      expect(screen.queryByRole('combobox', { name: /^País/ })).toBeNull();

      await choose(/^Operación/, 'No residente');
      await answerCatalog(http, 'countries', [
        { id: 46, code: 'CO', name: 'Colombia' },
        { id: 249, code: 'US', name: 'Estados Unidos' },
      ]);
      await choose(/^País/, 'Estados Unidos');
      type(/^Departamento o estado/, 'Florida');
      type(/^Ciudad/, 'Miami');
      await fillSeller();
      await fillLine();
      (await waitFor(() => http.expectOne(isCalculate))).flush(CALCULATION);
      await screen.findByTestId('payable');

      fireEvent.click(screen.getByRole('button', { name: 'Emitir documento soporte' }));
      const issue = await waitFor(() => http.expectOne(ISSUE_URL));
      expect(issue.request.body.type_operation_id).toBe(24);
      expect(issue.request.body.seller).toEqual(
        expect.objectContaining({
          country_id: 249,
          state_name: 'Florida',
          municipality_name: 'Miami',
        }),
      );
    },
    LONG_FLOW_MS,
  );

  it(
    'checks the seller before calling the API and shows the API validation errors',
    async () => {
      const { http } = await renderDocumentsRoute('/documentos/documento-soporte');
      await screen.findByRole('heading', { level: 1, name: 'Emitir documento soporte' });
      await answerIdentificationTypes(http);
      await fillLine();
      (await waitFor(() => http.expectOne(isCalculate))).flush(CALCULATION);
      await screen.findByTestId('payable');

      // Without the seller the button stays disabled and the page says why.
      expect(
        (screen.getByRole('button', { name: 'Emitir documento soporte' }) as HTMLButtonElement)
          .disabled,
      ).toBe(true);
      expect(screen.getByText(/Para emitir complete los datos del proveedor/)).toBeTruthy();

      await fillSeller();
      type(/^Código postal/, '63-0001');
      fireEvent.blur(screen.getByLabelText(/^Código postal/));
      expect(await screen.findByText('El código postal solo lleva dígitos.')).toBeTruthy();
      type(/^Código postal/, '630001');

      fireEvent.click(screen.getByRole('button', { name: 'Emitir documento soporte' }));
      (await waitFor(() => http.expectOne(ISSUE_URL))).flush(
        {
          status: 422,
          code: 'software.missing',
          message: 'La empresa no tiene software de documento soporte.',
          errors: { software: ['Configure el software de documento soporte.'] },
        },
        { status: 422, statusText: 'Unprocessable Entity' },
      );

      const alert = await screen.findByRole('alert');
      expect(alert.textContent).toContain('La empresa no tiene software de documento soporte.');
      expect(alert.textContent).toContain('Configure el software de documento soporte.');
    },
    LONG_FLOW_MS,
  );
});
