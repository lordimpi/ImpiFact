import { Page, Request, expect, test } from '@playwright/test';
import { seriousViolations, seriousViolationsInBothThemes } from './support/axe';
import { CRITICAL } from './support/critical';
import { typeDate } from './support/date-picker';
import { COMPANIES, USER, mockApi } from './support/mock-api';
import { pickOption } from './support/select';

// Lista, detalle con seguimiento del estado, emisión de factura con vista previa y notas crédito y débito
// (feature-documents) contra la Api simulada.

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
  /** URLs of the Excel and PDF exports, to check their filters. */
  exportRequests: URL[];
  calculateBodies: unknown[];
  issueRequests: Request[];
  noteRequests: Request[];
  fileRequests: Request[];
  detailCalls: () => number;
  /** Mientras sea verdadero, el detalle de `doc-9` no pasa de Enviado (la prueba decide cuándo llega Aceptado). */
  holdDetail: boolean;
  /** Respuesta de la vista previa: el PDF o el conversor caído (503). */
  preview: 'pdf' | 'unavailable';
}

/** Factura aceptada que corrigen las notas (`?factura=doc-1`). */
const ACCEPTED_INVOICE = {
  ...DETAIL,
  id: 'doc-1',
  full_number: 'SETP990000001',
  status: 'Accepted',
};

