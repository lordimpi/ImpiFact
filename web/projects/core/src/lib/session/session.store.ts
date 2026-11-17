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
import { SessionMessage, SessionSync } from './session-sync';

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

    function apply(tokens: PanelSessionTokens, email: string | null): void {
      const claims = readAccessTokenClaims(tokens.access_token);
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
      patchState(store, { status: 'anonymous', ...ANONYMOUS });
    }

    function goToLogin(): void {
      void store._router.navigateByUrl(store._paths.login);
    }

    async function callRefresh(retryOnConflict: boolean): Promise<boolean> {
      try {
        const tokens: PanelSessionTokens = await firstValueFrom(
          store._api.invoke(refreshSession, PANEL_CLIENT, store._silent()),
        );
        apply(tokens, store.email());
        store._sync.post({ type: 'session', tokens, email: store.email() });
        return true;
      } catch (error) {
        // 409: otra renovación con la misma cookie llegó primero (por ejemplo, un navegador sin Web Locks). La
        // cookie ya quedó renovada, así que un segundo intento sale bien.
        if (retryOnConflict && statusOf(error) === CONFLICT) {
          return callRefresh(false);
        }
        clear();
        return false;
      }
    }

    function refresh(staleToken: string | null): Promise<boolean> {
      refreshing ??= store._sync
        .exclusive(() => {
          const current = store.accessToken();
          return current !== null && current !== staleToken
            ? Promise.resolve(true)
            : callRefresh(true);
        })
        .finally(() => (refreshing = null));
      return refreshing;
    }

    function onMessage(message: SessionMessage): void {
      if (message.type === 'session') {
        apply(message.tokens, message.email ?? store.email());
      } else if (message.type === 'logout') {
        clear();
        goToLogin();
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
        apply(tokens, credentials.email);
        store._sync.post({ type: 'session', tokens, email: credentials.email });
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

      /** Cierra la sesión en el Api (revoca la cookie), la limpia aquí, avisa a las demás pestañas y va al login. */
      async logout(): Promise<void> {
        try {
          await firstValueFrom(store._api.invoke(logout, PANEL_CLIENT, store._silent()));
        } catch {
          // El Api responde 204 siempre; si no hay red, la sesión se cierra igual en el panel.
        }
        clear();
        store._sync.post({ type: 'logout' });
        goToLogin();
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
