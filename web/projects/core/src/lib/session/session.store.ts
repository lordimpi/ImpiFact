import { HttpContext } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Api, PanelSessionTokens, login, logout, refreshSession } from '@impifact/api';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { SKIP_ERROR_TOAST } from '../http/api-request';
import { readAccessTokenClaims } from './jwt-claims';
import { CORE_ROUTE_PATHS } from './route-paths';
import { LogoutReason, SessionMessage, SessionSync } from './session-sync';

/**
 * - `unknown`: todavía no se intentó recuperar la sesión.
 * - `restoring`: se está pidiendo la renovación inicial.
 * - `authenticated` / `anonymous`: hay o no hay sesión.
 */
export type SessionStatus = 'unknown' | 'restoring' | 'authenticated' | 'anonymous';

export interface SessionUser {
  id: string;
  /** Correo con el que se inició sesión en esta pestaña; el JWT no lo trae, así que puede faltar. */
  email: string | null;
}

export interface LoginCredentials {
  email: string;
  password: string;
  /** Empresa con la que se entra; sin ella el Api elige o deja la sesión sin empresa activa. */
  companyId?: string | null;
}

export interface LogoutOptions {
  /** Motivo del cierre cuando no lo pidió la persona; el login lo muestra. */
  reason?: LogoutReason;
}

interface SessionState {
  status: SessionStatus;
  /** JWT de acceso: solo en memoria, nunca en `localStorage` ni `sessionStorage` (sección 6 de la arquitectura). */
  accessToken: string | null;
  userId: string | null;
  email: string | null;
  roles: string[];
  scopes: string[];
  /** Empresa activa de la sesión (claim `company_id`). */
  companyId: string | null;
  /** Empresas a las que tiene acceso el usuario. */
  companies: string[];
}

const ANONYMOUS: Omit<SessionState, 'status'> = {
  accessToken: null,
  userId: null,
  email: null,
  roles: [],
  scopes: [],
  companyId: null,
  companies: [],
};

/** Encabezado que pide la sesión del panel: el Api deja la renovación en la cookie `HttpOnly`. */
const PANEL_CLIENT = { 'X-Client': 'panel' } as const;
const CONFLICT = 409;
const UNAUTHORIZED = 401;

/**
 * Sesión del panel (NgRx SignalStore). La renovación vive en la cookie `impifact_refresh`, que el navegador manda
 * solo a `/api/v1/auth` (los interceptores ponen `withCredentials` ahí); el panel nunca la ve.
 *
 * Renovación: una sola en vuelo por pestaña y, con el candado de `SessionSync`, una sola a la vez entre pestañas.
 * La pestaña que renueva avisa a las demás con el token nuevo; si una pestaña esperaba el candado y mientras tanto
 * recibió ese token, no renueva otra vez.
 */
