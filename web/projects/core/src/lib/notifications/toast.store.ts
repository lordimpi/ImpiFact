import { patchState, signalStore, withHooks, withMethods, withState } from '@ngrx/signals';

export type ToastKind = 'info' | 'success' | 'warning' | 'error';

/** Aviso breve (*toast*). `text` llega ya traducido; `correlationId` se muestra para que la persona lo copie. */
export interface Toast {
  id: number;
  kind: ToastKind;
  text: string;
  correlationId: string | null;
  /** Clave opcional: un aviso nuevo con la misma clave reemplaza al anterior (por ejemplo, el de inactividad). */
  key?: string;
}

export interface ToastInput {
  kind: ToastKind;
  text: string;
  correlationId?: string | null;
  key?: string;
  /** Milisegundos hasta cerrarse solo; `null` lo deja hasta que se cierre. Por defecto: 5 s para info y éxito. */
  durationMs?: number | null;
}

export const MAX_TOASTS = 5;
const AUTO_CLOSE_MS = 5_000;

/**
 * Avisos del panel (NgRx SignalStore). La aplicación los dibuja con `ui-toast-region`, que los anuncia por
 * `aria-live`. Los errores y advertencias quedan hasta que la persona los cierra, para que alcance a leer y copiar
 * el identificador de soporte.
 */
export const ToastStore = signalStore(
  { providedIn: 'root' },
  withState({ toasts: [] as Toast[] }),
  withMethods((store) => {
    let nextId = 1;
    const timers = new Map<number, ReturnType<typeof setTimeout>>();

    function dismiss(id: number): void {
      clearTimeout(timers.get(id));
      timers.delete(id);
      patchState(store, { toasts: store.toasts().filter((toast) => toast.id !== id) });
    }

    return {
      show(input: ToastInput): number {
        const id = nextId++;
        const toast: Toast = {
          id,
          kind: input.kind,
          text: input.text,
          correlationId: input.correlationId ?? null,
          ...(input.key === undefined ? {} : { key: input.key }),
        };
        const kept = store
          .toasts()
          .filter((item) => input.key === undefined || item.key !== input.key);
        const dropped = [...kept, toast].slice(0, -MAX_TOASTS);
        dropped.forEach((item) => clearTimeout(timers.get(item.id)));
        patchState(store, { toasts: [...kept, toast].slice(-MAX_TOASTS) });

        const duration =
          input.durationMs !== undefined
            ? input.durationMs
            : input.kind === 'info' || input.kind === 'success'
              ? AUTO_CLOSE_MS
              : null;
        if (duration !== null) {
          timers.set(
            id,
            setTimeout(() => dismiss(id), duration),
          );
        }
        return id;
      },

      dismiss,

      dismissKey(key: string): void {
        store
          .toasts()
          .filter((toast) => toast.key === key)
          .forEach((toast) => dismiss(toast.id));
      },

      _clearTimers(): void {
        timers.forEach((timer) => clearTimeout(timer));
        timers.clear();
      },
    };
  }),
  withHooks({ onDestroy: (store) => store._clearTimers() }),
);
