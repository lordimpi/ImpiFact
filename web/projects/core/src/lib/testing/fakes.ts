import { Provider } from '@angular/core';
import { PanelSessionTokens } from '../api-shell';
import { SessionMessage, SessionSync } from '../session/session-sync';

/*
 * Dobles de prueba de core (solo los usan los archivos .spec.ts; no se exportan en la API pública).
 */

/**
 * `SessionSync` en memoria: guarda lo publicado, permite simular mensajes de otra pestaña y controlar el candado
 * entre pestañas (`holdLock` lo toma «otra pestaña» hasta que se llama a la función que devuelve).
 */
export class FakeSessionSync extends SessionSync {
  readonly posted: SessionMessage[] = [];
  private readonly listeners = new Set<(message: SessionMessage) => void>();
  private queue: Promise<unknown> = Promise.resolve();

  post(message: SessionMessage): void {
    this.posted.push(message);
  }

  subscribe(listener: (message: SessionMessage) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  exclusive<T>(task: () => Promise<T>): Promise<T> {
    const run = this.queue.then(task);
    this.queue = run.catch(() => undefined);
    return run;
  }

  /** Mensaje que llega desde otra pestaña. */
  receive(message: SessionMessage): void {
    this.listeners.forEach((listener) => listener(message));
  }

  /** Otra pestaña toma el candado; se libera al llamar a la función devuelta. */
  holdLock(): () => void {
    let release!: () => void;
    const held = new Promise<void>((resolve) => (release = resolve));
    void this.exclusive(() => held);
    return release;
  }
}

export function provideFakeSessionSync(): { sync: FakeSessionSync; provider: Provider } {
  const sync = new FakeSessionSync();
  return { sync, provider: { provide: SessionSync, useValue: sync } };
}

/** JWT sin firma válida con los claims dados (el panel no verifica firmas: lo hace el Api). */
export function fakeJwt(claims: Record<string, unknown>): string {
  const encode = (value: unknown) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'ES256', typ: 'JWT' })}.${encode(claims)}.firma`;
}

export function panelTokens(overrides: Partial<PanelSessionTokens> = {}): PanelSessionTokens {
  return {
    access_token: fakeJwt({
      sub: 'user-1',
      role: ['owner'],
      scope: ['documents:read', 'documents:write'],
      company_id: 'company-1',
    }),
    expires_at: '2026-11-17T12:15:00Z',
    refresh_token_expires_at: '2026-12-01T12:00:00Z',
    company_id: 'company-1',
    roles: ['owner'],
    companies: ['company-1', 'company-2'],
    email: 'ana@example.test',
    ...overrides,
  };
}
