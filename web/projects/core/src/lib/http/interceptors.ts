import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { ApiConfiguration } from '@impifact/api';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { AppError, NETWORK_ERROR_CODE, toAppError } from '../errors/app-error';
import { ToastStore } from '../notifications/toast.store';
import { SessionStore } from '../session/session.store';
import {
  CORRELATION_HEADER,
  IDEMPOTENCY_HEADER,
  IDEMPOTENCY_KEY,
  RETRIED_AFTER_REFRESH,
  SKIP_ERROR_TOAST,
  isApiRequest,
  isSessionRequest,
} from './api-request';

/*
 * Interceptores de HttpClient del panel (sección 6 de la arquitectura). Solo actúan sobre las solicitudes al Api;
 * las demás (textos de i18n, `config.json`) pasan sin cambios.
 */

const UNAUTHORIZED = 401;

/** Pone `X-Correlation-Id` para cruzar una queja con los registros del Api; respeta uno ya puesto. */
export const correlationInterceptor: HttpInterceptorFn = (request, next) => {
  if (
    !isApiRequest(request, inject(ApiConfiguration).rootUrl) ||
    request.headers.has(CORRELATION_HEADER)
  ) {
    return next(request);
  }
  return next(request.clone({ setHeaders: { [CORRELATION_HEADER]: crypto.randomUUID() } }));
};

/** Pone `Idempotency-Key` cuando la solicitud la trae en su contexto (`IDEMPOTENCY_KEY`). */
export const idempotencyInterceptor: HttpInterceptorFn = (request, next) => {
  const key = request.context.get(IDEMPOTENCY_KEY);
  if (!key || request.headers.has(IDEMPOTENCY_HEADER)) {
    return next(request);
  }
  return next(request.clone({ setHeaders: { [IDEMPOTENCY_HEADER]: key } }));
};

/**
 * Adjunta el token Bearer. Las rutas de sesión (`/api/v1/auth/*`) van sin Bearer y con `withCredentials`, para que
 * el navegador mande y guarde la cookie `HttpOnly` de renovación.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const rootUrl = inject(ApiConfiguration).rootUrl;
  if (!isApiRequest(request, rootUrl)) {
    return next(request);
  }
  if (isSessionRequest(request, rootUrl)) {
    return next(request.clone({ withCredentials: true }));
  }
  const token = inject(SessionStore).accessToken();
  return next(
    token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request,
  );
};

/**
 * Ante un 401 renueva la sesión una vez (una sola renovación en vuelo, compartida entre solicitudes y pestañas) y
 * repite la solicitud una vez. Si la renovación falla o la repetición vuelve a dar 401, cierra la sesión.
 * Va antes de `authInterceptor` para que la repetición salga con el token nuevo.
 */
export const refreshInterceptor: HttpInterceptorFn = (request, next) => {
  const rootUrl = inject(ApiConfiguration).rootUrl;
  if (!isApiRequest(request, rootUrl) || isSessionRequest(request, rootUrl)) {
    return next(request);
  }
  const session = inject(SessionStore);
  const sentToken = session.accessToken();
  const endSession = (error: unknown) =>
    from(session.logout()).pipe(switchMap(() => throwError(() => error)));

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== UNAUTHORIZED) {
        return throwError(() => error);
      }
      if (request.context.get(RETRIED_AFTER_REFRESH)) {
        return endSession(error);
      }
      return from(session.refresh(sentToken)).pipe(
        switchMap((renewed) => {
          if (!renewed) {
            return endSession(error);
          }
          const retry = request.clone({
            context: request.context.set(RETRIED_AFTER_REFRESH, true),
          });
          return next(retry).pipe(
            catchError((retryError: unknown) =>
              retryError instanceof HttpErrorResponse && retryError.status === UNAUTHORIZED
                ? endSession(retryError)
                : throwError(() => retryError),
            ),
          );
        }),
      );
    }),
  );
};

/**
 * Convierte cada error del Api en `AppError` y, salvo que el error traiga errores por campo (los muestra el
 * formulario), sea un 401 (lo maneja la sesión) o la solicitud lo pida con `SKIP_ERROR_TOAST`, abre un *toast*
 * con el mensaje y el identificador de soporte.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApiRequest(request, inject(ApiConfiguration).rootUrl)) {
    return next(request);
  }
  const toasts = inject(ToastStore);
  // Transloco se pide solo al armar el mensaje: pedirlo antes crea un ciclo mientras Transloco se construye.
  const injector = inject(Injector);
  const fallbackMessage = (code: string) =>
    injector
      .get(TranslocoService)
      .translate(code === NETWORK_ERROR_CODE ? 'errors.network' : 'errors.unexpected');

  return next(request).pipe(
    catchError((error: unknown) =>
      error instanceof HttpErrorResponse ? readBlobBody(error) : throwError(() => error),
    ),
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      const appError = toAppError(error, fallbackMessage, request.headers.get(CORRELATION_HEADER));
      if (shouldToast(appError, request.context.get(SKIP_ERROR_TOAST))) {
        toasts.show({
          kind: 'error',
          text: appError.message,
          correlationId: appError.correlationId,
        });
      }
      return throwError(() => appError);
    }),
  );
};

/**
 * Las descargas (`responseType: 'blob'`) reciben también el error como archivo; se lee su texto para que
 * `toAppError` encuentre el ProblemDetails. Devuelve siempre un error, nunca un valor.
 */
function readBlobBody(error: HttpErrorResponse): Observable<never> {
  if (!(error.error instanceof Blob)) {
    return throwError(() => error);
  }
  return from(error.error.text().catch(() => '')).pipe(
    switchMap((body) =>
      throwError(
        () =>
          new HttpErrorResponse({
            error: body,
            headers: error.headers,
            status: error.status,
            statusText: error.statusText,
            url: error.url ?? undefined,
          }),
      ),
    ),
  );
}

function shouldToast(error: AppError, skip: boolean): boolean {
  return !skip && error.status !== UNAUTHORIZED && !error.hasFieldErrors;
}

/**
 * Orden para `withInterceptors`: la correlación primero (la repetición conserva el mismo identificador), luego los
 * errores (ven el resultado final, tras renovar y repetir), la renovación y el Bearer al final.
 */
export const coreInterceptors: HttpInterceptorFn[] = [
  correlationInterceptor,
  idempotencyInterceptor,
  errorInterceptor,
  refreshInterceptor,
  authInterceptor,
];