/** Endpoints de documentos, clientes y productos; el detalle pasa por En cola y Enviado antes de Aceptado. */
async function mockDocuments(page: Page): Promise<DocumentsApi> {
  let detailCalls = 0;
  const api: DocumentsApi = {
    listRequests: [],
    exportRequests: [],
    calculateBodies: [],
    issueRequests: [],
    noteRequests: [],
    fileRequests: [],
    detailCalls: () => detailCalls,
    holdDetail: false,
    preview: 'pdf',
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
    if (url.pathname === '/api/v1/documents/export.xlsx') {
      api.exportRequests.push(url);
      return route.fulfill({
        status: 200,
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        headers: {
          'Content-Disposition': 'attachment; filename="documentos.xlsx"',
          'Access-Control-Expose-Headers': 'Content-Disposition',
        },
        body: 'PK',
      });
    }
    if (url.pathname === '/api/v1/documents/export.pdf') {
      api.exportRequests.push(url);
      return route.fulfill({
        status: 200,
        contentType: 'application/pdf',
        headers: {
          'Content-Disposition': 'attachment; filename="documentos.pdf"',
          'Access-Control-Expose-Headers': 'Content-Disposition',
        },
        body: '%PDF-1.7\n%%EOF\n',
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
    if (url.pathname === '/api/v1/documents/invoices/preview') {
      return api.preview === 'pdf'
        ? route.fulfill({ status: 200, contentType: 'application/pdf', body: '%PDF-1.7\n%%EOF' })
        : route.fulfill({
            status: 503,
            contentType: 'application/problem+json',
            body: JSON.stringify({ status: 503, code: 'pdf.unavailable', message: 'No responde.' }),
          });
    }
    if (url.pathname === '/api/v1/documents/credit-notes') {
      api.noteRequests.push(request);
      return route.fulfill({
        status: 201,
        json: {
          message: 'Documento recibido',
          QRStr: '',
          urlinvoiceattached: '',
          urlinvoicepdf: '',
          urlinvoicexml: '',
          document: { id: 'note-1', full_number: 'NC1', status: 'Queued' },
        },
      });
    }
    if (url.pathname === '/api/v1/documents/debit-notes') {
      api.noteRequests.push(request);
      return route.fulfill({
        status: 422,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          status: 422,
          code: 'document.reference_not_accepted',
          message: 'La factura referenciada no está aceptada por la DIAN.',
        }),
      });
    }
    if (/^\/api\/v1\/documents\/[^/]+\/files\/pdf$/.test(url.pathname)) {
      api.fileRequests.push(request);
      return route.fulfill({
        status: 200,
        contentType: 'application/pdf',
        headers: {
          'Content-Disposition': 'attachment; filename="SETP990000009.pdf"',
          'Access-Control-Expose-Headers': 'Content-Disposition',
        },
        body: '%PDF-1.7\n%%EOF',
      });
    }
    if (url.pathname === '/api/v1/documents/doc-1') {
      return route.fulfill({ json: ACCEPTED_INVOICE });
    }
    if (url.pathname === '/api/v1/documents/note-1') {
      return route.fulfill({
        json: {
          ...DETAIL,
          id: 'note-1',
          kind: 'credit_note',
          full_number: 'NC1',
          status: 'Accepted',
        },
      });
    }
    if (url.pathname === '/api/v1/documents/doc-9') {
      const last = api.holdDetail ? detailStatuses.length - 2 : detailStatuses.length - 1;
      const status = detailStatuses[Math.min(detailCalls++, last)];
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

  await typeDate(page.getByRole('combobox', { name: 'Desde' }), '2026-11-01');
  await pickOption(page, page.getByRole('combobox', { name: 'Tipo de documento' }), 'Factura');
  // The DIAN statuses are folded under «Más filtros» until someone opens it; open, it reads «Menos filtros».
  await expect(page.getByLabel('En cola')).toBeHidden();
  await page.getByText('Más filtros').click();
  await expect(page.getByText('Menos filtros')).toBeVisible();
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
  await expect(page.getByRole('combobox', { name: 'Desde' })).toHaveValue('01/11/2026');
  await expect(page.getByRole('combobox', { name: 'Tipo de documento' })).toHaveText('Factura');
  // With statuses in the URL the section opens by itself, so the active filter is in sight.
  await expect(page.getByLabel('Enviado a la DIAN')).toBeVisible();
  await expect(page.getByLabel('Enviado a la DIAN')).toBeChecked();
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
  await page.getByRole('button', { name: 'Exportar' }).click();
  await page.getByRole('menuitem', { name: 'CSV' }).click();

  const request = await exportRequest;
  expect(request.headers()['authorization']).toMatch(/^Bearer /);
  expect(new URL(request.url()).searchParams.get('kind')).toBe('invoice');
  expect((await download).suggestedFilename()).toBe('documentos-2026-11.csv');
});

test('exports to Excel from the accessible Exportar menu with the current filters', async ({
  page,
}) => {
  // Two axe runs in both themes with the menu open.
  test.slow();
  const api = await mockDocuments(page);
  await logIn(page);
  await page.goto('/documentos?tipo=credit_note&estado=Accepted,Rejected&desde=2026-11-01');
  await expect(page.getByRole('link', { name: 'SETP990000001', exact: true })).toBeVisible();

  const button = page.getByRole('button', { name: 'Exportar' });
  await expect(button).toHaveAttribute('aria-haspopup', 'menu');
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.click();
  const menu = page.getByRole('menu', { name: 'Exportar' });
  await expect(menu).toBeVisible();
  await expect(button).toHaveAttribute('aria-expanded', 'true');
  await expect(menu.getByRole('menuitem')).toHaveText(['Excel (.xlsx)', 'CSV', 'PDF']);
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  // Escape closes the menu and focus goes back to the button.
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();
  await expect(button).toBeFocused();
  await expect(button).toHaveAttribute('aria-expanded', 'false');

  // Keyboard: open, first option with the down arrow, Enter picks it.
  const download = page.waitForEvent('download');
  await page.keyboard.press('Enter');
  await expect(menu).toBeVisible();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');

  expect((await download).suggestedFilename()).toBe('documentos.xlsx');
  const request = api.exportRequests.at(-1)!;
  expect(request.pathname).toBe('/api/v1/documents/export.xlsx');
  expect(request.searchParams.get('kind')).toBe('credit_note');
  expect(request.searchParams.getAll('status')).toEqual(['Accepted', 'Rejected']);
  expect(request.searchParams.get('from')).toBe('2026-11-01');
  expect(request.searchParams.has('limit')).toBe(false);
  await expect(menu).toBeHidden();
  await expect(button).toBeFocused();
  await expect(page.getByText('Se descargó el archivo de Excel.')).toBeVisible();
});

test('exports the PDF report from the Exportar menu with the current filters', async ({ page }) => {
  const api = await mockDocuments(page);
  await logIn(page);
  await page.goto(
    '/documentos?tipo=invoice&estado=Accepted&desde=2026-11-01&hasta=2026-11-30&cliente=golden',
  );
  await expect(page.getByRole('link', { name: 'SETP990000001', exact: true })).toBeVisible();

  const exportRequest = page.waitForRequest('**/api/v1/documents/export.pdf**');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar' }).click();
  await page.getByRole('menuitem', { name: 'PDF' }).click();

  const request = await exportRequest;
  expect(request.method()).toBe('GET');
  expect(request.headers()['authorization']).toMatch(/^Bearer /);
  expect((await download).suggestedFilename()).toBe('documentos.pdf');
  const url = api.exportRequests.at(-1)!;
  expect(url.pathname).toBe('/api/v1/documents/export.pdf');
  expect(url.searchParams.get('kind')).toBe('invoice');
  expect(url.searchParams.getAll('status')).toEqual(['Accepted']);
  expect(url.searchParams.get('from')).toBe('2026-11-01');
  expect(url.searchParams.get('to')).toBe('2026-11-30');
  expect(url.searchParams.get('customer')).toBe('golden');
  expect(url.searchParams.has('limit')).toBe(false);
  await expect(page.getByText('Se descargó el archivo PDF.')).toBeVisible();
});

// Flujo crítico 3 de 5: emisión con totales del servidor, seguimiento del estado hasta Aceptado y descarga del PDF.
test(
  'issues an invoice with the server totals, follows its DIAN status and downloads the PDF',
  CRITICAL,
  async ({ page }) => {
    // Tres revisiones de axe en los dos temas: con muchos trabajadores en paralelo pasa de los 30 s por defecto.
    test.slow();
    // Reloj simulado: el retroceso del seguimiento (2 s, 4 s…) se adelanta sin esperarlo y sin esperas fijas.
    await page.clock.install();
    const api = await mockDocuments(page);
    api.holdDetail = true;
    await logIn(page);
    await page.getByRole('link', { name: 'Documentos' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Documentos' })).toBeVisible();
    expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
    await page.getByRole('link', { name: 'Emitir factura' }).click();
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
    expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

    await expect(
      page.getByRole('checkbox', {
        name: 'Enviar la factura por correo al cliente cuando la DIAN la acepte',
      }),
    ).toBeChecked();
    await page.getByRole('button', { name: 'Emitir factura' }).click();

    await expect(page).toHaveURL('/documentos/doc-9');
    const issue = api.issueRequests[0]!;
    expect(issue.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);
    const body = issue.postDataJSON();
    expect(body.legal_monetary_totals).toEqual(TOTALS);
    expect(body.tax_totals).toEqual([SUBTOTAL]);
    expect(body.invoice_lines[0].line_extension_amount).toBe('1500000.00');
    expect(body.invoice_lines[0].tax_totals).toEqual([SUBTOTAL]);
    // La factura se envía por correo al cliente (casilla marcada por defecto, el cliente tiene correo).
    expect(body.sendmail).toBe(true);

    // El detalle consulta con retroceso (2 s, 4 s) hasta el estado final. La DIAN simulada no pasa de Enviado hasta
    // que la prueba lo decide, así «Consultando…» se ve sin depender de cuánto tarda la máquina.
    const polling = page.getByText('Consultando el estado en la DIAN…');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Factura SETP990000009' }),
    ).toBeVisible();
    await expect(polling).toBeVisible();
    await page.clock.runFor(2_000);
    await expect(page.getByText('Enviado a la DIAN', { exact: true })).toBeVisible();
    await expect(polling).toBeVisible();
    api.holdDetail = false;
    // Cada vuelta adelanta el reloj simulado; con la máquina cargada solo da más vueltas.
    await expect(async () => {
      await page.clock.runFor(4_000);
      await expect(page.getByText('Aceptado', { exact: true })).toBeVisible({ timeout: 500 });
    }).toPass({ timeout: 15_000 });
    await expect(polling).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'PDF', exact: true })).toBeVisible();
    // Con el estado final ya no consulta más, aunque pase el retroceso máximo (30 s) dos veces.
    const callsWhenAccepted = api.detailCalls();
    await page.clock.runFor(60_000);
    expect(api.detailCalls()).toBe(callsWhenAccepted);
    expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

    // El PDF se pide con el token (no con un enlace directo) y se guarda con el nombre que manda el Api.
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'PDF', exact: true }).click();
    expect((await download).suggestedFilename()).toBe('SETP990000009.pdf');
    expect(api.fileRequests.map((request) => new URL(request.url()).pathname)).toEqual([
      '/api/v1/documents/doc-9/files/pdf',
    ]);
    expect(api.fileRequests[0]!.headers()['authorization']).toMatch(/^Bearer /);
  },
);