export const SessionStore = signalStore(
  { providedIn: 'root' },
  withState<SessionState>({ status: 'unknown', ...ANONYMOUS }),
  withProps(() => ({
    _api: inject(Api),
    _router: inject(Router),
    _paths: inject(CORE_ROUTE_PATHS),
    _sync: inject(SessionSync),
    _silent: () => new HttpContext().set(SKIP_ERROR_TOAST, true),
  })),
  withComputed((store) => ({
    isAuthenticated: computed(() => store.status() === 'authenticated'),
    user: computed<SessionUser | null>(() => {
      const id = store.userId();
      return id === null ? null : { id, email: store.email() };
    }),
  })),
  withMethods((store) => {
    let refreshing: Promise<boolean> | null = null;
    let restoring: Promise<void> | null = null;
    let loggingOut: Promise<void> | null = null;
    // Generación de la sesión: cambia cada vez que se limpia. Una renovación que empezó en otra generación (por
    // ejemplo, antes de un cierre de sesión aquí o en otra pestaña) descarta su resultado: no revive la sesión.
    let generation = 0;
    // La sesión se cerró con logout (aquí o en otra pestaña) y no hubo otra después: un logout más no hace nada.
    let closed = false;

    function apply(tokens: PanelSessionTokens, email: string | null): void {
      const claims = readAccessTokenClaims(tokens.access_token);
      closed = false;
      patchState(store, {
        status: 'authenticated',
        accessToken: tokens.access_token,
        userId: claims.subject,
        email,
        roles: tokens.roles.length > 0 ? [...tokens.roles] : claims.roles,
        scopes: claims.scopes,
        companyId: tokens.company_id,
        companies: [...tokens.companies],
      });
    }

    function clear(): void {
      generation++;
      patchState(store, { status: 'anonymous', ...ANONYMOUS });
    }

    function goToLogin(reason?: LogoutReason): void {
      const query = reason ? `?reason=${reason}` : '';
      void store._router.navigateByUrl(`${store._paths.login}${query}`);
    }

    /** Registra la renovación en vuelo (la esperan `logout` y los demás `refresh`) y la suelta al terminar. */
    function track(task: Promise<boolean>): Promise<boolean> {
      const tracked: Promise<boolean> = task.finally(() => {
        if (refreshing === tracked) {
          refreshing = null;
        }
      });
      refreshing = tracked;
      return tracked;
    }

    async function callRefresh(retryOnConflict: boolean, startedIn: number): Promise<boolean> {
      try {
        const tokens: PanelSessionTokens = await firstValueFrom(
          store._api.invoke(refreshSession, PANEL_CLIENT, store._silent()),
        );
        if (generation !== startedIn) {
          return false;
        }
        // The refresh carries the email: after a page reload it is the only source (the JWT has none).
        apply(tokens, tokens.email ?? store.email());
        store._sync.post({ type: 'session', tokens, email: store.email() });
        return true;
      } catch (error) {
        // 409: otra renovación con la misma cookie llegó primero (por ejemplo, un navegador sin Web Locks). La
        // cookie ya quedó renovada, así que un segundo intento sale bien.
        if (generation !== startedIn) {
          return false;
        }
        if (retryOnConflict && statusOf(error) === CONFLICT) {
          return callRefresh(false, startedIn);
        }
        clear();
        return false;
      }
    }

    function refresh(staleToken: string | null): Promise<boolean> {
      if (refreshing) {
        return refreshing;
      }
      const startedIn = generation;
      return track(
        store._sync.exclusive(() => {
          if (generation !== startedIn) {
            return Promise.resolve(false);
          }
          const current = store.accessToken();
          return current !== null && current !== staleToken
            ? Promise.resolve(true)
            : callRefresh(true, startedIn);
        }),
      );
    }

    /**
     * Renovación con `company_id`: el Api emite un token con esa empresa activa (si el usuario pertenece a ella).
     * Un 403 deja la sesión como estaba; un 401 la cierra. En ambos casos el error queda en `failure`.
     */
    async function callSwitch(
      companyId: string,
      retryOnConflict: boolean,
      startedIn: number,
      failure: { error?: unknown },
    ): Promise<boolean> {
      try {
        const tokens: PanelSessionTokens = await firstValueFrom(
          store._api.invoke(
            refreshSession,
            { ...PANEL_CLIENT, body: { company_id: companyId, refresh_token: null } },
            store._silent(),
          ),
        );
        if (generation !== startedIn) {
          return false;
        }
        // The refresh carries the email: after a page reload it is the only source (the JWT has none).
        apply(tokens, tokens.email ?? store.email());
        store._sync.post({ type: 'session', tokens, email: store.email() });
        return true;
      } catch (error) {
        if (generation !== startedIn) {
          return false;
        }
        if (retryOnConflict && statusOf(error) === CONFLICT) {
          return callSwitch(companyId, false, startedIn, failure);
        }
        failure.error = error;
        if (statusOf(error) === UNAUTHORIZED) {
          clear();
        }
        return store.isAuthenticated();
      }
    }

    function onMessage(message: SessionMessage): void {
      if (message.type === 'session') {
        apply(message.tokens, message.email ?? store.email());
      } else if (message.type === 'logout') {
        clear();
        closed = true;
        goToLogin(message.reason);
      }
    }

    return {
      _onMessage: onMessage,

      hasRole(...roles: string[]): boolean {
        const current = store.roles();
        return roles.some((role) => current.includes(role));
      },

      hasScope(scope: string): boolean {
        return store.scopes().includes(scope);
      },

      /** Inicia sesión. Si falla lanza el `AppError` que deja `errorInterceptor` (el formulario lo muestra). */
      async login(credentials: LoginCredentials): Promise<void> {
        const tokens: PanelSessionTokens = await firstValueFrom(
          store._api.invoke(
            login,
            {
              ...PANEL_CLIENT,
              body: {
                email: credentials.email,
                password: credentials.password,
                company_id: credentials.companyId ?? null,
              },
            },
            store._silent(),
          ),
        );
        apply(tokens, tokens.email ?? credentials.email);
        store._sync.post({ type: 'session', tokens, email: store.email() });
      },

      /**
       * Recupera la sesión al arrancar con una renovación (la cookie sigue en el navegador tras recargar). Se hace
       * una sola vez; los guards la esperan. Un 401 deja la sesión anónima sin errores ni navegación.
       */
      restore(): Promise<void> {
        if (store.status() === 'authenticated' || store.status() === 'anonymous') {
          return Promise.resolve();
        }
        if (!restoring) {
          patchState(store, { status: 'restoring' });
          restoring = refresh(null).then(() => undefined);
        }
        return restoring;
      },

      /**
       * Renueva el token. `staleToken` es el token con el que falló la solicitud: si al obtener el candado ya hay
       * otro (lo trajo otra pestaña), no se renueva. Devuelve si hay sesión; nunca lanza.
       */
      refresh,

      /**
       * Cambia la empresa activa con una renovación que lleva `company_id` (sección 6 de la arquitectura); las demás
       * pestañas reciben la sesión nueva. Espera la renovación en vuelo y comparte el candado entre pestañas, así
       * nunca hay dos renovaciones con la misma cookie. Si el Api la rechaza lanza su error: con 403 la sesión sigue
       * con la empresa anterior y con 401 queda cerrada. Un cierre de sesión mientras tanto descarta la respuesta.
       */
      async selectCompany(companyId: string): Promise<void> {
        const previous = refreshing;
        const failure: { error?: unknown } = {};
        await track(
          (async () => {
            if (previous) {
              await previous;
            }
            const startedIn = generation;
            return store._sync.exclusive(() =>
              generation === startedIn && store.isAuthenticated()
                ? callSwitch(companyId, true, startedIn, failure)
                : Promise.resolve(false),
            );
          })(),
        );
        if (failure.error !== undefined) {
          throw failure.error;
        }
      },

      /**
       * Cierra la sesión: la limpia aquí al instante, avisa a las demás pestañas, va al login y revoca la cookie en el
       * Api. Si hay una renovación en vuelo, la espera antes de llamar a `/auth/logout`: esa renovación pudo rotar la
       * cookie, y hay que revocar la más nueva. Varias llamadas a la vez comparten un solo cierre, y una llamada con
       * la sesión ya cerrada no hace nada.
       */
      logout(options: LogoutOptions = {}): Promise<void> {
        if (loggingOut) {
          return loggingOut;
        }
        if (closed) {
          return Promise.resolve();
        }
        const pendingRefresh = refreshing;
        clear();
        closed = true;
        store._sync.post(
          options.reason ? { type: 'logout', reason: options.reason } : { type: 'logout' },
        );
        goToLogin(options.reason);
        loggingOut = (async () => {
          if (pendingRefresh) {
            await pendingRefresh;
          }
          try {
            await firstValueFrom(store._api.invoke(logout, PANEL_CLIENT, store._silent()));
          } catch {
            // El Api responde 204 siempre; si no hay red, la sesión ya quedó cerrada en el panel.
          }
        })().finally(() => (loggingOut = null));
        return loggingOut;
      },
    };
  }),
  withHooks((store) => {
    let unsubscribe = () => undefined as unknown;
    return {
      onInit: () => (unsubscribe = store._sync.subscribe((message) => store._onMessage(message))),
      onDestroy: () => unsubscribe(),
    };
  }),
);

/** Código HTTP de un `HttpErrorResponse` o de un `AppError`. */
function statusOf(error: unknown): number | null {
  return typeof error === 'object' && error !== null && 'status' in error
    ? Number((error as { status: unknown }).status)
    : null;
}
