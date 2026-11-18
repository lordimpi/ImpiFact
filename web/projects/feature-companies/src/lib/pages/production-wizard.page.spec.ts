import { TestBed } from '@angular/core/testing';
import { IDEMPOTENCY_KEY } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { CompanySetupStore } from '../state/company-setup.store';
import {
  CERTIFICATE,
  expectRequest,
  problem,
  renderCompaniesRoute,
} from '../testing/companies-testing';

const CERTIFICATES_URL = '/api/v1/companies/900123456/certificates';
const TEST_SET = '7f8a1c2e-1111-4222-8333-944455556666';

async function open(certificates: unknown[] = [CERTIFICATE]) {
  const rendered = await renderCompaniesRoute('/empresas/company-1/produccion');
  await screen.findByRole('heading', { level: 2, name: 'Paso a producción' });
  (await expectRequest(rendered.http, CERTIFICATES_URL)).flush({ success: true, certificates });
  return rendered;
}

const step = (title: string) => {
  const list = screen.getByRole('list', { name: 'Pasos del paso a producción' });
  return within(list)
    .getAllByRole('listitem')
    .find((item) => within(item).queryByRole('heading', { name: title }))!;
};

describe('ProductionWizardPage', () => {
  it('derives each step from what the API reports', async () => {
    await open();

    await waitFor(() =>
      expect(within(step('Certificado digital')).getByText('Completo')).toBeTruthy(),
    );
    expect(
      within(step('Certificado digital')).getByText(
        'Certificado activo de CN=ACME SAS, SERIALNUMBER=900123456, vence en 225 días.',
      ),
    ).toBeTruthy();
    expect(within(step('Datos de la empresa')).getByText('Sin verificar')).toBeTruthy();
    expect(
      within(step('Datos de la empresa'))
        .getByRole('link', { name: 'Ir a Datos' })
        .getAttribute('href'),
    ).toBe('/empresas/company-1/datos');
    expect(within(step('Software y set de pruebas')).getByText('Sin verificar')).toBeTruthy();
    expect(
      (
        within(step('Set de pruebas de la DIAN')).getByRole('button', {
          name: 'Enviar factura de prueba',
        }) as HTMLButtonElement
      ).disabled,
    ).toBe(true);
  });

  it('locks the following steps while there is no valid certificate', async () => {
    await open([{ ...CERTIFICATE, certificate_days_left: 0, is_active: true }]);

    await waitFor(() =>
      expect(within(step('Certificado digital')).getByText('Pendiente')).toBeTruthy(),
    );
    expect(within(step('Set de pruebas de la DIAN')).getByText('Bloqueado')).toBeTruthy();
    expect(within(step('Producción')).getByText('Bloqueado')).toBeTruthy();
    expect(
      within(step('Producción')).queryByRole('button', { name: /Pasar a producción/ }),
    ).toBeNull();
  });

  it('enables the test invoice once the test set is known', async () => {
    await open();

    TestBed.inject(CompanySetupStore).setTestSetId(TEST_SET);

    await waitFor(() =>
      expect(
        (
          within(step('Set de pruebas de la DIAN')).getByRole('button', {
            name: 'Enviar factura de prueba',
          }) as HTMLButtonElement
        ).disabled,
      ).toBe(false),
    );
  });

  it('sends the test invoice to the test set and follows its DIAN status', async () => {
    const { http } = await open();
    const testSet = step('Software y set de pruebas');
    fireEvent.input(within(testSet).getByLabelText(/Identificador del set de pruebas/), {
      target: { value: TEST_SET },
    });

    fireEvent.click(
      within(step('Set de pruebas de la DIAN')).getByRole('button', {
        name: 'Enviar factura de prueba',
      }),
    );

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
    expect(result.getByText('Completo')).toBeTruthy();
  });

  it('shows the DIAN rejection with its explanation', async () => {
    const { http } = await open();
    TestBed.inject(CompanySetupStore).setTestSetId(TEST_SET);
    fireEvent.click(
      await within(step('Set de pruebas de la DIAN')).findByRole('button', {
        name: 'Enviar factura de prueba',
      }),
    );
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
    http.expectNone('/api/v1/companies/900123456/environment');

    fireEvent.click(within(dialog).getByRole('checkbox'));
    fireEvent.click(confirm);

    const request = await expectRequest(http, '/api/v1/companies/900123456/environment');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ type_environment_id: 1 });
    request.flush({ success: true, environments: [{ document_kind: 1, type_environment_id: 1 }] });

    expect(await within(step('Producción')).findByText('En producción')).toBeTruthy();
  });
});
