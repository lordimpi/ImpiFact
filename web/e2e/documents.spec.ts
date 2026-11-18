import { Page, Request, expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { COMPANIES, USER, mockApi } from './support/mock-api';

// Lista, detalle con seguimiento del estado y emisión de factura (feature-documents) contra la Api simulada.

const SUBTOTAL = {
  tax_id: 1,
  percent: '19.00',
  tax_amount: '285000.00',
  taxable_amount: '1500000.00',
};
const TOTALS = {
  line_extension_amount: '1500000.00',
  tax_exclusive_amount: '1500000.00',
  tax_inclusive_amount: '1785000.00',
  allowance_total_amount: '0.00',
  charge_total_amount: '0.00',
  pre_paid_amount: '0.00',
  payable_amount: '1785000.00',
};
const CALCULATION = {
  allowance_charges: [],
  lines: [
    { allowance_charges: [], line_extension_amount: '1500000.00', tax_subtotals: [SUBTOTAL] },
  ],
  tax_totals: [{ tax_id: 1, tax_amount: '285000.00', tax_subtotals: [SUBTOTAL] }],
  legal_monetary_totals: TOTALS,
};
const CUSTOMER = {
  id: 'cus-1',
  identification_number: '800000001',
  dv: '1',
  type_document_identification_id: 6,
  type_organization_id: 1,
  type_regime_id: 1,
  type_liability_id: 117,
  name: 'CLIENTE DE PRUEBA GOLDEN SAS',
  email: 'cliente.golden@example.com',
  municipality_id: 149,
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
const SUMMARY = {
  id: 'doc-1',
  full_number: 'SETP990000001',
  kind: 'invoice',
  issue_date: '2026-11-14',
  status: 'Accepted',
  email_status: 'Sent',
  customer: { name: CUSTOMER.name, identification_number: CUSTOMER.identification_number },
  totals: TOTALS,
};
const DETAIL = {
  ...SUMMARY,
  id: 'doc-9',
  full_number: 'SETP990000009',
  issue_time: '10:05:00',
  cufe: 'cufe-9',
  customer: CUSTOMER,
  lines: [
    {
      line_number: 1,
      code: 'SERV01',
      description: 'Servicio de consultoría',
      invoiced_quantity: '1',
      price_amount: '1500000.00',
      line_extension_amount: '1500000.00',
    },
  ],
  taxes: [SUBTOTAL],
};

interface DocumentsApi {
  listRequests: URL[];
  calculateBodies: unknown[];
  issueRequests: Request[];
  detailCalls: () => number;
}

/** Endpoints de documentos, clientes y productos; el detalle pasa por En cola y Enviado antes de Aceptado. */
async function mockDocuments(page: Page): Promise<DocumentsApi> {
  let detailCalls = 0;
  const api: DocumentsApi = {
    listRequests: [],
    calculateBodies: [],
    issueRequests: [],
    detailCalls: () => detailCalls,
  };
  const detailStatuses = ['Queued', 'Sent', 'Accepted'];

  await page.route('**/api/v1/customers**', (route) =>
    route.fulfill({ json: { items: [CUSTOMER], next_cursor: null } }),
  );
  await page.route('**/api/v1/products**', (route) =>
    route.fulfill({ json: { items: [PRODUCT], next_cursor: null } }),
  );
  await page.route('**/api/v1/documents**', (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname === '/api/v1/documents' && request.method() === 'GET') {
      api.listRequests.push(url);
      return route.fulfill({
        json: {
          items: [SUMMARY],
          next_cursor: null,
          total_count: url.searchParams.has('include_total') ? 1 : null,
        },
      });
    }
    if (url.pathname === '/api/v1/documents/export.csv') {
      return route.fulfill({
        status: 200,
        contentType: 'text/csv',
        headers: {
          'Content-Disposition': 'attachment; filename="documentos-2026-11.csv"',
          'Access-Control-Expose-Headers': 'Content-Disposition',
        },
        body: 'numero;cliente\nSETP990000001;CLIENTE DE PRUEBA GOLDEN SAS\n',
      });
    }
    if (url.pathname === '/api/v1/documents/calculate') {
      api.calculateBodies.push(request.postDataJSON());
      return route.fulfill({ json: CALCULATION });
    }
    if (url.pathname === '/api/v1/documents/invoices') {
      api.issueRequests.push(request);
      return route.fulfill({
        status: 201,
        json: {
          message: 'Documento recibido',
          QRStr: '',
          urlinvoiceattached: '',
          urlinvoicepdf: '',
          urlinvoicexml: '',
          document: { id: 'doc-9', full_number: 'SETP990000009', status: 'Queued' },
        },
      });
    }
    if (url.pathname === '/api/v1/documents/doc-9') {
      const status = detailStatuses[Math.min(detailCalls++, detailStatuses.length - 1)];
      return route.fulfill({ json: { ...DETAIL, status } });
    }
    return route.fulfill({
      status: 404,
      json: { code: 'document.not_found', message: 'No existe.' },
    });
  });
  return api;
}

async function logIn(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Correo electrónico').fill(USER.email);
  await page.getByLabel('Contraseña').fill(USER.password);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page).toHaveURL('/');
}

