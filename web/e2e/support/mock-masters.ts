import { Page, Route } from '@playwright/test';

/*
 * Clientes y productos simulados con `page.route` para las pruebas E2E (de extremo a extremo) de feature-masters.
 * Guarda cada consulta recibida para comprobar que la paginación, el orden y la búsqueda viajan al servidor; las
 * respuestas se pueden cambiar por prueba con `respond`.
 */

export interface MastersRequest {
  method: string;
  path: string;
  params: Record<string, string>;
  body: unknown;
}

type Handler = (request: MastersRequest, route: Route) => Promise<void> | void;

export interface MockMasters {
  readonly requests: MastersRequest[];
  /** Solicitudes de un endpoint (`GET /api/v1/customers`, por ejemplo). */
  calls(method: string, path: string): MastersRequest[];
  /** Respuesta propia para `method` + `path` (la ruta exacta, sin la consulta); se usa en lugar de la de serie. */
  respond(method: string, path: string, handler: Handler): void;
}

export const CUSTOMER = {
  id: '6a1f0000-0000-4000-8000-000000000001',
  type_document_identification_id: 6,
  identification_number: '900123456',
  dv: '8',
  name: 'Ana Comercial SAS',
  email: 'facturas@ana.test',
  phone: null,
  address: null,
  municipality_id: 149,
  type_organization_id: 1,
  type_regime_id: 1,
  type_liability_id: 117,
  merchant_registration: null,
  is_active: true,
};

export const PRODUCT = {
  id: '6a1f0000-0000-4000-8000-0000000000a1',
  code: 'CAF-500',
  name: 'Café molido 500 g',
  description: null,
  type_item_identification_id: 4,
  unit_measure_id: 70,
  unit_price: 12500.5,
  tax_id: 1,
  tax_percent: 19,
  tax_per_unit_amount: null,
  is_active: true,
};

export function problem(
  route: Route,
  status: number,
  code: string,
  message: string,
  errors?: Record<string, string[]>,
) {
  return route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ status, code, message, correlation_id: 'e2e-masters', errors }),
  });
}

export async function mockMasters(page: Page): Promise<MockMasters> {
  const requests: MastersRequest[] = [];
  const custom = new Map<string, Handler>();

  const defaults: Record<string, Handler> = {
    'GET /api/v1/customers': (_, route) =>
      route.fulfill({ json: { items: [CUSTOMER], next_cursor: null } }),
    'GET /api/v1/products': (_, route) =>
      route.fulfill({ json: { items: [PRODUCT], next_cursor: null } }),
    [`GET /api/v1/customers/${CUSTOMER.id}`]: (_, route) => route.fulfill({ json: CUSTOMER }),
    [`GET /api/v1/products/${PRODUCT.id}`]: (_, route) => route.fulfill({ json: PRODUCT }),
  };

  await page.route(/\/api\/v1\/(customers|products)(\/|\?|$)/, async (route) => {
    const url = new URL(route.request().url());
    const request: MastersRequest = {
      method: route.request().method(),
      path: url.pathname,
      params: Object.fromEntries(url.searchParams.entries()),
      body: route.request().postDataJSON() ?? null,
    };
    requests.push(request);
    const key = `${request.method} ${request.path}`;
    const handler = custom.get(key) ?? defaults[key];
    if (handler) {
      await handler(request, route);
      return;
    }
    await problem(route, 404, 'http.not_found', 'No encontrado.');
  });

  return {
    requests,
    calls: (method, path) => requests.filter((r) => r.method === method && r.path === path),
    respond: (method, path, handler) => custom.set(`${method} ${path}`, handler),
  };
}
