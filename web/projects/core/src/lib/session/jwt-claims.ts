/**
 * Claims (datos firmados) del JWT (JSON Web Token) de acceso que emite el Api: `sub` (usuario), `role`, `scope`
 * (alcances) y `company_id`. Se leen solo para mostrar u ocultar acciones; el Api valida la firma y es la autoridad.
 */
export interface AccessTokenClaims {
  subject: string | null;
  roles: string[];
  scopes: string[];
  companyId: string | null;
}

const EMPTY_CLAIMS: AccessTokenClaims = { subject: null, roles: [], scopes: [], companyId: null };

/** Lee la parte central del JWT (base64url). Un token mal formado da claims vacíos, nunca una excepción. */
export function readAccessTokenClaims(token: string): AccessTokenClaims {
  const payload = token.split('.')[1];
  if (!payload) {
    return EMPTY_CLAIMS;
  }
  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const bytes = Uint8Array.from(atob(base64), (char) => char.charCodeAt(0));
    const claims: unknown = JSON.parse(new TextDecoder().decode(bytes));
    if (typeof claims !== 'object' || claims === null) {
      return EMPTY_CLAIMS;
    }
    const record = claims as Record<string, unknown>;
    return {
      subject: typeof record['sub'] === 'string' ? record['sub'] : null,
      roles: strings(record['role']),
      scopes: strings(record['scope']),
      companyId: typeof record['company_id'] === 'string' ? record['company_id'] : null,
    };
  } catch {
    return EMPTY_CLAIMS;
  }
}

// Un claim con un solo valor puede llegar como texto en lugar de arreglo.
function strings(value: unknown): string[] {
  const values = Array.isArray(value) ? value : [value];
  return values.filter((item): item is string => typeof item === 'string');
}
