import { TestBed } from '@angular/core/testing';
import { IDEMPOTENCY_KEY } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { CompanySetupStore } from '../state/company-setup.store';
import {
  CERTIFICATE,
  COMPANY_PATH,
  EMPTY_READINESS,
  ENVIRONMENTS,
  INVOICE_SOFTWARE,
  expectRequest,
  problem,
  renderCompaniesRoute,
  respond,
} from '../testing/companies-testing';

const STATUS_URL = `${COMPANY_PATH}/setup-status`;
const TEST_SET = '7f8a1c2e-1111-4222-8333-944455556666';

/** Empresa con todo listo para el set de pruebas, sin resultado de la DIAN todavía. */
const READY = {
  ...EMPTY_READINESS,
  company_complete: true,
  certificate_valid: true,
  software_configured: true,
  test_set_id_configured: true,
  resolutions: EMPTY_READINESS.resolutions.map((entry) => ({ ...entry, valid: true })),
};

interface OpenOptions {
  readiness?: object;
  certificates?: object[];
  software?: object[];
}

async function open(options: OpenOptions = {}) {
  const rendered = await renderCompaniesRoute('/empresas/company-1/produccion');
  await screen.findByRole('heading', { level: 2, name: 'Paso a producción' });
  const { http } = rendered;
  await respond(
    http,
    STATUS_URL,
    options.readiness ?? { ...EMPTY_READINESS, certificate_valid: true },
  );
  await respond(http, `${COMPANY_PATH}/certificates`, {
    success: true,
    certificates: options.certificates ?? [CERTIFICATE],
  });
  await respond(http, `${COMPANY_PATH}/software`, { software: options.software ?? [] });
  await screen.findByRole('list', { name: 'Pasos del paso a producción' });
  return rendered;
}

const step = (title: string) => {
  const list = screen.getByRole('list', { name: 'Pasos del paso a producción' });
  return within(list)
    .getAllByRole('listitem')
    .find((item) => within(item).queryByRole('heading', { name: title }))!;
};
const badge = (title: string) => step(title).querySelector('.companies-badge')?.textContent?.trim();
const sendButton = () =>
  within(step('Set de pruebas de la DIAN')).getByRole('button', {
    name: 'Enviar factura de prueba',
  }) as HTMLButtonElement;