test('previews the invoice PDF in a dialog and explains when the PDF service is down', async ({
  page,
}) => {
  const api = await mockDocuments(page);
  await logIn(page);
  await page.goto('/documentos/nuevo');
  await page.getByLabel('Buscar cliente').fill('golden');
  await page.getByRole('button', { name: /Elegir CLIENTE DE PRUEBA GOLDEN SAS/ }).click();
  await page.getByLabel('Agregar un producto').fill('serv');
  await page.getByRole('button', { name: /Agregar Servicio de consultoría/ }).click();
  await expect(page.getByTestId('payable')).toContainText('1.785.000,00');

  // El PDF se muestra en un diálogo del panel (un iframe con una URL `blob:`), sin ventanas emergentes.
  await page.getByRole('button', { name: 'Vista previa' }).click();
  const dialog = page.getByRole('dialog', { name: 'Vista previa del PDF' });
  await expect(dialog.locator('iframe')).toHaveAttribute('src', /^blob:/);
  // The dialog sits above the fixed sidebar and top bar: nothing covers its title.
  const title = dialog.getByText('Vista previa del PDF').first();
  const box = (await title.boundingBox())!;
  const topmost = await page.evaluate(
    ([x, y]) => document.elementFromPoint(x, y)?.closest('[role="dialog"]') !== null,
    [box.x + 4, box.y + box.height / 2],
  );
  expect(topmost).toBe(true);
  expect(page.context().pages()).toHaveLength(1);
  const download = page.waitForEvent('download');
  await dialog.getByRole('button', { name: 'Descargar PDF' }).click();
  expect((await download).suggestedFilename()).toBe('vista-previa.pdf');
  await page.getByRole('button', { name: 'Cerrar la vista previa' }).click();
  await expect(dialog).toBeHidden();
  expect(api.issueRequests).toHaveLength(0);

  api.preview = 'unavailable';
  await page.getByRole('button', { name: 'Vista previa' }).click();
  await expect(
    page.getByText('El servicio que genera el PDF no está disponible en este momento.', {
      exact: false,
    }),
  ).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
});

