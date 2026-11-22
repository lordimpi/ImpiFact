import { Page, expect, test } from '@playwright/test';
import { seriousViolationsInBothThemes } from './support/axe';
import { COMPANIES } from './support/mock-api';
import { pickOption } from './support/select';
import { signInAt } from './support/sign-in';

// Documento soporte en adquisiciones a no obligados a facturar (fase 7a, T4b) contra la Api simulada: el software del
// documento soporte en la pestaña Software, la emisión, la lista filtrada por tipo, el detalle y la nota de ajuste.

const COMPANY = COMPANIES[0]!;
const NIT = '900123456';

const SUBTOTAL = { tax_id: 1, percent: '0.00', tax_amount: '0.00', taxable_amount: '200.00' };
const TOTALS = {
  line_extension_amount: '200.00',
  tax_exclusive_amount: '200.00',
  tax_inclusive_amount: '200.00',
  allowance_total_amount: '0.00',
  charge_total_amount: '0.00',
  pre_paid_amount: '0.00',
  payable_amount: '200.00',
};
const CALCULATION = {
  allowance_charges: [],
  lines: [{ allowance_charges: [], line_extension_amount: '200.00', tax_subtotals: [SUBTOTAL] }],
  tax_totals: [{ tax_id: 1, tax_amount: '0.00', tax_subtotals: [SUBTOTAL] }],
  legal_monetary_totals: TOTALS,
};
const SELLER = {
  identification_number: '800000003',
  dv: '4',
  type_document_identification_id: 6,
  name: 'PROVEEDOR NO OBLIGADO DE EJEMPLO',
  phone: '3000000003',
  address: 'CALLE 3 # 3-03',
  merchant_registration: '0000-00',
};
/** The support document as the API returns it (with its own `kind` since 91adc38). */
const SUPPORT_DOCUMENT = {
  id: 'ds-9',
  document_type_id: 11,
  kind: 'support_document',
  full_number: 'SEDS984000009',
  issue_date: '2026-11-21',
  issue_time: '10:05:00',
  status: 'Accepted',
  email_status: 'NotSent',
  cufe: 'd'.repeat(96),
  customer: SELLER,
  totals: TOTALS,
  taxes: [SUBTOTAL],
  lines: [
    {
      line_number: 1,
      code: 'SERV01',
      description: 'SERVICIO DE EJEMPLO',
      invoiced_quantity: '2',
      price_amount: '100.00',
      unit_measure_id: 70,
      type_item_identification_id: 4,
      line_extension_amount: '200.00',
      tax_totals: [SUBTOTAL],
    },
  ],
};
const ADJUSTMENT_NOTE = {
  ...SUPPORT_DOCUMENT,
  id: 'nds-1',
  document_type_id: 13,
  kind: 'support_document_adjustment_note',
  full_number: 'NDS1',
};

interface SupportApi {
  listRequests: URL[];
  issueBodies: Record<string, unknown>[];
  noteBodies: Record<string, unknown>[];
}

/** Calculation, issuing, list and detail of the support document family. */
async function mockSupportDocuments(page: Page): Promise<SupportApi> {
  const api: SupportApi = { listRequests: [], issueBodies: [], noteBodies: [] };
  const issued = (document: object) => ({
    status: 201,
    json: { message: 'Documento recibido', cuds: 'd'.repeat(96), document },
  });
  await page.route('**/api/v1/documents**', (route) => {
    const request = route.request();
    const url = new URL(request.url());
    switch (url.pathname) {
      case '/api/v1/documents':
        api.listRequests.push(url);
        return route.fulfill({
          json: { items: [SUPPORT_DOCUMENT, ADJUSTMENT_NOTE], next_cursor: null },
        });
      case '/api/v1/documents/calculate':
        return route.fulfill({ json: CALCULATION });
      case '/api/v1/documents/support-documents':
        api.issueBodies.push(request.postDataJSON());
        return route.fulfill(
          issued({ id: 'ds-9', full_number: 'SEDS984000009', status: 'Queued' }),
        );
      case '/api/v1/documents/support-documents/adjustment-notes':
        api.noteBodies.push(request.postDataJSON());
        return route.fulfill(issued({ id: 'nds-1', full_number: 'NDS1', status: 'Queued' }));
      case '/api/v1/documents/ds-9':
        return route.fulfill({ json: SUPPORT_DOCUMENT });
      case '/api/v1/documents/nds-1':
        return route.fulfill({ json: ADJUSTMENT_NOTE });
      default:
        return route.fulfill({
          status: 404,
          json: { code: 'document.not_found', message: 'No existe.' },
        });
    }
  });
  return api;
}

test('the software screen saves the support document software and never shows its PIN', async ({
  page,
}) => {
  const bodies: unknown[] = [];
  let saved = false;
  const support = {
    document_kind: 4,
    type_environment_id: 2,
    identifier: 'sd-123',
    url: 'https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc',
    test_set_id: null,
    has_pin: true,
  };
  await signInAt(page, `/empresas/${COMPANY}/software`);
  await page.route(`**/api/v1/companies/${NIT}/software`, (route) => {
    if (route.request().method() === 'PUT') {
      bodies.push(route.request().postDataJSON());
      saved = true;
    }
    return route.fulfill({ json: { success: true, software: saved ? [support] : [] } });
  });
  await page.reload();

  await expect(page.getByRole('heading', { name: 'Software del documento soporte' })).toBeVisible();
  const pin = page.getByRole('textbox', { name: 'PIN (documento soporte)' });
  await expect(pin).toHaveAttribute('type', 'password');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('textbox', { name: 'Identificador (documento soporte)' }).fill('sd-123');
  await pin.fill('54321');
  await page.getByRole('button', { name: 'Guardar el software del documento soporte' }).click();

  await expect(page.getByText('Los cambios se guardaron.')).toBeVisible();
  expect(bodies).toEqual([{ idsupportdocument: 'sd-123', pinsupportdocument: '54321' }]);
  const table = page.getByRole('table', { name: 'Software registrado' });
  await expect(table.getByRole('row', { name: /Documento soporte/ })).toContainText(
    'PIN configurado',
  );
  await expect(pin).toHaveValue('');
});

