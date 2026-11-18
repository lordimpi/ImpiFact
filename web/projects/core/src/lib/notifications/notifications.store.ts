import { DOCUMENT } from '@angular/common';
import { HttpContext } from '@angular/common/http';
import { effect, inject, untracked } from '@angular/core';
import {
  Api,
  NotificationResponse,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
} from '@impifact/api';
import {
  patchState,
  signalStore,
  withHooks,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { SKIP_ERROR_TOAST } from '../http/api-request';
import { SessionStore } from '../session/session.store';

/** Cada cuánto se pregunta por avisos nuevos (sección 9 de la arquitectura del frontend). */
export const NOTIFICATIONS_POLL_MS = 60_000;

/** Avisos que trae cada consulta para la lista desplegable de la campana. */
export const NOTIFICATIONS_LATEST_LIMIT = 5;

/** Alcance que pide el Api para leer los avisos de la empresa activa. */
const READ_SCOPE = 'documents:read';

interface NotificationsState {
  /** Total sin leer de la empresa activa (`unread_count`), para el número de la campana. */
  unreadCount: number;
  /** Los avisos más recientes, leídos o no. */
  latest: NotificationResponse[];
  /** Si ya llegó la primera respuesta. */
  loaded: boolean;
  /** Si falló la última consulta (la campana no abre avisos: lo intenta de nuevo en el siguiente ciclo). */
  error: boolean;
}

const EMPTY: NotificationsState = { unreadCount: 0, latest: [], loaded: false, error: false };

/**
 * Avisos de la campana (NgRx SignalStore en core, sección 4): consulta `listNotifications` cada 60 s mientras hay
 * sesión con empresa activa y `documents:read`; se pausa con la pestaña oculta (al volver consulta enseguida), se
 * detiene al cerrar la sesión y vuelve a empezar al cambiar de empresa. Si más adelante el Api agrega SSE (eventos
 * enviados por el servidor), solo cambia este store, no los componentes que lo leen.
 */
export const NotificationsStore = signalStore(
  { providedIn: 'root' },
  withState<NotificationsState>(EMPTY),
  withProps(() => ({
    _api: inject(Api),
    _session: inject(SessionStore),
    _document: inject(DOCUMENT),
  })),
  withMethods((store) => {
    /** Sube con cada cambio de sesión o empresa: una respuesta de una generación anterior se descarta. */
    let generation = 0;
    let timer: ReturnType<typeof setTimeout> | null = null;
    let active = false;

    const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);
    const hidden = () => store._document.visibilityState === 'hidden';

    function clearTimer(): void {
      if (timer !== null) {
        clearTimeout(timer);
        timer = null;
      }
    }

    function schedule(): void {
      clearTimer();
      if (active && !hidden()) {
        timer = setTimeout(() => {
          timer = null;
          void refresh().finally(schedule);
        }, NOTIFICATIONS_POLL_MS);
      }
    }

    async function refresh(): Promise<void> {
      // Sin sesión que pueda leer avisos no se pregunta nada.
      if (!active) {
        return;
      }
      const asked = generation;
      try {
        const page = await firstValueFrom(
          store._api.invoke(listNotifications, { limit: NOTIFICATIONS_LATEST_LIMIT }, silent()),
        );
        if (asked === generation) {
          patchState(store, {
            unreadCount: Number(page.unread_count ?? 0),
            latest: page.items ?? [],
            loaded: true,
            error: false,
          });
        }
      } catch {
        if (asked === generation) {
          patchState(store, { error: true });
        }
      }
    }

    function onVisibility(): void {
      if (!active) {
        return;
      }
      if (hidden()) {
        clearTimer();
      } else {
        void refresh();
        schedule();
      }
    }

    return {
      _start(): void {
        generation++;
        patchState(store, EMPTY);
        if (!active) {
          active = true;
          store._document.addEventListener('visibilitychange', onVisibility);
        }
        if (!hidden()) {
          void refresh();
        }
        schedule();
      },
      _stop(): void {
        generation++;
        active = false;
        clearTimer();
        store._document.removeEventListener('visibilitychange', onVisibility);
        patchState(store, EMPTY);
      },
      /** Vuelve a pedir el número y los últimos avisos (por ejemplo, al abrir la lista desplegable). */
      refresh,
      /** Marca un aviso como leído; devuelve si salió bien (el error lo avisa core). */
      async markRead(notification: Pick<NotificationResponse, 'id' | 'is_read'>): Promise<boolean> {
        const id = notification.id ?? '';
        try {
          await firstValueFrom(store._api.invoke(markNotificationRead, { id }), {
            defaultValue: null,
          });
        } catch {
          return false;
        }
        patchState(store, {
          unreadCount: notification.is_read
            ? store.unreadCount()
            : Math.max(0, store.unreadCount() - 1),
          latest: store
            .latest()
            .map((item) => (item.id === id ? { ...item, is_read: true } : item)),
        });
        return true;
      },
      /** Marca todos como leídos; devuelve cuántos cambiaron, o `null` si falló (el error lo avisa core). */
      async markAllRead(): Promise<number | null> {
        try {
          const result = await firstValueFrom(store._api.invoke(markAllNotificationsRead));
          patchState(store, {
            unreadCount: 0,
            latest: store.latest().map((item) => ({ ...item, is_read: true })),
          });
          return Number(result.marked_count ?? 0);
        } catch {
          return null;
        }
      },
    };
  }),
  withHooks({
    onInit(store) {
      // Clave de la consulta: la empresa activa, solo si la sesión puede leer avisos.
      effect(() => {
        const session = store._session;
        const key =
          session.isAuthenticated() && session.hasScope(READ_SCOPE) ? session.companyId() : null;
        untracked(() => (key ? store._start() : store._stop()));
      });
    },
  }),
);
