import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { NotificationsStore, SessionStore } from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import { of } from 'rxjs';
import notificationsEsCO from '../../../../feature-notifications/src/i18n/es-CO.json';
import esCO from '../../../public/i18n/es-CO.json';
import { NotificationBellComponent } from './notification-bell.component';

class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    return of((path === 'notifications/es-CO' ? notificationsEsCO : esCO) as Translation);
  }
}

function fakes(options: { scopes?: string[] } = {}) {
  const scopes = signal(options.scopes ?? ['documents:read']);
  const session = {
    isAuthenticated: signal(true),
    companyId: signal<string | null>('company-1'),
    scopes,
    hasScope: (scope: string) => scopes().includes(scope),
  };
  const store = {
    unreadCount: signal(3),
    latest: signal([
      { id: 'n-1', title: 'Certificado por vencer', body: 'Quedan 10 días', is_read: false },
    ]),
    loaded: signal(true),
    error: signal(false),
    refresh: vi.fn(async () => undefined),
    markRead: vi.fn(async () => true),
    markAllRead: vi.fn(async () => 1),
  };
  return { session, store };
}

async function renderBell(options: Parameters<typeof fakes>[0] = {}) {
  const { session, store } = fakes(options);
  await render(NotificationBellComponent, {
    providers: [
      provideRouter([]),
      provideTransloco({
        config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
        loader: InMemoryLoader,
      }),
      { provide: SessionStore, useValue: session },
      { provide: NotificationsStore, useValue: store },
    ],
  });
  return { session, store };
}

describe('NotificationBellComponent', () => {
  it('shows the unread count in the button name and badge', async () => {
    const { store } = await renderBell();

    const bell = await screen.findByRole('button', { name: 'Avisos, 3 sin leer' });
    expect(bell.getAttribute('aria-expanded')).toBe('false');
    expect(bell.textContent).toContain('3');

    store.unreadCount.set(0);
    expect(await screen.findByRole('button', { name: 'Avisos' })).toBeTruthy();
  });

  it('caps a large count in the badge', async () => {
    const { store } = await renderBell();
    store.unreadCount.set(250);

    const bell = await screen.findByRole('button', { name: 'Avisos, 250 sin leer' });
    expect(bell.textContent).toContain('99+');
  });

  it('loads the dropdown on the first open and closes it with Escape', async () => {
    await renderBell();
    const bell = await screen.findByRole('button', { name: 'Avisos, 3 sin leer' });

    fireEvent.click(bell);

    // El primer `import()` del chunk puede tardar más que la espera por defecto.
    expect(await screen.findByText('Certificado por vencer', {}, { timeout: 5000 })).toBeTruthy();
    expect(bell.getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByRole('link', { name: 'Ver todos los avisos' })).toBeTruthy();

    fireEvent.keyDown(bell, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByText('Certificado por vencer')).toBeNull());
    expect(document.activeElement).toBe(bell);
  });

  it('is not shown without a session that can read notices', async () => {
    await renderBell({ scopes: ['platform:*'] });

    expect(screen.queryByRole('button', { name: /Avisos/ })).toBeNull();
  });
});
