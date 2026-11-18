import { HttpContextToken, HttpRequest } from '@angular/common/http';

/**
 * Marcas de una solicitud (`HttpContext`) que leen los interceptores de core.
 * `SKIP_ERROR_TOAST`: quien llama muestra el error por su cuenta; el interceptor de errores no abre un *toast*.
 */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/** Solicitud repetida tras renovar la sesión: si vuelve a dar 401 no se renueva otra vez. */
export const RETRIED_AFTER_REFRESH = new HttpContextToken<boolean>(() => false);

/**
 * Llave de idempotencia de una emisión: el feature la genera al abrir el formulario y la pone en el contexto de la
 * solicitud; `idempotencyInterceptor` la manda en el encabezado `Idempotency-Key`. Repetir el envío con la misma
 * llave devuelve el mismo documento en lugar de emitir otro (el cliente generado no expone ese encabezado).
 */
export const IDEMPOTENCY_KEY = new HttpContextToken<string | null>(() => null);

/** Encabezado que lee el Api para no emitir dos veces la misma solicitud. */
export const IDEMPOTENCY_HEADER = 'Idempotency-Key';

/** Encabezado del identificador de correlación (Arquitectura detallada, sección 9.1). */
export const CORRELATION_HEADER = 'X-Correlation-Id';

const API_PATH = '/api/';
const SESSION_PATH = '/api/v1/auth/';

/** Si la solicitud va al Api (y no, por ejemplo, a los textos de i18n o a `config.json`). */
export function isApiRequest(request: HttpRequest<unknown>, rootUrl: string): boolean {
  return request.url.startsWith(`${rootUrl}${API_PATH}`);
}

/**
 * Inicio, renovación y cierre de la sesión del panel: viajan con la cookie de renovación (`withCredentials`), sin
 * token Bearer, y un 401 en ellas significa «sin sesión», no «renueve y reintente».
 */
export function isSessionRequest(request: HttpRequest<unknown>, rootUrl: string): boolean {
  return request.url.startsWith(`${rootUrl}${SESSION_PATH}`);
}
