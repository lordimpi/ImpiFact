import { Page, Route } from '@playwright/test';
import { mockCatalogs } from './mock-catalogs';

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

/** Razón social, NIT y DV de cada empresa simulada (`GET /api/v1/me/companies`). */
export const COMPANY_NAMES: Record<string, { name: string; nit: string; dv: string }> = {
  [COMPANIES[0]!]: { name: 'Acme SAS', nit: '900123456', dv: '7' },
  [COMPANIES[1]!]: { name: 'Beta Ltda', nit: '800111222', dv: '3' },
};

/** Tablero de una empresa nueva: sin documentos, sin plan, sin certificado ni resoluciones por agotarse. */
export const EMPTY_DASHBOARD = {
  generated_at: '2026-11-17T15:00:00Z',
  current_month: { month: '2026-11', total: 0, by_status: [] },
  months: [],
  plan: null,
  certificate: null,
  resolutions: [],
};

/** Estado del paso a producción de una empresa nueva (`GET /api/v1/companies/{nit}/setup-status`). */
export const EMPTY_SETUP_STATUS = {
  company_complete: false,
  certificate_valid: false,
  software_configured: false,
  test_set_id_configured: false,
  test_set_accepted: null,
  environments: [1, 2, 3, 4, 5].map((kind) => ({ document_kind: kind, type_environment_id: 2 })),
  resolutions: [
    { kind: 'invoice', type_document_id: 1, valid: false },
    { kind: 'credit_note', type_document_id: 4, valid: false },
    { kind: 'debit_note', type_document_id: 5, valid: false },
  ],
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

/** Respuesta de login y renovación del Api simulado (también la usan las pruebas que simulan otro usuario). */
export function sessionTokens(
  companyId: string | null,
  companies: string[],
  roles: string[] = ['owner'],
  scopes: string[] = ['documents:read', 'documents:write'],
  email: string = USER.email,
) {
  return {
    access_token: fakeJwt(companyId, roles, scopes),
    token_type: 'Bearer',
    expires_at: new Date(Date.now() + 15 * 60_000).toISOString(),
    refresh_token_expires_at: new Date(Date.now() + 14 * 86_400_000).toISOString(),
    company_id: companyId,
    roles,
    companies,
    email,
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
      json: sessionTokens(session.companyId, companies, options.roles, options.scopes),
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
      json: sessionTokens(session.companyId, companies, options.roles, options.scopes),
    });
  });

  await page.route('**/api/v1/auth/logout', (route) => {
    calls.logout++;
    session = null;
    return route.fulfill({ status: 204 });
  });

  await page.route('**/api/v1/me/companies', (route) => {
    if (!session) {
      return problem(route, 401, 'identity.unauthorized', 'No hay sesión.');
    }
    return route.fulfill({
      json: {
        items: companies.map((id) => ({
          id,
          ...(COMPANY_NAMES[id] ?? { name: 'Empresa sin nombre', nit: '1', dv: '0' }),
          roles: options.roles ?? ['owner'],
          is_active: true,
        })),
      },
    });
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
  // Lecturas de la configuración de la empresa: por defecto, una empresa nueva sin software ni resoluciones.
  await page.route(
    /\/api\/v1\/companies\/\d+(\/(environments|software|resolutions|setup-status))?$/,
    (route) => {
      if (route.request().method() !== 'GET') {
        return route.fallback();
      }
      const path = new URL(route.request().url()).pathname;
      const nit = path.split('/')[4]!;
      const company = Object.values(COMPANY_NAMES).find((entry) => entry.nit === nit);
      if (path.endsWith('/environments')) {
        return route.fulfill({ json: { environments: EMPTY_SETUP_STATUS.environments } });
      }
      if (path.endsWith('/software')) {
        return route.fulfill({ json: { software: [] } });
      }
      if (path.endsWith('/resolutions')) {
        return route.fulfill({ json: { resolutions: [] } });
      }
      if (path.endsWith('/setup-status')) {
        return route.fulfill({ json: EMPTY_SETUP_STATUS });
      }
      return route.fulfill({
        json: {
          identification_number: nit,
          dv: company?.dv ?? '0',
          business_name: company?.name ?? 'Empresa sin nombre',
          type_document_identification_id: 6,
          type_liability_ids: [],
          environments: EMPTY_SETUP_STATUS.environments,
        },
      });
    },
  );
  // Llaves de API y empresas de la plataforma: listas vacías por defecto.
  await page.route(/\/api\/v1\/(api-keys|platform\/companies)(\?|$)/, (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({ json: { items: [], next_cursor: null } })
      : route.fallback(),
  );
  // Catálogos de la DIAN que piden los formularios (empresa, clientes, productos y notas).
  await mockCatalogs(page);

  return { calls, hasSession: () => session !== null };
}
