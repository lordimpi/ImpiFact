import { Page, Route } from '@playwright/test';

/*
 * Api simulada con `page.route` para las pruebas E2E (de extremo a extremo) del panel: no hace falta el backend en
 * el CI. `config.json` apunta al mismo origen (`apiBaseUrl: ""`), así las respuestas simuladas no pasan por CORS.
 * Imita la sesión del panel: la renovación responde 200 solo después de un login correcto (como la cookie
 * `impifact_refresh`) y el logout la revoca.
 */

export const USER = { email: 'ana@example.test', password: 'clave-correcta-123' };
export const COMPANIES = [
  '3f2a9c1e-0000-4000-8000-000000000001',
  '3f2a9c1e-0000-4000-8000-000000000002',
];

/** Tablero de una empresa nueva: sin documentos, sin plan, sin certificado ni resoluciones por agotarse. */
export const EMPTY_DASHBOARD = {
  generated_at: '2026-11-17T15:00:00Z',
  current_month: { month: '2026-11', total: 0, by_status: [] },
  months: [],
  plan: null,
  certificate: null,
  resolutions: [],
};

export interface MockApiOptions {
  /** Minutos de inactividad del `config.json` simulado (por defecto, los de producción). */
  inactivity?: { warnMinutes: number; logoutMinutes: number };
  /** Empresas del usuario; con una sola, el Api la deja activa al iniciar sesión. */
  companies?: string[];
  /** Roles y alcances del token (por defecto, propietario con `documents:read` y `documents:write`). */
  roles?: string[];
  scopes?: string[];
}

export interface MockApi {
  /** Cuerpos JSON recibidos por endpoint (`login`, `refresh`, `logout`). */
  readonly calls: { login: unknown[]; refresh: unknown[]; logout: number };
  /** Si la «cookie» de renovación sigue viva. */
  hasSession(): boolean;
}

function base64Url(value: unknown): string {
  return Buffer.from(JSON.stringify(value)).toString('base64url');
}

/** JWT sin firma válida: el panel no verifica firmas (lo hace el Api). */
function fakeJwt(companyId: string | null, roles: string[], scopes: string[]): string {
  const claims = { sub: 'user-1', role: roles, scope: scopes, company_id: companyId };
  return `${base64Url({ alg: 'ES256', typ: 'JWT' })}.${base64Url(claims)}.firma`;
}

function tokens(
  companyId: string | null,
  companies: string[],
  roles: string[] = ['owner'],
  scopes: string[] = ['documents:read', 'documents:write'],
) {
  return {
    access_token: fakeJwt(companyId, roles, scopes),
    token_type: 'Bearer',
    expires_at: new Date(Date.now() + 15 * 60_000).toISOString(),
    refresh_token_expires_at: new Date(Date.now() + 14 * 86_400_000).toISOString(),
    company_id: companyId,
    roles,
    companies,
  };
}

function problem(route: Route, status: number, code: string, message: string) {
  return route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ status, code, message, correlation_id: 'e2e-correlation' }),
  });
}

export async function mockApi(page: Page, options: MockApiOptions = {}): Promise<MockApi> {
  const companies = options.companies ?? COMPANIES;
  const calls: MockApi['calls'] = { login: [], refresh: [], logout: 0 };
  let session: { companyId: string | null } | null = null;

  await page.route('**/config.json', (route) =>
    route.fulfill({
      json: {
        apiBaseUrl: '',
        inactivity: options.inactivity ?? { warnMinutes: 25, logoutMinutes: 30 },
        features: {},
      },
    }),
  );

  await page.route('**/api/v1/auth/login', (route) => {
    const body = route.request().postDataJSON() as { email: string; password: string };
    calls.login.push(body);
    if (body.email !== USER.email || body.password !== USER.password) {
      return problem(route, 401, 'identity.invalid_credentials', 'Credenciales inválidas.');
    }
    // Como el Api: con una sola empresa queda activa; con varias, la persona elige.
    session = { companyId: companies.length === 1 ? companies[0]! : null };
    return route.fulfill({
      json: tokens(session.companyId, companies, options.roles, options.scopes),
    });
  });

  await page.route('**/api/v1/auth/refresh', (route) => {
    const body = (route.request().postDataJSON() ?? null) as { company_id?: string } | null;
    calls.refresh.push(body);
    if (!session) {
      return problem(route, 401, 'identity.invalid_refresh_token', 'No hay sesión.');
    }
    if (body?.company_id) {
      if (!companies.includes(body.company_id)) {
        return problem(route, 403, 'identity.company_not_allowed', 'No pertenece a la empresa.');
      }
      session.companyId = body.company_id;
    }
    return route.fulfill({
      json: tokens(session.companyId, companies, options.roles, options.scopes),
    });
  });

  await page.route('**/api/v1/auth/logout', (route) => {
    calls.logout++;
    session = null;
    return route.fulfill({ status: 204 });
  });

  // La campana del encabezado y el tablero de `/` piden datos al entrar: por defecto, sin avisos y un tablero vacío.
  // Una prueba que necesite otros datos registra su propia ruta después (Playwright usa la última registrada).
  await page.route(/\/api\/v1\/notifications(\/|\?|$)/, (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({ json: { items: [], next_cursor: null, unread_count: 0 } })
      : route.fulfill({ json: { marked_count: 0 } }),
  );
  await page.route('**/api/v1/dashboard/summary', (route) =>
    route.fulfill({ json: EMPTY_DASHBOARD }),
  );

  return { calls, hasSession: () => session !== null };
}
