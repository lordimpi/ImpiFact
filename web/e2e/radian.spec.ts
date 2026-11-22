import { Page, expect, test } from '@playwright/test';
import { seriousViolationsInBothThemes } from './support/axe';
import { typeDate } from './support/date-picker';
import { pressLikeAPerson } from './support/press';
import { pickOption } from './support/select';
import { signInAt } from './support/sign-in';

// RADIAN in the panel (phase 7a, T6b) against the mocked API: received invoices loaded by CUFE, their events in the
// order of the DIAN with the confirm dialog before each send, what the DIAN reports, and the tacit acceptance (034)
// of an own accepted invoice.

const CUFE = 'a'.repeat(96);

const ACKNOWLEDGEMENT = {
  id: 'ev-1',
  event_code: '030',
  event_name: 'Acuse de recibo de Factura Electrónica de Venta',
  status: 'accepted',
  cude: 'c'.repeat(96),
  number: 'AR1',
  issued_at: '2026-11-21T15:40:00Z',
  dian_status_code: '00',
  dian_status_description: 'Procesado Correctamente.',
  error_messages: [],
};

const RECEIVED = {
  id: 'rd-1',
  supplier_identification_number: '800000003',
  supplier_dv: '4',
  supplier_name: 'PROVEEDOR DE EJEMPLO SAS',
  supplier_email: 'facturas@proveedor.example.com',
  type_document_id: 1,
  prefix: 'FV',
  number: 'FV1001',
  cufe: CUFE,
  issue_date: '2026-11-18',
  type_environment_id: 2,
  line_extension_amount: '1000000.00',
  tax_exclusive_amount: '1000000.00',
  allowance_total_amount: '0.00',
  tax_amount: '190000.00',
  payable_amount: '1190000.00',
  created_at: '2026-11-21T15:30:00Z',
  events: [] as object[],
};

interface RadianApi {
  listRequests: URL[];
  loadBodies: unknown[];
  eventBodies: unknown[];
}

/** Received invoices: list, load by CUFE, detail and events; the events sent are kept, as the API does. */
async function mockReceived(page: Page): Promise<RadianApi> {
  const api: RadianApi = { listRequests: [], loadBodies: [], eventBodies: [] };
  const invoice = { ...RECEIVED, events: [] as object[] };
  const others = Array.from({ length: 24 }, (_, index) => ({
    ...RECEIVED,
    id: `rd-other-${index}`,
    number: `FV${2000 + index}`,
    supplier_name: 'OTRO PROVEEDOR SAS',
    events: [ACKNOWLEDGEMENT],
  }));
  await page.route('**/api/v1/received-documents**', (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname === '/api/v1/received-documents' && request.method() === 'POST') {
      api.loadBodies.push(request.postDataJSON());
      return route.fulfill({ status: 201, json: invoice });
    }
    if (url.pathname === '/api/v1/received-documents') {
      api.listRequests.push(url);
      const all = [invoice, ...others];
      const offset = Number(url.searchParams.get('offset') ?? 0);
      const limit = Number(url.searchParams.get('limit') ?? 50);
      return route.fulfill({
        json: { data: all.slice(offset, offset + limit), total_count: all.length, limit, offset },
      });
    }
    if (url.pathname === '/api/v1/received-documents/rd-1') {
      return route.fulfill({ json: invoice });
    }
    if (url.pathname === '/api/v1/received-documents/rd-1/events') {
      const body = request.postDataJSON() as { event_code: string };
      api.eventBodies.push(body);
      const event = {
        ...ACKNOWLEDGEMENT,
        id: `ev-${body.event_code}`,
        event_code: body.event_code,
        issued_at: new Date().toISOString(),
      };
      invoice.events = [...invoice.events, event];
      return route.fulfill({
        json: {
          success: true,
          message: 'ApplicationResponse #1 generada con éxito',
          cude: event.cude,
          event,
        },
      });
    }
    if (url.pathname === '/api/v1/received-documents/rd-1/dian-events') {
      return route.fulfill({
        json: {
          success: true,
          message: 'Consulta de eventos generada con exito',
          cufe: CUFE,
          events: [
            {
              dian_code: '030',
              description: 'Acuse de recibo de Factura Electrónica de Venta',
              date: '2026-11-21',
              time: '10:40:00',
              event_number: 'AR1',
            },
          ],
        },
      });
    }
    return route.fulfill({
      status: 404,
      json: {
        code: 'received_document.not_found',
        message: 'No se encontró el documento recibido.',
      },
    });
  });
  return api;
}

