import { HttpErrorResponse } from '@angular/common/http';

/** Errores por campo que devuelve el Api en las validaciones (`errors` del ProblemDetails). */
export type FieldErrors = Readonly<Record<string, readonly string[]>>;

/**
 * Error del Api ya interpretado. `errorInterceptor` convierte cada respuesta de error en uno de estos, así los
 * features no leen el cuerpo HTTP: muestran `fieldErrors` junto a cada campo y el resto en un *toast*.
 */
export class AppError extends Error {
  constructor(
    /** Código estable del Api (`validation.failed`, `plan.limit_reached`…) o uno local (`network.unavailable`). */
    readonly code: string,
    /** Texto para la persona, en español. */
    message: string,
    /** Código HTTP; 0 si no hubo respuesta (sin red, CORS, servidor caído). */
    readonly status: number,
    readonly fieldErrors: FieldErrors = {},
    /** Identificador de la solicitud para soporte (`X-Correlation-Id`). */
    readonly correlationId: string | null = null,
  ) {
    super(message);
    this.name = 'AppError';
  }

  get hasFieldErrors(): boolean {
    return Object.keys(this.fieldErrors).length > 0;
  }
}

/** Códigos locales, para errores que no traen ProblemDetails. */
export const NETWORK_ERROR_CODE = 'network.unavailable';
export const UNEXPECTED_ERROR_CODE = 'http.unexpected';

/**
 * Convierte la respuesta de error en `AppError`. Lee el ProblemDetails (RFC 9457, el formato estándar de errores en
 * JSON) del Api: `code`, `message` (o `detail`/`title`), `errors` y `correlation_id`; el identificador también
 * puede venir en el encabezado. `fallbackMessage` da el texto cuando el cuerpo no trae uno.
 */
export function toAppError(
  response: HttpErrorResponse,
  fallbackMessage: (code: string) => string,
  sentCorrelationId: string | null = null,
): AppError {
  const body = parseBody(response.error);
  const code =
    text(body['code']) ?? (response.status === 0 ? NETWORK_ERROR_CODE : UNEXPECTED_ERROR_CODE);
  const message =
    text(body['message']) ?? text(body['detail']) ?? text(body['title']) ?? fallbackMessage(code);
  const correlationId =
    text(body['correlation_id']) ?? response.headers?.get('X-Correlation-Id') ?? sentCorrelationId;
  return new AppError(code, message, response.status, fieldErrors(body['errors']), correlationId);
}

function parseBody(error: unknown): Record<string, unknown> {
  if (typeof error === 'string') {
    try {
      return parseBody(JSON.parse(error));
    } catch {
      return {};
    }
  }
  // Solo un objeto JSON plano es un ProblemDetails; sin red llega un ProgressEvent, que no se lee.
  const isPlainObject =
    typeof error === 'object' &&
    error !== null &&
    [Object.prototype, null].includes(Object.getPrototypeOf(error));
  return isPlainObject ? (error as Record<string, unknown>) : {};
}

function fieldErrors(raw: unknown): FieldErrors {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
    return {};
  }
  const entries = Object.entries(raw as Record<string, unknown>).map(([field, value]) => {
    const messages = (Array.isArray(value) ? value : [value]).filter(
      (item): item is string => typeof item === 'string' && item.length > 0,
    );
    return [field, messages] as const;
  });
  return Object.fromEntries(entries.filter(([, messages]) => messages.length > 0));
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}
