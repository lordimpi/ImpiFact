import { Page, Route } from '@playwright/test';
import { COMPANIES, MockApiOptions, USER, mockApi } from '../support/mock-api';

/*
 * Api simulada para las capturas del manual de uso (docs/manual). Se monta sobre `mockApi` (sesión, catálogos y
 * valores por defecto) y registra después sus propias rutas, que Playwright prefiere por ser las últimas: datos
 * realistas pero ficticios (empresas, NIT, clientes y valores en pesos colombianos inventados) para que las
 * pantallas se vean como en el uso diario. Nada de esto llama a la DIAN.
 */

export const COMPANY_ID = COMPANIES[0]!;
export const OTHER_COMPANY_ID = COMPANIES[1]!;
export const NIT = '901458321';
export const DV = '4';
export const COMPANY_NAME = 'Café La Montaña SAS';

const MY_COMPANIES = [
  { id: COMPANY_ID, name: COMPANY_NAME, nit: NIT, dv: DV },
  { id: OTHER_COMPANY_ID, name: 'Distribuidora El Roble Ltda', nit: '900874512', dv: '4' },
];

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, json: body });
}

function problem(route: Route, status: number, code: string, message: string) {
  return route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ status, code, message, correlation_id: 'manual-0001' }),
  });
}

const money = (value: number) => value.toFixed(2);

// ---------------------------------------------------------------------------------------------------------------
// Totales: el mismo cálculo para la emisión, el detalle y la lista (precio por cantidad más el IVA de cada línea).
// ---------------------------------------------------------------------------------------------------------------

interface CalcLine {
  quantity: number;
  unit_price: number;
  free_of_charge?: boolean;
  taxes?: { tax_id: number; percent: number }[];
}

function calculate(lines: CalcLine[]) {
  const perLine = lines.map((line) => {
    const base = line.free_of_charge ? 0 : line.quantity * line.unit_price;
    const subtotals = (line.taxes ?? []).map((tax) => ({
      tax_id: tax.tax_id,
      percent: money(tax.percent),
      tax_amount: money((base * tax.percent) / 100),
      taxable_amount: money(base),
    }));
    return { base, subtotals };
  });
  const grouped = new Map<string, { tax_id: number; percent: string; tax: number; base: number }>();
  for (const line of perLine) {
    for (const subtotal of line.subtotals) {
      const key = `${subtotal.tax_id}-${subtotal.percent}`;
      const entry = grouped.get(key) ?? {
        tax_id: subtotal.tax_id,
        percent: subtotal.percent,
        tax: 0,
        base: 0,
      };
      entry.tax += Number(subtotal.tax_amount);
      entry.base += Number(subtotal.taxable_amount);
      grouped.set(key, entry);
    }
  }
  const subtotals = [...grouped.values()].map((entry) => ({
    tax_id: entry.tax_id,
    percent: entry.percent,
    tax_amount: money(entry.tax),
    taxable_amount: money(entry.base),
  }));
  const base = perLine.reduce((sum, line) => sum + line.base, 0);
  const tax = subtotals.reduce((sum, entry) => sum + Number(entry.tax_amount), 0);
  return {
    allowance_charges: [],
    lines: perLine.map((line) => ({
      allowance_charges: [],
      line_extension_amount: money(line.base),
      tax_subtotals: line.subtotals,
    })),
    tax_totals: subtotals.map((entry) => ({
      tax_id: entry.tax_id,
      tax_amount: entry.tax_amount,
      tax_subtotals: [entry],
    })),
    legal_monetary_totals: {
      line_extension_amount: money(base),
      tax_exclusive_amount: money(base),
      tax_inclusive_amount: money(base + tax),
      allowance_total_amount: '0.00',
      charge_total_amount: '0.00',
      pre_paid_amount: '0.00',
      payable_amount: money(base + tax),
    },
    subtotals,
  };
}

// ---------------------------------------------------------------------------------------------------------------
// Clientes y productos.
// ---------------------------------------------------------------------------------------------------------------

const customer = (
  id: string,
  name: string,
  identification: string,
  dv: string | null,
  email: string | null,
  municipality: number,
  extra: Record<string, unknown> = {},
) => ({
  id,
  type_document_identification_id: dv ? 6 : 3,
  identification_number: identification,
  dv,
  name,
  email,
  phone: '6044441234',
  address: 'Carrera 43A # 1-50',
  municipality_id: municipality,
  type_organization_id: dv ? 1 : 2,
  type_regime_id: 1,
  type_liability_id: 117,
  merchant_registration: null,
  is_active: true,
  ...extra,
});