test.beforeEach(async ({ page }) => {
  await mockApi(page, { companies: [COMPANIES[0]!] });
});

test('keeps the list filters in the URL and sends them to the API', async ({ page }) => {
  const api = await mockDocuments(page);
  await logIn(page);
  await page.getByRole('link', { name: 'Documentos' }).click();

  await expect(page.getByRole('heading', { level: 1, name: 'Documentos' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'SETP990000001', exact: true })).toBeVisible();

  await page.getByLabel('Desde').fill('2026-11-01');
  await page.getByLabel('Tipo de documento').selectOption('invoice');
  await page.getByLabel('En cola').check();
  await page.getByLabel('Enviado a la DIAN').check();
  await page.getByLabel('Cliente', { exact: true }).fill('golden');
  await page.getByLabel('Mostrar cuántos documentos hay').check();
  await page.getByRole('button', { name: 'Aplicar filtros' }).click();

  await expect(page).toHaveURL(
    '/documentos?desde=2026-11-01&tipo=invoice&estado=Queued,Sent&cliente=golden&total=1',
  );
  await expect(page.getByText('1 documentos con estos filtros')).toBeVisible();
  const last = api.listRequests.at(-1)!;
  expect(last.searchParams.get('from')).toBe('2026-11-01');
  expect(last.searchParams.get('kind')).toBe('invoice');
  expect(last.searchParams.getAll('status')).toEqual(['Queued', 'Sent']);
  expect(last.searchParams.get('customer')).toBe('golden');
  expect(last.searchParams.get('include_total')).toBe('true');

  // La misma URL, recargada, consulta lo mismo y muestra los filtros.
  await page.reload();
  await expect(page.getByLabel('Cliente', { exact: true })).toHaveValue('golden');
  expect(api.listRequests.at(-1)!.searchParams.getAll('status')).toEqual(['Queued', 'Sent']);

  expect(await seriousViolations(page)).toEqual([]);
});

test('downloads the CSV export with the token, not with a plain link', async ({ page }) => {
  await mockDocuments(page);
  await logIn(page);
  await page.goto('/documentos?tipo=invoice');
  await expect(page.getByRole('link', { name: 'SETP990000001', exact: true })).toBeVisible();

  const exportRequest = page.waitForRequest('**/api/v1/documents/export.csv**');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar CSV' }).click();

  const request = await exportRequest;
  expect(request.headers()['authorization']).toMatch(/^Bearer /);
  expect(new URL(request.url()).searchParams.get('kind')).toBe('invoice');
  expect((await download).suggestedFilename()).toBe('documentos-2026-11.csv');
});

test('issues an invoice with the server totals and follows its DIAN status', async ({ page }) => {
  const api = await mockDocuments(page);
  await logIn(page);
  await page.goto('/documentos/nuevo');
  await expect(page.getByRole('heading', { level: 1, name: 'Emitir factura' })).toBeVisible();

  await page.getByLabel('Buscar cliente').fill('golden');
  await page.getByRole('button', { name: /Elegir CLIENTE DE PRUEBA GOLDEN SAS/ }).click();
  await expect(page.getByText('Cliente elegido')).toBeVisible();
  await page.getByLabel('Agregar un producto').fill('serv');
  await page.getByRole('button', { name: /Agregar Servicio de consultoría/ }).click();

  await expect(page.getByTestId('payable')).toContainText('1.785.000,00');
  expect(api.calculateBodies.at(-1)).toEqual({
    lines: [
      {
        quantity: 1,
        unit_price: 1500000,
        free_of_charge: false,
        taxes: [{ tax_id: 1, percent: 19 }],
      },
    ],
  });
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Emitir factura' }).click();

  await expect(page).toHaveURL('/documentos/doc-9');
  const issue = api.issueRequests[0]!;
  expect(issue.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);
  const body = issue.postDataJSON();
  expect(body.legal_monetary_totals).toEqual(TOTALS);
  expect(body.tax_totals).toEqual([SUBTOTAL]);
  expect(body.invoice_lines[0].line_extension_amount).toBe('1500000.00');
  expect(body.invoice_lines[0].tax_totals).toEqual([SUBTOTAL]);

  // El detalle consulta con retroceso (2 s, 4 s) hasta el estado final.
  await expect(
    page.getByRole('heading', { level: 1, name: 'Factura SETP990000009' }),
  ).toBeVisible();
  await expect(page.getByText('Consultando el estado en la DIAN…')).toBeVisible();
  await expect(page.getByText('Aceptado', { exact: true })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Consultando el estado en la DIAN…')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'PDF', exact: true })).toBeVisible();
  const callsWhenAccepted = api.detailCalls();
  await page.waitForTimeout(3_000);
  expect(api.detailCalls()).toBe(callsWhenAccepted);

  expect(await seriousViolations(page)).toEqual([]);
});
