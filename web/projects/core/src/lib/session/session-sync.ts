import { Injectable, OnDestroy } from '@angular/core';
import { PanelSessionTokens } from '../api-shell';

/** Motivo de un cierre de sesión que no pidió la persona; el login lo explica (`/login?reason=…`). */
export type LogoutReason = 'inactivity';

/** Mensajes entre pestañas del panel. Nunca llevan la cookie de renovación, que el navegador no deja leer. */
export type SessionMessage =
  | { type: 'session'; tokens: PanelSessionTokens; email: string | null }
  | { type: 'logout'; reason?: LogoutReason }
  | { type: 'activity'; at: number };

/**
 * Coordinación de la sesión entre pestañas.
 * - `post`/`subscribe`: avisos a las demás pestañas (nueva sesión, cierre, actividad del usuario).
 * - `exclusive`: ejecuta la renovación con un candado compartido por todas las pestañas del mismo origen. Dos
 *   renovaciones simultáneas con la misma cookie hacen que el Api responda 200 a una y 409 a la otra; con el
 *   candado la segunda espera y sale con la cookie ya renovada.
 *
 * Es una clase abstracta para poder reemplazarla en las pruebas.
 */
@Injectable({ providedIn: 'root', useFactory: () => new BrowserSessionSync() })
export abstract class SessionSync {
  abstract post(message: SessionMessage): void;
  abstract subscribe(listener: (message: SessionMessage) => void): () => void;
  abstract exclusive<T>(task: () => Promise<T>): Promise<T>;
}

const CHANNEL_NAME = 'impifact.session';
const REFRESH_LOCK = 'impifact.session.refresh';

/**
 * Implementación del navegador: `BroadcastChannel` para los avisos y la Web Locks API (`navigator.locks`) para el
 * candado. Si el navegador no tiene alguna de las dos, los avisos no salen y la renovación solo se serializa dentro
 * de la pestaña (el `SessionStore` ya deja una sola en vuelo).
 */
@Injectable()
export class BrowserSessionSync extends SessionSync implements OnDestroy {
  private readonly channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(CHANNEL_NAME);

  post(message: SessionMessage): void {
    this.channel?.postMessage(message);
  }

  subscribe(listener: (message: SessionMessage) => void): () => void {
    const channel = this.channel;
    if (!channel) {
      return () => undefined;
    }
    const handler = (event: MessageEvent<SessionMessage>) => listener(event.data);
    channel.addEventListener('message', handler);
    return () => channel.removeEventListener('message', handler);
  }

  exclusive<T>(task: () => Promise<T>): Promise<T> {
    const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;
    return locks ? locks.request(REFRESH_LOCK, { mode: 'exclusive' }, task) : task();
  }

  ngOnDestroy(): void {
    this.channel?.close();
  }
}