// Flujo crítico 4 de 5: desde el detalle de una factura aceptada, nota crédito con sus líneas y totales del servidor.
test('issues a credit note from an accepted invoice with its lines', CRITICAL, async ({ page }) => {
  const api = await mockDocuments(page);
  await logIn(page);
  await page.getByRole('link', { name: 'Documentos' }).click();
  await page.getByRole('link', { name: 'SETP990000001', exact: true }).click();
  await expect(page).toHaveURL('/documentos/doc-1');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Factura SETP990000001' }),
  ).toBeVisible();
  await expect(page.getByText('Aceptado', { exact: true })).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await page.getByRole('link', { name: 'Emitir nota crédito' }).click();

  await expect(page).toHaveURL('/documentos/nota-credito?factura=doc-1');
  await expect(page.getByRole('heading', { level: 1, name: 'Emitir nota crédito' })).toBeVisible();
  await expect(page.getByText('SETP990000001')).toBeVisible();
  const line = page.getByRole('group', { name: 'Línea 1' });
  await expect(line.getByLabel('Código')).toHaveValue('SERV01');
  await expect(page.getByTestId('payable')).toContainText('1.785.000,00');
  await expect(page.getByRole('button', { name: 'Vista previa' })).toBeVisible();
  await pickOption(
    page,
    page.getByRole('combobox', { name: /^Concepto de la corrección/ }),
    'Anulación de factura electrónica',
  );
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('button', { name: 'Emitir nota crédito' }).click();

  await expect(page).toHaveURL('/documentos/note-1');
  await expect(page.getByRole('heading', { level: 1, name: 'Nota crédito NC1' })).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  const note = api.noteRequests[0]!;
  expect(note.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);
  expect(note.postDataJSON()).toMatchObject({
    type_document_id: 4,
    discrepancyresponsecode: 2,
    billing_reference: { number: 'SETP990000001', uuid: 'cufe-9', issue_date: '2026-11-14' },
    legal_monetary_totals: TOTALS,
    tax_totals: [SUBTOTAL],
  });
  expect(note.postDataJSON().credit_note_lines[0].line_extension_amount).toBe('1500000.00');
});

