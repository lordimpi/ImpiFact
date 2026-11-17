import { DOCUMENT, Injectable, OnDestroy, effect, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { RuntimeConfigService } from '../config/runtime-config';
import { ToastStore } from '../notifications/toast.store';
import { SessionSync } from '../session/session-sync';
import { SessionStore } from '../session/session.store';

const MINUTE_MS = 60_000;
/** Cada cuánto, como máximo, se avisa la actividad a las demás pestañas. */
const BROADCAST_EVERY_MS = 30_000;
const ACTIVITY_EVENTS = ['pointerdown', 'keydown', 'wheel', 'touchstart'] as const;
const TOAST_KEY = 'inactivity';

/**
 * Cierre de sesión por inactividad (sección 6 de la arquitectura): aviso a los `warnMinutes` y cierre a los
 * `logoutMinutes` de `config.json` (25 y 30 por defecto) sin actividad del usuario.
 *
 * - La actividad en cualquier pestaña cuenta para todas (se comparte por `SessionSync`).
 * - Con la pestaña oculta no corre ningún temporizador (como el *polling*, sección 9); al volver a mostrarse se
 *   compara el tiempo transcurrido desde la última actividad y, si ya pasó el límite, se cierra la sesión.
 *
 * Arranca y se detiene solo según haya sesión; `provideCore` lo crea al iniciar la aplicación.
 */
@Injectable({ providedIn: 'root' })
export class InactivityMonitor implements OnDestroy {
  private readonly session = inject(SessionStore);
  private readonly config = inject(RuntimeConfigService);
  private readonly sync = inject(SessionSync);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  private readonly warningState = signal(false);
  /** Si se mostró el aviso de inactividad. */
  readonly warning = this.warningState.asReadonly();

  private running = false;
  private lastActivity = 0;
  private lastBroadcast = 0;
  private timers: ReturnType<typeof setTimeout>[] = [];
  private unsubscribe: () => void = () => undefined;

  private readonly onActivity = () => this.recordActivity();
  private readonly onVisibility = () => this.schedule();

  constructor() {
    effect(() => (this.session.isAuthenticated() ? this.start() : this.stop()));
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private start(): void {
    if (this.running) {
      return;
    }
    this.running = true;
    this.lastActivity = Date.now();
    ACTIVITY_EVENTS.forEach((type) =>
      this.document.addEventListener(type, this.onActivity, { passive: true }),
    );
    this.document.addEventListener('visibilitychange', this.onVisibility);
    this.unsubscribe = this.sync.subscribe((message) => {
      if (message.type === 'activity' && message.at > this.lastActivity) {
        this.lastActivity = message.at;
        this.schedule();
      }
    });
    this.schedule();
  }

  private stop(): void {
    if (!this.running) {
      return;
    }
    this.running = false;
    ACTIVITY_EVENTS.forEach((type) => this.document.removeEventListener(type, this.onActivity));
    this.document.removeEventListener('visibilitychange', this.onVisibility);
    this.unsubscribe();
    this.clearTimers();
    this.hideWarning();
  }

  private recordActivity(): void {
    const now = Date.now();
    this.lastActivity = now;
    if (now - this.lastBroadcast >= BROADCAST_EVERY_MS) {
      this.lastBroadcast = now;
      this.sync.post({ type: 'activity', at: now });
    }
    this.schedule();
  }

  /** Programa el aviso y el cierre desde la última actividad; con la pestaña oculta no programa nada. */
  private schedule(): void {
    this.clearTimers();
    if (!this.running || this.document.visibilityState === 'hidden') {
      return;
    }
    const { warnMinutes, logoutMinutes } = this.config.config().inactivity;
    const elapsed = Date.now() - this.lastActivity;
    const untilWarning = warnMinutes * MINUTE_MS - elapsed;
    const untilLogout = logoutMinutes * MINUTE_MS - elapsed;

    if (untilLogout <= 0) {
      this.expire();
      return;
    }
    if (untilWarning <= 0) {
      this.showWarning(Math.ceil(untilLogout / MINUTE_MS));
    } else {
      this.hideWarning();
      this.timers.push(setTimeout(() => this.schedule(), untilWarning));
    }
    this.timers.push(setTimeout(() => this.expire(), untilLogout));
  }

  private expire(): void {
    this.stop();
    void this.session.logout();
  }

  private showWarning(minutes: number): void {
    this.warningState.set(true);
    this.toasts.show({
      kind: 'warning',
      key: TOAST_KEY,
      text: this.transloco.translate('core.inactivity.warning', { minutes }),
    });
  }

  private hideWarning(): void {
    if (this.warningState()) {
      this.warningState.set(false);
      this.toasts.dismissKey(TOAST_KEY);
    }
  }

  private clearTimers(): void {
    this.timers.forEach((timer) => clearTimeout(timer));
    this.timers = [];
  }
}