describe('ProductionWizardPage', () => {
  it('derives each step from the setup status the API reports', async () => {
    await open();

    expect(badge('Datos de la empresa')).toBe('Pendiente');
    expect(
      within(step('Datos de la empresa')).getByText(/Faltan datos obligatorios de la empresa/),
    ).toBeTruthy();
    expect(
      within(step('Datos de la empresa'))
        .getByRole('link', { name: 'Ir a Datos' })
        .getAttribute('href'),
    ).toBe('/empresas/company-1/datos');
    expect(badge('Certificado digital')).toBe('Completo');
    expect(
      within(step('Certificado digital')).getByText(
        'Certificado activo de CN=ACME SAS, SERIALNUMBER=900123456, vence en 225 días.',
      ),
    ).toBeTruthy();
    expect(badge('Resolución de numeración')).toBe('Pendiente');
    expect(badge('Software y set de pruebas')).toBe('Pendiente');
    expect(badge('Set de pruebas de la DIAN')).toBe('Pendiente');
    expect(
      within(step('Set de pruebas de la DIAN')).getByText('Sin resultado de la DIAN todavía.'),
    ).toBeTruthy();
    expect(sendButton().disabled).toBe(true);
    expect(screen.queryByText('Sin verificar')).toBeNull();
  });

  it('marks the steps the API reports as complete and takes the saved test set', async () => {
    await open({ readiness: READY, software: [{ ...INVOICE_SOFTWARE, test_set_id: TEST_SET }] });

    expect(badge('Datos de la empresa')).toBe('Completo');
    expect(badge('Resolución de numeración')).toBe('Completo');
    expect(badge('Software y set de pruebas')).toBe('Completo');
    expect(
      within(step('Software y set de pruebas')).getByText(
        `Software soft-123 con el set de pruebas ${TEST_SET}.`,
      ),
    ).toBeTruthy();
    expect(sendButton().disabled).toBe(false);
  });

  it('tells whether the DIAN accepted or rejected the test set', async () => {
    const { http } = await open({ readiness: { ...READY, test_set_accepted: true } });
    expect(badge('Set de pruebas de la DIAN')).toBe('Completo');
    expect(
      within(step('Set de pruebas de la DIAN')).getByText('La DIAN aceptó el set de pruebas.'),
    ).toBeTruthy();

    const store = TestBed.inject(CompanySetupStore);
    fireEvent.click(screen.getByRole('button', { name: 'Volver a revisar' }));
    await respond(http, STATUS_URL, {
      ...READY,
      test_set_accepted: false,
    });
    await waitFor(() => expect(store.readiness()?.test_set_accepted).toBe(false));
    expect(badge('Set de pruebas de la DIAN')).toBe('Pendiente');
    expect(
      within(step('Set de pruebas de la DIAN')).getByText(/La DIAN rechazó el set de pruebas/),
    ).toBeTruthy();
  });

  it('locks the following steps while there is no valid certificate', async () => {
    await open({ readiness: EMPTY_READINESS, certificates: [] });

    expect(badge('Certificado digital')).toBe('Pendiente');
    expect(badge('Set de pruebas de la DIAN')).toBe('Bloqueado');
    expect(badge('Producción')).toBe('Bloqueado');
    expect(
      within(step('Producción')).queryByRole('button', { name: /Pasar a producción/ }),
    ).toBeNull();
  });

  it('explains when the setup status could not be read and retries', async () => {
    const { http } = await renderCompaniesRoute('/empresas/company-1/produccion');
    const error = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, STATUS_URL)).flush(error.body, error.options);

    expect(
      await screen.findByText('No se pudo consultar el estado del paso a producción.'),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await respond(http, STATUS_URL, READY);

    expect(await screen.findByRole('list', { name: 'Pasos del paso a producción' })).toBeTruthy();
  });

  it('enables the test invoice once a test set is written', async () => {
    await open();

    TestBed.inject(CompanySetupStore).setTestSetId(TEST_SET);

    await waitFor(() => expect(sendButton().disabled).toBe(false));
  });

  it('sends the test invoice, follows its DIAN status and reads the setup status again', async () => {
    const { http } = await open();
    const testSet = step('Software y set de pruebas');
    fireEvent.input(within(testSet).getByLabelText(/Identificador del set de pruebas/), {
      target: { value: TEST_SET },
    });

    fireEvent.click(sendButton());

    const calculation = await expectRequest(http, '/api/v1/documents/calculate');
    expect(calculation.request.body).toEqual({
      lines: [
        {
          quantity: 1,
          unit_price: 10000,
          free_of_charge: false,
          taxes: [{ tax_id: 1, percent: 19 }],
        },
      ],
    });
    const subtotal = {
      tax_id: 1,
      percent: '19.00',
      taxable_amount: '10000.00',
      tax_amount: '1900.00',
    };
    calculation.flush({
      allowance_charges: [],
      lines: [
        { allowance_charges: [], line_extension_amount: '10000.00', tax_subtotals: [subtotal] },
      ],
      tax_totals: [{ tax_id: 1, tax_amount: '1900.00', tax_subtotals: [subtotal] }],
      legal_monetary_totals: {
        line_extension_amount: '10000.00',
        tax_exclusive_amount: '10000.00',
        tax_inclusive_amount: '11900.00',
        payable_amount: '11900.00',
      },
    });

    const issue = await expectRequest(http, `/api/v1/documents/invoices/test-set/${TEST_SET}`);
    expect(issue.request.method).toBe('POST');
    const body = issue.request.body as Record<string, unknown>;
    expect(body['legal_monetary_totals']).toEqual(
      expect.objectContaining({ payable_amount: '11900.00' }),
    );
    expect(body['customer']).toEqual(
      expect.objectContaining({ identification_number: '222222222222' }),
    );
    // El interceptor de core convierte esta marca en el encabezado Idempotency-Key.
    expect(issue.request.context.get(IDEMPOTENCY_KEY)).toMatch(/^[0-9a-f-]{36}$/);
    issue.flush({
      message: 'Documento recibido.',
      QRStr: '',
      urlinvoiceattached: '',
      urlinvoicepdf: '',
      urlinvoicexml: '',
      document: { id: 'doc-1', full_number: 'SETP990000001', status: 'Queued' },
    });

    const detail = await expectRequest(http, '/api/v1/documents/doc-1');
    detail.flush({
      id: 'doc-1',
      full_number: 'SETP990000001',
      status: 'Accepted',
      dian_status_code: '00',
      dian_status_description: 'Procesado Correctamente.',
    });

    const result = within(step('Set de pruebas de la DIAN'));
    expect(await result.findByText('Factura SETP990000001: Aceptada')).toBeTruthy();
    expect(result.getByText('Respuesta de la DIAN: 00 Procesado Correctamente.')).toBeTruthy();

    await respond(http, STATUS_URL, { ...READY, test_set_accepted: true });
    await waitFor(() => expect(badge('Set de pruebas de la DIAN')).toBe('Completo'));
  });

  it('shows the DIAN rejection with its explanation', async () => {
    const { http } = await open();
    TestBed.inject(CompanySetupStore).setTestSetId(TEST_SET);
    await waitFor(() => expect(sendButton().disabled).toBe(false));
    fireEvent.click(sendButton());
    const error = problem(422, 'software.missing', 'La empresa no tiene software en habilitación.');
    (await expectRequest(http, '/api/v1/documents/calculate')).flush(error.body, error.options);

    expect(
      await within(step('Set de pruebas de la DIAN')).findByText(
        'La empresa no tiene software en habilitación.',
      ),
    ).toBeTruthy();
  });

  it('switches a family to production only after an explicit confirmation', async () => {
    const { http } = await open();

    const production = step('Producción');
    fireEvent.click(
      await within(production).findByRole('button', {
        name: 'Pasar a producción: Factura electrónica y notas',
      }),
    );
    const dialog = await screen.findByRole('dialog', { name: 'Confirmar el paso a producción' });
    expect(within(dialog).getByText(/efecto fiscal real ante la DIAN/)).toBeTruthy();
    const confirm = within(dialog).getByRole('button', { name: 'Pasar a producción' });
    expect((confirm as HTMLButtonElement).disabled).toBe(true);
    http.expectNone(`${COMPANY_PATH}/environment`);

    fireEvent.click(within(dialog).getByRole('checkbox'));
    fireEvent.click(confirm);

    const request = await expectRequest(http, `${COMPANY_PATH}/environment`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ type_environment_id: 1 });
    request.flush({ success: true, environments: [{ document_kind: 1, type_environment_id: 1 }] });

    expect(await within(step('Producción')).findByText('En producción')).toBeTruthy();
    await respond(http, STATUS_URL, {
      ...EMPTY_READINESS,
      certificate_valid: true,
      environments: [{ document_kind: 1, type_environment_id: 1 }, ...ENVIRONMENTS.slice(1)],
    });
    expect(badge('Producción')).toBe('Completo');
  });
});