test('loads a received invoice by its CUFE and registers its acknowledgement after confirming', async ({
  page,
}) => {
  const api = await mockReceived(page);
  await signInAt(page, '/');

  await page
    .getByRole('navigation', { name: 'Navegación principal' })
    .getByRole('link', { name: 'Documentos recibidos' })
    .click();
  await expect(page).toHaveURL('/documentos/recibidos');
  await expect(page.getByRole('heading', { level: 1, name: 'Documentos recibidos' })).toBeVisible();
  await expect(page.getByRole('row', { name: /FV1001/ })).toContainText('Sin eventos');
  await expect(page.getByRole('row', { name: /FV2000/ })).toContainText('Acuse de recibo');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('button', { name: 'Página 2' }).click();
  await expect(page.getByRole('row', { name: /FV2019/ })).toBeVisible();
  expect(api.listRequests.at(-1)?.searchParams.get('offset')).toBe('20');

  await page.getByRole('button', { name: 'Cargar por CUFE' }).click();
  const load = page.getByRole('dialog', { name: 'Cargar factura por CUFE' });
  await expect(load).toBeVisible();
  await load.getByRole('textbox', { name: /CUFE/ }).fill(CUFE.toUpperCase());
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await load.getByRole('button', { name: 'Cargar factura' }).click();

  await expect(page).toHaveURL('/documentos/recibidos/rd-1');
  expect(api.loadBodies).toEqual([{ cufe: CUFE }]);
  await expect(page.getByText('Factura FV1001 cargada.')).toBeVisible();
  await expect(
    page.getByRole('heading', { level: 1, name: 'Factura recibida FV1001' }),
  ).toBeVisible();
  await expect(page.getByText('Todavía no hay eventos registrados.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aceptar', exact: true })).toBeDisabled();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('button', { name: 'Acuse de recibo', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Acuse de recibo' });
  await expect(dialog).toBeVisible();
  await pressLikeAPerson(page, dialog.getByRole('button', { name: 'Continuar' }));

  const confirm = page.getByRole('alertdialog', {
    name: '¿Registrar «Acuse de recibo de la factura» ante la DIAN?',
  });
  await expect(confirm).toBeVisible();
  await expect(confirm).toContainText('no se puede deshacer ni anular');
  // The safe choice has the focus, and Escape only closes the question.
  await expect(confirm.getByRole('button', { name: 'Volver' })).toBeFocused();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await page.keyboard.press('Escape');
  await expect(confirm).toBeHidden();
  await expect(dialog).toBeVisible();
  expect(api.eventBodies).toEqual([]);

  await dialog.getByRole('button', { name: 'Continuar' }).click();
  await confirm.getByRole('button', { name: 'Registrar en la DIAN' }).click();

  await expect(
    page.getByText('La DIAN registró el evento «Acuse de recibo de la factura».'),
  ).toBeVisible();
  expect(api.eventBodies).toEqual([{ event_code: '030' }]);
  await expect(dialog).toBeHidden();
  // The invoice is read again by itself: the event is in the history and the next step opens.
  const history = page.getByRole('list', { name: 'Historial de eventos RADIAN' });
  await expect(history).toContainText('Acuse de recibo de la factura (030)');
  await expect(page.getByRole('button', { name: 'Acuse de recibo', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Recibo del bien o servicio' })).toBeEnabled();

  await page.getByRole('button', { name: 'Consultar en la DIAN' }).click();
  await expect(page.getByRole('table', { name: 'Eventos registrados en la DIAN' })).toContainText(
    'Acuse de recibo de Factura Electrónica de Venta',
  );
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
});

test('a claim asks its reason and the person who decides', async ({ page }) => {
  const api = await mockReceived(page);
  await signInAt(page, '/documentos/recibidos/rd-1');

  for (const action of ['Acuse de recibo', 'Recibo del bien o servicio']) {
    await page.getByRole('button', { name: action, exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Continuar' }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: 'Registrar en la DIAN' })
      .click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  }

  await page.getByRole('button', { name: 'Reclamar' }).click();
  const dialog = page.getByRole('dialog', { name: 'Reclamar la factura' });
  await pickOption(
    page,
    dialog.getByRole('combobox', { name: /Motivo del reclamo/ }),
    '03 · Mercancía no entregada parcialmente',
  );
  await dialog.getByText('Indicar la persona que recibe o decide').click();
  await dialog.getByRole('textbox', { name: /Número de documento/ }).fill('1000000001');
  await dialog.getByRole('textbox', { name: /Nombres/ }).fill('Ana María');
  await dialog.getByRole('textbox', { name: /Apellidos/ }).fill('Pérez Gómez');
  await dialog.getByRole('textbox', { name: /Cargo/ }).fill('Auxiliar contable');
  await dialog.getByRole('textbox', { name: /Área o dependencia/ }).fill('Compras');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await dialog.getByRole('button', { name: 'Continuar' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Registrar en la DIAN' }).click();

  await expect(page.getByText('La DIAN registró el evento «Reclamo de la factura».')).toBeVisible();
  expect(api.eventBodies.at(-1)).toEqual({
    event_code: '031',
    claim_reason: '03',
    issuer_party: {
      identification_number: '1000000001',
      first_name: 'Ana María',
      last_name: 'Pérez Gómez',
      job_title: 'Auxiliar contable',
      organization_department: 'Compras',
    },
  });
  await expect(page.getByRole('button', { name: 'Aceptar', exact: true })).toBeDisabled();
  await expect(
    page.getByText('No disponible: la factura ya tiene «Reclamo de la factura».'),
  ).toBeVisible();
});

test('filters the received invoices by supplier and dates through the URL', async ({ page }) => {
  const api = await mockReceived(page);
  await signInAt(page, '/documentos/recibidos');

  await page.getByRole('searchbox', { name: 'NIT del proveedor' }).fill('800000003');
  await page.getByText('Más filtros').click();
  await typeDate(page.getByRole('combobox', { name: 'Desde' }), '2026-11-01');
  await typeDate(page.getByRole('combobox', { name: 'Hasta' }), '2026-11-30');
  await page.getByRole('button', { name: 'Aplicar filtros' }).click();

  await expect(page).toHaveURL(
    '/documentos/recibidos?proveedor=800000003&desde=2026-11-01&hasta=2026-11-30',
  );
  const last = api.listRequests.at(-1)!;
  expect(last.searchParams.get('supplier_nit')).toBe('800000003');
  expect(last.searchParams.get('from')).toBe('2026-11-01');
  expect(last.searchParams.get('to')).toBe('2026-11-30');
  await expect(page.getByText('Menos filtros')).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
});

test('an accepted own invoice shows its RADIAN events and sends the tacit acceptance', async ({
  page,
}) => {
  const sent: unknown[] = [];
  let ownEvents: object[] = [];
  const invoice = {
    id: 'doc-1',
    full_number: 'SETP990000002',
    kind: 'invoice',
    issue_date: '2026-11-14',
    issue_time: '10:05:00',
    status: 'Accepted',
    email_status: 'Sent',
    cufe: 'b'.repeat(96),
    customer: { name: 'CLIENTE DE PRUEBA SAS', identification_number: '800000001', dv: '1' },
    lines: [],
    taxes: [],
    totals: { payable_amount: '1785000.00' },
  };
  await page.route('**/api/v1/documents/doc-1**', (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/v1/documents/doc-1') {
      return route.fulfill({ json: invoice });
    }
    if (path === '/api/v1/documents/doc-1/dian-events') {
      return route.fulfill({
        json: {
          success: true,
          message: 'Consulta de eventos generada con exito',
          cufe: invoice.cufe,
          events: [
            {
              dian_code: '030',
              description: 'Acuse de recibo',
              date: '2026-11-14',
              time: '11:00:00',
            },
            {
              dian_code: '032',
              description: 'Recibo del bien',
              date: '2026-11-15',
              time: '09:00:00',
            },
          ],
        },
      });
    }
    if (path === '/api/v1/documents/doc-1/events' && request.method() === 'POST') {
      sent.push(request.postDataJSON());
      const event = { ...ACKNOWLEDGEMENT, id: 'ev-34', event_code: '034', number: 'AT1' };
      ownEvents = [event];
      return route.fulfill({ json: { success: true, message: 'ok', cude: event.cude, event } });
    }
    if (path === '/api/v1/documents/doc-1/events') {
      return route.fulfill({ json: { data: ownEvents } });
    }
    return route.fallback();
  });
  await signInAt(page, '/documentos/doc-1');

  await expect(page.getByRole('heading', { name: 'Eventos RADIAN' })).toBeVisible();
  await expect(
    page.getByText('La empresa no ha registrado eventos sobre esta factura.'),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Consultar en la DIAN' }).click();
  await expect(page.getByRole('table', { name: 'Eventos registrados en la DIAN' })).toContainText(
    'Recibo del bien',
  );
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('button', { name: 'Aceptación tácita' }).click();
  const confirm = page.getByRole('alertdialog', {
    name: '¿Registrar «Aceptación tácita» ante la DIAN?',
  });
  await expect(confirm).toContainText('SETP990000002');
  await confirm.getByRole('button', { name: 'Registrar en la DIAN' }).click();

  await expect(page.getByText('La DIAN registró el evento «Aceptación tácita».')).toBeVisible();
  expect(sent).toEqual([{ event_code: '034' }]);
  await expect(page.getByRole('list', { name: 'Historial de eventos RADIAN' })).toContainText(
    'Aceptación tácita (034)',
  );
  await expect(page.getByRole('button', { name: 'Aceptación tácita' })).toBeDisabled();
});