export const CUSTOMERS = [
  customer(
    'cus-01',
    'Hotel Mirador del Valle SAS',
    '900345678',
    '9',
    'compras@miradordelvalle.example',
    1006,
  ),
  customer(
    'cus-02',
    'Panadería San Telmo SAS',
    '901236547',
    '9',
    'facturas@pansantelmo.example',
    1,
  ),
  customer(
    'cus-03',
    'Supermercados La Ceiba SAS',
    '860512347',
    '8',
    'proveedores@laceiba.example',
    149,
  ),
  customer(
    'cus-04',
    'Juan Pablo Restrepo Gómez',
    '1032456789',
    null,
    'jprestrepo@correo.example',
    1,
  ),
  customer(
    'cus-05',
    'Restaurante Fogón Paisa SAS',
    '901777222',
    '1',
    'admin@fogonpaisa.example',
    1,
  ),
  customer('cus-06', 'Librería Palabras Ltda', '900111333', '1', null, 149, { is_active: false }),
];

const product = (
  id: string,
  code: string,
  name: string,
  price: number,
  unit = 70,
  extra: Record<string, unknown> = {},
) => ({
  id,
  code,
  name,
  description: null,
  type_item_identification_id: 4,
  unit_measure_id: unit,
  unit_price: price,
  tax_id: 1,
  tax_percent: 19,
  tax_per_unit_amount: null,
  is_active: true,
  ...extra,
});

export const PRODUCTS = [
  product('pro-01', 'CAF-G1K', 'Café en grano tostado 1 kg', 68000, 767),
  product('pro-02', 'CAF-M500', 'Café molido 500 g', 32000),
  product('pro-03', 'SRV-CAT', 'Servicio de catación para eventos', 2140000),
  product('pro-04', 'ACC-FIL', 'Filtros de papel x 100', 9500),
  product('pro-05', 'CAF-CAP', 'Cápsulas de café x 10', 18500, 70, { is_active: false }),
];