test('a debit note found from the list shows why the API rejects it', async ({ page }) => {
  const api = await mockDocuments(page);
  await logIn(page);
  await page.goto('/documentos');
  await page.getByRole('main').getByRole('link', { name: 'Nota débito' }).click();

  await expect(page.getByRole('heading', { level: 1, name: 'Emitir nota débito' })).toBeVisible();
  await page.getByLabel('Buscar factura').fill('990000001');
  await page.getByRole('button', { name: /Elegir SETP990000001/ }).click();
  const search = api.listRequests.at(-1)!;
  expect(search.searchParams.get('kind')).toBe('invoice');
  expect(search.searchParams.getAll('status')).toEqual(['Accepted', 'AcceptedWithWarnings']);
  expect(search.searchParams.get('number')).toBe('990000001');

  const line = page.getByRole('group', { name: 'Línea 1' });
  await line.getByLabel('Código').fill('INT01');
  await line.getByLabel('Descripción').fill('Intereses de mora');
  await line.getByLabel('Precio unitario').fill('50000');
  await expect(page.getByTestId('payable')).toBeVisible();
  await pickOption(
    page,
    page.getByRole('combobox', { name: /^Concepto de la corrección/ }),
    'Intereses',
  );
  await page.getByRole('button', { name: 'Emitir nota débito' }).click();

  await expect(page.getByRole('alert')).toContainText(
    'La factura referenciada no está aceptada por la DIAN.',
  );
  expect(api.noteRequests[0]!.postDataJSON()).toMatchObject({
    type_document_id: 5,
    discrepancyresponsecode: 1,
    requested_monetary_totals: TOTALS,
  });
  await expect(page).toHaveURL('/documentos/nota-debito');
  expect(await seriousViolations(page)).toEqual([]);
});

test.describe('on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  /** The cell sits fully inside the screen width: nothing hides behind a sideways scroll. */
  async function expectOnScreen(page: Page, cell: ReturnType<Page['locator']>) {
    await expect(cell).toBeVisible();
    const box = (await cell.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize()!.width);
  }

  /** Neither the page nor the table's own scroll region scrolls sideways. */
  async function expectNoSidewaysScroll(page: Page, caption: string) {
    const scrollsSideways = (element: Element) => element.scrollWidth > element.clientWidth;
    expect(await page.locator('html').evaluate(scrollsSideways)).toBe(false);
    expect(await page.getByRole('region', { name: caption }).evaluate(scrollsSideways)).toBe(false);
  }

  test('stacks the list and the detail lines so status and amounts show without scrolling sideways', async ({
    page,
  }) => {
    await mockDocuments(page);
    await logIn(page);
    await page.goto('/documentos');

    const list = page.getByRole('table', { name: 'Documentos emitidos' });
    const row = list
      .getByRole('row')
      .filter({ has: page.getByRole('link', { name: 'SETP990000001', exact: true }) });
    await expectOnScreen(page, row.locator('td[data-label="Estado DIAN"]'));
    await expect(row.locator('td[data-label="Estado DIAN"]')).toContainText('Aceptad');
    await expectOnScreen(page, row.locator('td[data-label="Total"]'));
    await expect(row.locator('td[data-label="Total"]')).toContainText('1.785.000');
    await expectNoSidewaysScroll(page, 'Documentos emitidos');
    expect(await seriousViolations(page)).toEqual([]);

    await page.goto('/documentos/doc-1');
    const lines = page.getByRole('table', { name: 'Líneas del documento' });
    const line = lines.getByRole('row').filter({ hasText: 'SERV01' });
    await expectOnScreen(page, line.locator('td[data-label="Descripción"]'));
    await expectOnScreen(page, line.locator('td[data-label="Valor"]'));
    await expect(line.locator('td[data-label="Valor"]')).toContainText('1.500.000');
    await expectNoSidewaysScroll(page, 'Líneas del documento');
    expect(await seriousViolations(page)).toEqual([]);
  });
});