test('issues a support document, finds it in the list and adjusts it with a note', async ({
  page,
}) => {
  const api = await mockSupportDocuments(page);
  await signInAt(page, '/');

  await page
    .getByRole('navigation', { name: 'Navegación principal' })
    .getByRole('link', { name: 'Documento soporte' })
    .click();
  await expect(page).toHaveURL('/documentos/documento-soporte');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Emitir documento soporte' }),
  ).toBeVisible();

  await pickOption(page, page.getByRole('combobox', { name: 'Tipo de identificación' }), 'NIT');
  await page.getByLabel('Número de identificación').fill(SELLER.identification_number);
  await page.getByLabel('Dígito de verificación').fill(SELLER.dv);
  await page.getByLabel('Nombre o razón social').fill(SELLER.name);
  await page.getByLabel('Dirección').fill(SELLER.address);
  await page.getByLabel('Teléfono').fill(SELLER.phone);
  await page.getByLabel('Matrícula mercantil').fill(SELLER.merchant_registration);
  await page.getByLabel('Código postal').fill('630001');
  const line = page.getByRole('group', { name: 'Línea 1' });
  await line.getByLabel('Código').fill('SERV01');
  await line.getByLabel('Descripción').fill('SERVICIO DE EJEMPLO');
  await line.getByLabel('Cantidad').fill('2');
  await line.getByLabel('Precio unitario').fill('100');
  await expect(page.getByTestId('payable')).toContainText('200,00');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('button', { name: 'Emitir documento soporte' }).click();
  await expect(page).toHaveURL('/documentos/ds-9');
  expect(api.issueBodies).toHaveLength(1);
  expect(api.issueBodies[0]).toMatchObject({
    type_document_id: 11,
    type_operation_id: 23,
    seller: { identification_number: '800000003', postal_zone_code: '630001' },
    legal_monetary_totals: TOTALS,
  });
  await expect(
    page.getByRole('heading', { level: 1, name: /Documento soporte SEDS984000009/ }),
  ).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Proveedor' })).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.goto('/documentos?tipo=support_document');
  await expect(page.getByRole('combobox', { name: 'Tipo de documento' })).toHaveText(
    'Documento soporte',
  );
  expect(api.listRequests.at(-1)?.searchParams.get('kind')).toBe('support_document');
  await expect(page.getByRole('row', { name: /SEDS984000009/ })).toContainText('Documento soporte');
  await expect(page.getByRole('row', { name: /NDS1/ })).toContainText('Nota de ajuste DS');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('link', { name: 'SEDS984000009', exact: true }).click();
  await page.getByRole('link', { name: 'Emitir nota de ajuste' }).click();
  await expect(page).toHaveURL('/documentos/nota-ajuste?soporte=ds-9');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Emitir nota de ajuste' }),
  ).toBeVisible();
  await expect(page.getByText('SEDS984000009')).toBeVisible();
  await pickOption(
    page,
    page.getByRole('combobox', { name: 'Concepto de la corrección' }),
    'Ajuste de precio',
  );
  await page.getByLabel('Código postal del proveedor').fill('630001');
  await expect(page.getByTestId('payable')).toContainText('200,00');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('button', { name: 'Emitir nota de ajuste' }).click();
  await expect(page).toHaveURL('/documentos/nds-1');
  expect(api.noteBodies[0]).toMatchObject({
    type_document_id: 13,
    discrepancyresponsecode: 4,
    billing_reference: { number: 'SEDS984000009', issue_date: '2026-11-21' },
    seller: { identification_number: '800000003', postal_zone_code: '630001' },
  });
  await expect(
    page.getByRole('heading', { level: 1, name: /Nota de ajuste DS NDS1/ }),
  ).toBeVisible();
});

test('the dashboard counts support documents and adjustment notes in each month', async ({
  page,
}) => {
  const months = Array.from({ length: 12 }, (_, index) => {
    const month = new Date(Date.UTC(2025, 11 + index, 1)).toISOString().slice(0, 7);
    const last = index === 11;
    return {
      month,
      invoice: last ? 30 : index,
      credit_note: last ? 2 : 0,
      debit_note: last ? 1 : 0,
      support_document: last ? 6 : 0,
      support_document_adjustment_note: last ? 1 : 0,
      other: last ? 2 : 0,
      total: last ? 42 : index,
    };
  });
  await signInAt(page, '/');
  await page.route('**/api/v1/dashboard/summary', (route) =>
    route.fulfill({
      json: {
        generated_at: '2026-11-21T15:00:00Z',
        current_month: { month: '2026-11', total: 42, by_status: [] },
        months,
        plan: null,
        certificate: null,
        resolutions: [],
      },
    }),
  );
  await page.reload();

  await page.getByText('Ver los datos en una tabla').click();
  const table = page.getByRole('table', { name: 'Documentos por mes' });
  await expect(table.getByRole('columnheader', { name: 'Documentos soporte' })).toBeVisible();
  await expect(table.getByRole('columnheader', { name: 'Notas de ajuste DS' })).toBeVisible();
  await expect(table.getByRole('row', { name: /nov\.? 2026/ })).toHaveText(
    /nov\.? 2026\s*30\s*2\s*1\s*6\s*1\s*2\s*42/,
  );
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
});