/** Clientes y productos con búsqueda y estado como el Api; las escrituras responden como guardadas. */
export async function mockMastersData(page: Page) {
  await page.route(/\/api\/v1\/(customers|products)(\/|\?|$)/, (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const isCustomers = url.pathname.startsWith('/api/v1/customers');
    const rows: Record<string, unknown>[] = isCustomers ? CUSTOMERS : PRODUCTS;
    if (url.pathname === '/api/v1/customers/lookup') {
      return json(route, {
        found: true,
        name: 'HOTEL MIRADOR DEL VALLE SAS',
        email: 'compras@miradordelvalle.example',
      });
    }
    if (request.method() !== 'GET') {
      const body = (request.postDataJSON() ?? {}) as Record<string, unknown>;
      return json(route, { id: 'nuevo', is_active: true, ...body }, 201);
    }
    const id = url.pathname.split('/')[4];
    if (id) {
      const row = rows.find((entry) => entry['id'] === id);
      return row ? json(route, row) : problem(route, 404, 'http.not_found', 'No encontrado.');
    }
    const search = (url.searchParams.get('search') ?? url.searchParams.get('q') ?? '')
      .toLowerCase()
      .normalize('NFD')
      .replace(/\p{Diacritic}/gu, '');
    const status = url.searchParams.get('status') ?? 'all';
    const items = rows.filter((row) => {
      const text = `${row['name']} ${row['code'] ?? ''} ${row['identification_number'] ?? ''}`
        .toLowerCase()
        .normalize('NFD')
        .replace(/\p{Diacritic}/gu, '');
      const matchesStatus =
        status === 'all' ||
        (status === 'active' && row['is_active']) ||
        (status === 'inactive' && !row['is_active']);
      return matchesStatus && (!search || text.includes(search));
    });
    return json(route, { items, next_cursor: null });
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Documentos.
// ---------------------------------------------------------------------------------------------------------------

interface DocLine {
  code: string;
  description: string;
  quantity: number;
  price: number;
}

interface ManualDocument {
  id: string;
  full_number: string;
  kind: 'invoice' | 'credit_note' | 'debit_note';
  issue_date: string;
  issue_time: string;
  status: string;
  email_status: string;
  customer: (typeof CUSTOMERS)[number];
  lines: DocLine[];
  dian_status_code?: string | null;
  dian_status_description?: string | null;
  notes?: string | null;
}

const doc = (
  id: string,
  full_number: string,
  kind: ManualDocument['kind'],
  issue_date: string,
  status: string,
  customerIndex: number,
  lines: DocLine[],
  extra: Partial<ManualDocument> = {},
): ManualDocument => ({
  id,
  full_number,
  kind,
  issue_date,
  issue_time: '10:42:15',
  status,
  email_status: status === 'Accepted' || status === 'AcceptedWithWarnings' ? 'Sent' : 'NotSent',
  customer: CUSTOMERS[customerIndex]!,
  lines,
  ...extra,
});

const GRANO = (quantity: number): DocLine => ({
  code: 'CAF-G1K',
  description: 'Café en grano tostado 1 kg',
  quantity,
  price: 68000,
});
const MOLIDO = (quantity: number): DocLine => ({
  code: 'CAF-M500',
  description: 'Café molido 500 g',
  quantity,
  price: 32000,
});
const CATACION: DocLine = {
  code: 'SRV-CAT',
  description: 'Servicio de catación para eventos',
  quantity: 1,
  price: 2140000,
};

export const DOCUMENTS: ManualDocument[] = [
  doc('d-1024', 'CLM1024', 'invoice', '2026-11-19', 'Accepted', 0, [GRANO(20), CATACION], {
    dian_status_code: '00',
    dian_status_description: 'Procesado Correctamente.',
    notes: 'Pedido 4587. Entrega en la recepción del hotel.',
  }),
  doc('d-1023', 'CLM1023', 'invoice', '2026-11-19', 'Sent', 1, [MOLIDO(12)]),
  doc('n-0015', 'NC15', 'credit_note', '2026-11-18', 'Accepted', 2, [MOLIDO(3)], {
    dian_status_code: '00',
    dian_status_description: 'Procesado Correctamente.',
  }),
  doc('d-1022', 'CLM1022', 'invoice', '2026-11-18', 'Rejected', 3, [GRANO(2)], {
    dian_status_code: '99',
    dian_status_description:
      'Validación contiene errores en campos mandatorios. Regla: FAK24, Rechazo: el correo del adquiriente no es válido.',
  }),
  doc('d-1021', 'CLM1021', 'invoice', '2026-11-18', 'AcceptedWithWarnings', 4, [GRANO(5)], {
    dian_status_code: '00',
    dian_status_description: 'Procesado Correctamente con notificaciones.',
  }),
  doc('n-0004', 'ND4', 'debit_note', '2026-11-17', 'Accepted', 0, [
    {
      code: 'INT-MORA',
      description: 'Intereses de mora factura CLM0998',
      quantity: 1,
      price: 45000,
    },
  ]),
  doc('d-1020', 'CLM1020', 'invoice', '2026-11-17', 'Accepted', 2, [MOLIDO(40), GRANO(10)]),
  doc('d-1019', 'CLM1019', 'invoice', '2026-11-15', 'Accepted', 1, [MOLIDO(8)]),
];

/** Factura que se acaba de emitir en el manual: se queda en «Enviado a la DIAN» mientras la DIAN responde. */
const ISSUED: ManualDocument = doc('d-1025', 'CLM1025', 'invoice', '2026-11-19', 'Sent', 0, [
  GRANO(20),
  CATACION,
]);

function documentTotals(document: ManualDocument) {
  return calculate(
    document.lines.map((line) => ({
      quantity: line.quantity,
      unit_price: line.price,
      taxes: [{ tax_id: 1, percent: 19 }],
    })),
  );
}

function summaryOf(document: ManualDocument) {
  return {
    id: document.id,
    full_number: document.full_number,
    kind: document.kind,
    issue_date: document.issue_date,
    status: document.status,
    email_status: document.email_status,
    customer: {
      name: document.customer.name,
      identification_number: document.customer.identification_number,
    },
    totals: documentTotals(document).legal_monetary_totals,
  };
}

function detailOf(document: ManualDocument) {
  const totals = documentTotals(document);
  const accepted = document.status === 'Accepted' || document.status === 'AcceptedWithWarnings';
  return {
    ...summaryOf(document),
    issue_time: document.issue_time,
    cufe: accepted
      ? 'b3f1c2a9e8d74f6a5b4c3d2e1f0a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8'
      : null,
    track_id: document.status === 'Received' ? null : '6f2b9c1d-4e5a-4b7c-9d8e-0a1b2c3d4e5f',
    dian_status_code: document.dian_status_code ?? null,
    dian_status_description: document.dian_status_description ?? null,
    notes: document.notes ?? null,
    customer: document.customer,
    lines: document.lines.map((line, index) => ({
      line_number: index + 1,
      code: line.code,
      description: line.description,
      invoiced_quantity: String(line.quantity),
      price_amount: money(line.price),
      line_extension_amount: money(line.quantity * line.price),
      tax_totals: [
        {
          tax_id: 1,
          percent: '19.00',
          tax_amount: money(line.quantity * line.price * 0.19),
          taxable_amount: money(line.quantity * line.price),
        },
      ],
    })),
    taxes: totals.subtotals,
  };
}

/** PDF de una página con unas líneas de texto (Helvetica, WinAnsi): lo muestra la vista previa. */
export function samplePdf(lines: string[]): Buffer {
  const content = [
    'BT /F1 20 Tf 60 740 Td (Vista previa) Tj ET',
    ...lines.map((line, index) => `BT /F1 12 Tf 60 ${700 - index * 22} Td (${line}) Tj ET`),
  ].join('\n');
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${Buffer.byteLength(content, 'latin1')} >>\nstream\n${content}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((object, index) => {
    offsets.push(Buffer.byteLength(pdf, 'latin1'));
    pdf += `${index + 1} 0 obj\n${object}\nendobj\n`;
  });
  const xref = Buffer.byteLength(pdf, 'latin1');
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  pdf += offsets.map((offset) => `${String(offset).padStart(10, '0')} 00000 n \n`).join('');
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf, 'latin1');
}

const PREVIEW_PDF = samplePdf([
  `${COMPANY_NAME} - NIT ${NIT}-${DV}`,
  'Factura electrónica de venta (borrador)',
  'Cliente: Hotel Mirador del Valle SAS',
  'Total a pagar: $ 4.165.000,00',
]);

function issued(route: Route, document: { id: string; full_number: string }) {
  return json(
    route,
    {
      message: 'Documento recibido',
      QRStr: '',
      urlinvoiceattached: '',
      urlinvoicepdf: '',
      urlinvoicexml: '',
      document: { ...document, status: 'Queued' },
    },
    201,
  );
}

/** Lista con filtros, detalle, cálculo de totales, vista previa, emisión de facturas y notas, y reenvío de correo. */
export async function mockDocumentsData(page: Page) {
  await page.route('**/api/v1/documents**', (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === '/api/v1/documents' && request.method() === 'GET') {
      const kind = url.searchParams.get('kind');
      const statuses = url.searchParams.getAll('status');
      const number = url.searchParams.get('number');
      const search = (url.searchParams.get('customer') ?? '').toLowerCase();
      const items = DOCUMENTS.filter(
        (entry) =>
          (!kind || entry.kind === kind) &&
          (statuses.length === 0 || statuses.includes(entry.status)) &&
          (!number || entry.full_number.includes(number)) &&
          (!search ||
            entry.customer.name.toLowerCase().includes(search) ||
            entry.customer.identification_number.includes(search)),
      ).map(summaryOf);
      return json(route, {
        items,
        next_cursor: null,
        total_count: url.searchParams.has('include_total') ? items.length : null,
      });
    }
    if (path === '/api/v1/documents/calculate') {
      const body = request.postDataJSON() as { lines: CalcLine[] };
      return json(route, calculate(body.lines));
    }
    if (path.endsWith('/preview')) {
      return route.fulfill({ status: 200, contentType: 'application/pdf', body: PREVIEW_PDF });
    }
    if (path === '/api/v1/documents/invoices') {
      return issued(route, ISSUED);
    }
    if (path === '/api/v1/documents/credit-notes') {
      return issued(route, { id: 'n-0016', full_number: 'NC16' });
    }
    if (path === '/api/v1/documents/debit-notes') {
      return issued(route, { id: 'n-0005', full_number: 'ND5' });
    }
    const action =
      /^\/api\/v1\/documents\/([^/]+)\/(email|pdf\/regenerate|attached-document\/regenerate)$/.exec(
        path,
      );
    if (action) {
      return json(route, { success: true, message: 'En cola.' }, 202);
    }
    const id = /^\/api\/v1\/documents\/([^/]+)$/.exec(path)?.[1];
    const found = [...DOCUMENTS, ISSUED].find((entry) => entry.id === id);
    if (found) {
      return json(route, detailOf(found));
    }
    return problem(route, 404, 'document.not_found', 'No existe.');
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Tablero, plan, avisos y llaves de API.
// ---------------------------------------------------------------------------------------------------------------

const HISTORY = [38, 41, 45, 52, 49, 57, 61, 58, 66, 72, 79, 64];

export const DASHBOARD = {
  generated_at: '2026-11-19T15:30:00Z',
  current_month: {
    month: '2026-11',
    total: 64,
    by_status: [
      { status: 'Accepted', count: 58 },
      { status: 'AcceptedWithWarnings', count: 3 },
      { status: 'Sent', count: 1 },
      { status: 'Rejected', count: 2 },
    ],
  },
  months: HISTORY.map((total, index) => {
    const month = new Date(Date.UTC(2025, 11 + index, 1)).toISOString().slice(0, 7);
    const credit = Math.round(total * 0.08);
    const debit = index % 3 === 0 ? 1 : 0;
    return {
      month,
      invoice: total - credit - debit,
      credit_note: credit,
      debit_note: debit,
      other: 0,
      total,
    };
  }),
  plan: {
    name: 'Pyme 300',
    period: 'monthly',
    is_active: true,
    expired: false,
    starts_at: '2026-11-01T05:00:00Z',
    period_end: '2026-12-01T04:59:59Z',
    quotas: [{ kind: 'invoice', used: 61, limit: 300, remaining: 239 }],
  },
  certificate: {
    name: 'CAFE LA MONTANA SAS',
    not_after: '2026-12-07T04:59:59Z',
    days_left: 17,
    expired: false,
  },
  resolutions: [
    {
      id: 'r-1',
      document_type_id: 1,
      kind: 'invoice',
      prefix: 'CLM',
      resolution_number: '18764012345678',
      range_from: 1,
      range_to: 1100,
      next_number: 1026,
      remaining_numbers: 75,
      valid_to: '2027-08-31',
      days_left: 285,
      numbers_low: true,
      expiring: false,
      expired: false,
    },
  ],
};

export const NOTICES = [
  {
    id: 'n-4',
    type: 'document.rejected',
    title: 'La DIAN rechazó un documento',
    body: 'La DIAN rechazó el documento CLM1022. Motivo: Regla: FAK24, Rechazo: el correo del adquiriente no es válido. Corrija el documento y emita uno nuevo.',
    is_read: false,
    created_at: '2026-11-18T16:12:00Z',
  },
  {
    id: 'n-3',
    type: 'certificate.expiring',
    title: 'El certificado de firma vence en 30 días',
    body: 'El certificado de firma «CAFE LA MONTANA SAS» vence el 06/12/2026 (fecha de Colombia). Después de esa fecha no se pueden firmar ni enviar documentos a la DIAN. Suba el certificado nuevo desde el panel antes de que venza.',
    is_read: false,
    created_at: '2026-11-06T13:00:00Z',
  },
  {
    id: 'n-2',
    type: 'resolution.numbers_low',
    title: 'Quedan pocos números en la resolución 18764012345678 con prefijo CLM',
    body: 'Quedan 100 de los 1100 números autorizados (del 1 al 1100). Solicite a la DIAN una nueva resolución de numeración y regístrela antes de que se agoten.',
    is_read: false,
    created_at: '2026-11-04T13:00:00Z',
  },
  {
    id: 'n-1',
    type: 'document.unconfirmed',
    title: 'Un documento quedó sin confirmar en la DIAN',
    body: 'La DIAN no confirmó el estado del documento CLM0987 en las 24 horas siguientes a su envío.',
    is_read: true,
    created_at: '2026-10-28T19:45:00Z',
  },
];

export const API_KEYS = [
  {
    id: 'k-2',
    name: 'Tienda en línea',
    key_prefix: 'ifk_live_7Hq2',
    scopes: ['documents:read', 'documents:write'],
    created_at: '2026-09-02T15:00:00Z',
    expires_at: null,
    last_used_at: '2026-11-19T14:58:00Z',
    is_revoked: false,
    revoked_at: null,
  },
  {
    id: 'k-1',
    name: 'ERP contable',
    key_prefix: 'ifk_live_pX9m',
    scopes: ['documents:read'],
    created_at: '2026-03-10T15:00:00Z',
    expires_at: '2026-12-31T04:59:59Z',
    last_used_at: '2026-11-18T22:10:00Z',
    is_revoked: false,
    revoked_at: null,
  },
  {
    id: 'k-0',
    name: 'Integración anterior',
    key_prefix: 'ifk_live_a01B',
    scopes: ['documents:read'],
    created_at: '2025-11-03T15:00:00Z',
    expires_at: null,
    last_used_at: '2026-02-01T12:00:00Z',
    is_revoked: true,
    revoked_at: '2026-03-10T15:05:00Z',
  },
];

export const NEW_API_KEY = 'ifk_live_Qm4T8vZr2Lw9Xc6Hn1Bp5Yd3Kf7Gs0Je';

export async function mockOwnerData(page: Page) {
  await page.route('**/api/v1/dashboard/summary', (route) => json(route, DASHBOARD));
  await page.route(/\/api\/v1\/notifications(\/|\?|$)/, (route) => {
    if (route.request().method() !== 'GET') {
      return json(route, { marked_count: 0 });
    }
    const url = new URL(route.request().url());
    const unreadOnly = url.searchParams.get('unread') === 'true';
    const limit = Number(url.searchParams.get('limit') ?? '20');
    const items = NOTICES.filter((notice) => !unreadOnly || !notice.is_read).slice(0, limit);
    return json(route, {
      items,
      next_cursor: null,
      unread_count: NOTICES.filter((n) => !n.is_read).length,
    });
  });
  const keys = [...API_KEYS];
  await page.route(/\/api\/v1\/api-keys(\?|$)/, (route) => {
    if (route.request().method() === 'GET') {
      return json(route, { items: keys, next_cursor: null });
    }
    const body = route.request().postDataJSON() as {
      name: string;
      scopes: string[];
      expires_at: string | null;
    };
    keys.unshift({
      ...API_KEYS[0]!,
      id: 'k-3',
      name: body.name,
      key_prefix: NEW_API_KEY.slice(0, 13),
      scopes: body.scopes,
      created_at: '2026-11-19T15:30:00Z',
      expires_at: body.expires_at,
      last_used_at: null,
    });
    return json(
      route,
      {
        id: 'k-3',
        company_id: COMPANY_ID,
        name: body.name,
        key: NEW_API_KEY,
        key_prefix: NEW_API_KEY.slice(0, 13),
        scopes: body.scopes,
        expires_at: body.expires_at,
      },
      201,
    );
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Configuración de la empresa: guarda lo que recibe y lo devuelve en las lecturas, como el Api.
// ---------------------------------------------------------------------------------------------------------------

const INVOICE_KIND = 1;
const HABILITATION = 2;
const TEST_SET_ID = '7c1e5a92-3b4d-4f60-9a8b-2d6e0f1c5a37';

export interface CompanyConfig {
  environment: number;
  resolutions: Record<string, unknown>[];
  testSetAccepted: boolean | null;
}

export async function mockCompanyConfig(page: Page): Promise<CompanyConfig> {
  const state: CompanyConfig = {
    environment: HABILITATION,
    resolutions: [],
    testSetAccepted: null,
  };
  const company = {
    id: COMPANY_ID,
    identification_number: NIT,
    dv: DV,
    business_name: COMPANY_NAME,
    type_document_identification_id: 6,
    type_organization_id: 1,
    type_regime_id: 1,
    type_liability_ids: [9],
    municipality_id: 1,
    address: 'Calle 10 # 43D-25, oficina 302',
    phone: '6043217890',
    email: 'facturacion@cafelamontana.example',
    merchant_registration: '21-654321-12',
    is_active: true,
  };
  const environments = () =>
    [1, 2, 3, 4, 5].map((kind) => ({
      document_kind: kind,
      type_environment_id: kind === INVOICE_KIND ? state.environment : HABILITATION,
    }));
  const certificate = {
    id: 'cert-2',
    name: 'cafe-la-montana-2026.p12',
    subject: 'CN=CAFE LA MONTANA SAS, SERIALNUMBER=901458321',
    issuer: 'CN=Entidad de Certificación Digital de Ejemplo',
    expiration_date: '2027-07-01',
    certificate_days_left: 224,
    is_active: true,
  };
  const replaced = {
    id: 'cert-1',
    name: 'cafe-la-montana-2025.p12',
    subject: 'CN=CAFE LA MONTANA SAS, SERIALNUMBER=901458321',
    issuer: 'CN=Entidad de Certificación Digital de Ejemplo',
    expiration_date: '2026-07-15',
    certificate_days_left: 0,
    is_active: false,
  };
  const software = {
    document_kind: INVOICE_KIND,
    type_environment_id: HABILITATION,
    identifier: 'a8f3c2d1-5e6b-4a7c-8d9e-0f1a2b3c4d5e',
    url: 'https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc',
    test_set_id: TEST_SET_ID,
    has_pin: true,
  };
  let testSetCalls = 0;

  await page.route(`**/api/v1/companies/${NIT}/**`, (route) => {
    const request = route.request();
    const method = request.method();
    const path = new URL(request.url()).pathname.replace(`/api/v1/companies/${NIT}`, '');
    if (method === 'GET' && path === '/certificates') {
      return json(route, { success: true, certificates: [certificate, replaced] });
    }
    if (method === 'GET' && path === '/software') {
      return json(route, { software: [software] });
    }
    if (method === 'GET' && path === '/environments') {
      return json(route, { environments: environments() });
    }
    if (method === 'PUT' && path === '/environment') {
      const body = request.postDataJSON() as { type_environment_id?: number };
      state.environment = body.type_environment_id ?? state.environment;
      return json(route, { success: true, environments: environments() });
    }
    if (method === 'PUT' && path === '/resolution') {
      state.resolutions = [{ id: 'res-1', ...request.postDataJSON() }];
      return json(route, { success: true, resolution: state.resolutions[0] });
    }
    if (method === 'GET' && path === '/resolutions') {
      return json(route, {
        resolutions: state.resolutions.map((entry) => ({
          ...entry,
          kind: 'invoice',
          is_active: true,
          next_number: 990000000,
          remaining: 5000001,
          days_left: 1158,
          has_technical_key: true,
          technical_key: undefined,
        })),
      });
    }
    if (method === 'GET' && path === '/setup-status') {
      return json(route, {
        company_complete: true,
        certificate_valid: true,
        software_configured: true,
        test_set_id_configured: true,
        test_set_accepted: state.testSetAccepted,
        environments: environments(),
        resolutions: [
          { kind: 'invoice', type_document_id: 1, valid: state.resolutions.length > 0 },
          { kind: 'credit_note', type_document_id: 4, valid: false },
          { kind: 'debit_note', type_document_id: 5, valid: false },
        ],
      });
    }
    if (method === 'PUT') {
      return json(route, { success: true, company: { ...company, environments: environments() } });
    }
    return route.fallback();
  });
  await page.route(`**/api/v1/companies/${NIT}`, (route) =>
    json(route, { ...company, environments: environments() }),
  );
  await page.route(`**/api/v1/documents/invoices/test-set/${TEST_SET_ID}`, (route) =>
    json(
      route,
      {
        message: 'Documento recibido',
        QRStr: '',
        urlinvoiceattached: '',
        urlinvoicepdf: '',
        urlinvoicexml: '',
        document: { id: 'test-1', full_number: 'SETP990000000', status: 'Queued' },
      },
      201,
    ),
  );
  await page.route('**/api/v1/documents/test-1', (route) => {
    const status = testSetCalls++ === 0 ? 'Queued' : 'Accepted';
    if (status === 'Accepted') {
      state.testSetAccepted = true;
    }
    return json(route, {
      id: 'test-1',
      kind: 'invoice',
      full_number: 'SETP990000000',
      status,
      dian_status_code: status === 'Accepted' ? '00' : null,
      dian_status_description: status === 'Accepted' ? 'Procesado Correctamente.' : null,
    });
  });
  return state;
}

// ---------------------------------------------------------------------------------------------------------------
// Plataforma.
// ---------------------------------------------------------------------------------------------------------------

export const PLANS = [
  { id: 1, name: 'Pyme 300', period: 1, qty_docs_invoice: 300, state: true },
  { id: 2, name: 'Emprendedor 50', period: 1, qty_docs_invoice: 50, state: true },
  {
    id: 3,
    name: 'Anual 5000',
    period: 2,
    qty_docs_invoice: 5000,
    qty_docs_payroll: 600,
    state: true,
  },
  { id: 4, name: 'Paquete 1000', period: 3, qty_docs_invoice: 1000, state: true },
  { id: 5, name: 'Promoción 2025', period: 1, qty_docs_invoice: 100, state: false },
];

export const PLATFORM_COMPANIES = [
  {
    id: COMPANY_ID,
    name: COMPANY_NAME,
    nit: NIT,
    dv: DV,
    email: 'facturacion@cafelamontana.example',
    is_active: true,
    plan_id: 1,
    plan_name: 'Pyme 300',
  },
  {
    id: OTHER_COMPANY_ID,
    name: 'Distribuidora El Roble Ltda',
    nit: '900874512',
    dv: '4',
    email: 'contabilidad@elroble.example',
    is_active: true,
    plan_id: 3,
    plan_name: 'Anual 5000',
  },
  {
    id: '3f2a9c1e-0000-4000-8000-000000000003',
    name: 'Ferretería El Tornillo SAS',
    nit: '901236547',
    dv: '9',
    email: 'ventas@eltornillo.example',
    is_active: true,
    plan_id: null,
    plan_name: null,
  },
];

export const NEW_COMPANY_KEY = 'ifk_live_N3wC0mp4nyK3y8s5Tq1Wz7Rb2Lm9Xd4';

export async function mockPlatformData(page: Page) {
  const companies = [...PLATFORM_COMPANIES];
  await page.route('**/api/v1/plans', (route) =>
    route.request().method() === 'GET'
      ? json(route, { success: true, data: PLANS })
      : json(route, { success: true, plan: { id: 6, name: 'Nuevo' } }, 201),
  );
  await page.route(/\/api\/v1\/platform\/companies(\?|$)/, (route) => {
    const search = (new URL(route.request().url()).searchParams.get('search') ?? '').toLowerCase();
    const items = companies.filter(
      (entry) => !search || entry.name.toLowerCase().includes(search) || entry.nit.includes(search),
    );
    return json(route, { items, next_cursor: null });
  });
  await page.route(/\/api\/v1\/companies\/\d+\/\d$/, (route) => {
    if (route.request().method() !== 'POST') {
      return route.fallback();
    }
    const body = route.request().postDataJSON() as Record<string, unknown>;
    return json(
      route,
      {
        success: true,
        created: true,
        message: 'Empresa creada.',
        token: NEW_COMPANY_KEY,
        company: {
          id: '3f2a9c1e-0000-4000-8000-000000000009',
          identification_number: '900345678',
          dv: '9',
          business_name: body['business_name'],
          environments: [],
        },
      },
      201,
    );
  });
  await page.route('**/api/v1/companies/*/plan', (route) =>
    json(route, { success: true, plan: PLANS[0], start_plan_date: '2026-11-19T15:30:00Z' }),
  );
}

// ---------------------------------------------------------------------------------------------------------------
// Usuarios de la empresa: los de Café La Montaña SAS (la dueña que inicia sesión es `user-1`, el `sub` del token);
// las demás empresas no tienen usuarios. Los cambios y los usuarios nuevos quedan en la lista, como en el Api.
// ---------------------------------------------------------------------------------------------------------------

interface CompanyUser {
  id: string;
  email: string;
  name: string;
  phone: string | null;
  role: 'owner' | 'seller';
  is_active: boolean;
}

export const COMPANY_USERS: CompanyUser[] = [
  {
    id: 'user-1',
    email: USER.email,
    name: 'Ana María Gómez',
    phone: '3001234567',
    role: 'owner',
    is_active: true,
  },
  {
    id: 'user-2',
    email: 'carlos@cafelamontana.example',
    name: 'Carlos Andrés Ruiz',
    phone: '3104567890',
    role: 'seller',
    is_active: true,
  },
  {
    id: 'user-3',
    email: 'diana@cafelamontana.example',
    name: 'Diana Peña Ospina',
    phone: null,
    role: 'seller',
    is_active: false,
  },
];

export async function mockCompanyUsers(page: Page) {
  const users = COMPANY_USERS.map((user) => ({ ...user }));
  await page.route(/\/api\/v1\/companies\/\d+\/users(\/[^/?]+)?(\?|$)/, (route) => {
    const request = route.request();
    const [, nit, userId] = /\/companies\/(\d+)\/users(?:\/([^/?]+))?/.exec(request.url())!;
    const rows = nit === NIT ? users : [];
    if (!userId) {
      return json(route, { items: rows });
    }
    const user = rows.find((entry) => entry.id === userId);
    if (!user || request.method() !== 'PATCH') {
      return problem(route, 404, 'user.not_found', 'No encontrado.');
    }
    Object.assign(user, request.postDataJSON() as Partial<CompanyUser>);
    return json(route, user);
  });
  await page.route('**/api/v1/users', (route) => {
    const body = route.request().postDataJSON() as Omit<CompanyUser, 'id' | 'is_active'> & {
      company_id: string;
    };
    const created = { id: `user-${users.length + 1}`, ...body };
    if (body.company_id === COMPANY_ID) {
      users.push({
        id: created.id,
        email: body.email,
        name: body.name,
        phone: body.phone,
        role: body.role,
        is_active: true,
      });
    }
    return json(
      route,
      {
        id: created.id,
        email: body.email,
        name: body.name,
        role: body.role,
        company_id: body.company_id,
      },
      201,
    );
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Todo junto.
// ---------------------------------------------------------------------------------------------------------------

/** `mockApi` con las empresas del manual y los datos de todas las pantallas. */
export async function mockManualApi(page: Page, options: MockApiOptions = {}) {
  const api = await mockApi(page, {
    companies: [COMPANY_ID],
    roles: ['owner'],
    scopes: ['documents:read', 'documents:write', 'config:write'],
    ...options,
  });
  const companies = options.companies ?? [COMPANY_ID];
  await page.route('**/api/v1/me/companies', (route) =>
    json(route, {
      items: MY_COMPANIES.filter((entry) => companies.includes(entry.id)).map((entry) => ({
        ...entry,
        roles: options.roles ?? ['owner'],
        is_active: true,
      })),
    }),
  );
  await mockOwnerData(page);
  await mockMastersData(page);
  await mockDocumentsData(page);
  const config = await mockCompanyConfig(page);
  await mockPlatformData(page);
  await mockCompanyUsers(page);
  return { api, config };
}
