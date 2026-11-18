import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { InactivityMonitor, NotificationsStore, SessionStore, ToastStore } from '@impifact/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/angular';
import { of } from 'rxjs';
import authEsCO from '../../../feature-auth/src/i18n/es-CO.json';
import esCO from '../../public/i18n/es-CO.json';
import { App } from './app';
import { routes } from './app.routes';

// Cargador en memoria con los archivos reales de textos es-CO (raíz y scope de feature-auth), sin HTTP.
class InMemoryLoader implements TranslocoLoader {
  getTranslation(path: string) {
    return of((path === 'auth/es-CO' ? authEsCO : esCO) as Translation);
  }
}

/** Sesión y monitor de inactividad falsos: el shell solo lee sus signals y llama a sus métodos. */
function fakes(
  options: {
    authenticated?: boolean;
    companies?: string[];
    roles?: string[];
    scopes?: string[];
  } = {},
) {
  const isAuthenticated = signal(options.authenticated ?? false);
  const roles = signal(options.roles ?? ['owner']);
  const scopes = signal(options.scopes ?? []);
  const session = {
    isAuthenticated,
    roles,
    scopes,
    hasRole: (...wanted: string[]) => wanted.some((role) => roles().includes(role)),
    hasScope: (scope: string) => scopes().includes(scope),
    user: signal(isAuthenticated() ? { id: 'user-1', email: 'ana@example.test' } : null),
    companyId: signal<string | null>(options.companies?.[0] ?? null),
    companies: signal(options.companies ?? []),
    restore: vi.fn(async () => undefined),
    login: vi.fn(),
    selectCompany: vi.fn(),
    logout: vi.fn(async () => isAuthenticated.set(false)),
  };
  const monitor = {
    warning: signal(false),
    minutesLeft: signal(5),
    stayConnected: vi.fn(() => monitor.warning.set(false)),
  };
  // Campana de avisos de core: el shell solo lee el número sin leer.
  const notifications = {
    unreadCount: signal(0),
    latest: signal([]),
    loaded: signal(true),
    error: signal(false),
    refresh: vi.fn(async () => undefined),
    markRead: vi.fn(async () => true),
    markAllRead: vi.fn(async () => 0),
  };
  return { session, monitor, notifications };
}

describe('App shell', () => {
  async function renderShell(options: Parameters<typeof fakes>[0] = {}) {
    const { session, monitor, notifications } = fakes(options);
    const result = await render(App, {
      providers: [
        provideRouter(routes, withComponentInputBinding()),
        provideTransloco({
          config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
          loader: InMemoryLoader,
        }),
        { provide: SessionStore, useValue: session },
        { provide: InactivityMonitor, useValue: monitor },
        { provide: NotificationsStore, useValue: notifications },
      ],
    });
    return { ...result, session, monitor, notifications };
  }

  it('renders the layout with texts from the es-CO translations', async () => {
    await renderShell();

    expect(await screen.findByText('ImpiFact')).toBeTruthy();
    const nav = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Inicio' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Saltar al contenido' })).toBeTruthy();
    expect(screen.getByRole('main')).toBeTruthy();
  });

  it('shows customers and products in the menu to an owner or a seller', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'], roles: ['seller'] });

    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Clientes' }).getAttribute('href')).toBe(
      '/clientes',
    );
    expect(within(nav).getByRole('link', { name: 'Productos' }).getAttribute('href')).toBe(
      '/productos',
    );
  });

  it('hides customers and products from other roles', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'], roles: ['customer'] });

    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Inicio' })).toBeTruthy();
    expect(within(nav).queryByRole('link', { name: 'Clientes' })).toBeNull();
    expect(within(nav).queryByRole('link', { name: 'Productos' })).toBeNull();
  });

  it('shows the documents menu entry only to owners and sellers with a session', async () => {
    const view = await renderShell({ authenticated: true, roles: ['seller'] });
    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Documentos' }).getAttribute('href')).toBe(
      '/documentos',
    );

    view.session.roles.set(['customer']);
    await waitFor(() => expect(within(nav).queryByRole('link', { name: 'Documentos' })).toBeNull());

    view.session.roles.set(['owner']);
    view.session.isAuthenticated.set(false);
    await waitFor(() => expect(within(nav).queryByRole('link', { name: 'Documentos' })).toBeNull());
  });

  it('shows the notification bell with the unread count to a session that reads notices', async () => {
    const { notifications } = await renderShell({
      authenticated: true,
      companies: ['company-1'],
      roles: ['seller'],
      scopes: ['documents:read'],
    });
    notifications.unreadCount.set(2);

    expect(await screen.findByRole('button', { name: 'Avisos, 2 sin leer' })).toBeTruthy();
    const nav = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Avisos' }).getAttribute('href')).toBe(
      '/notificaciones',
    );
  });

  it('sets the document title from the translations', async () => {
    await renderShell();
    await screen.findByText('ImpiFact');

    expect(TestBed.inject(Title).getTitle()).toBe('ImpiFact · Panel');
  });

  it('renders the core toasts with the support code and lets the user close them', async () => {
    await renderShell();
    await screen.findByText('ImpiFact');
    const toasts = TestBed.inject(ToastStore);

    toasts.show({ kind: 'error', text: 'No se pudo guardar', correlationId: 'abc-123' });
    const toast = (await screen.findByText('No se pudo guardar')).closest('li') as HTMLElement;
    expect(within(toast).getByText('abc-123')).toBeTruthy();

    fireEvent.click(within(toast).getByRole('button', { name: 'Cerrar' }));
    expect(toasts.toasts()).toEqual([]);
  });

  it('copies the support code and confirms it', async () => {
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    await renderShell();
    await screen.findByText('ImpiFact');

    TestBed.inject(ToastStore).show({ kind: 'error', text: 'Falló', correlationId: 'abc-123' });
    fireEvent.click(await screen.findByRole('button', { name: 'Copiar código' }));

    expect(writeText).toHaveBeenCalledWith('abc-123');
    expect(await screen.findByText('Código copiado.')).toBeTruthy();
  });

  it('shows the home page on the root route to a signed-in user', async () => {
    const { navigate } = await renderShell({ authenticated: true, companies: ['company-1'] });
    await navigate('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Panel de ImpiFact' }),
    ).toBeTruthy();
  });

  it('sends an anonymous visitor of a protected route to the login and keeps the route', async () => {
    const { navigate } = await renderShell();
    await navigate('/');

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeTruthy();
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2F');
  });

  it('hides the user menu without a session', async () => {
    await renderShell();
    await screen.findByText('ImpiFact');

    expect(screen.queryByRole('button', { name: /Mi cuenta/ })).toBeNull();
  });

  it('offers to log out from the user menu', async () => {
    const { session } = await renderShell({ authenticated: true, companies: ['company-1'] });
    const toggle = await screen.findByRole('button', { name: /Mi cuenta/ });
    expect(toggle.textContent).toContain('ana@example.test');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    fireEvent.click(toggle);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(screen.queryByRole('link', { name: 'Cambiar de empresa' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar sesión' }));

    expect(session.logout).toHaveBeenCalledExactlyOnceWith();
    await waitFor(() => expect(screen.queryByRole('button', { name: /Mi cuenta/ })).toBeNull());
  });

  it('links to the company selection when the user has several companies', async () => {
    await renderShell({ authenticated: true, companies: ['company-1', 'company-2'] });

    fireEvent.click(await screen.findByRole('button', { name: /Mi cuenta/ }));

    expect(screen.getByRole('link', { name: 'Cambiar de empresa' }).getAttribute('href')).toBe(
      '/empresas/seleccionar',
    );
  });

  it('closes the user menu with Escape and returns the focus to its button', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'] });
    const toggle = await screen.findByRole('button', { name: /Mi cuenta/ });
    fireEvent.click(toggle);

    fireEvent.keyDown(screen.getByRole('button', { name: 'Cerrar sesión' }), { key: 'Escape' });

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(toggle);
  });

  it('opens the inactivity dialog when the monitor warns', async () => {
    const { monitor } = await renderShell({ authenticated: true, companies: ['company-1'] });
    await screen.findByText('ImpiFact');
    expect(screen.queryByRole('dialog')).toBeNull();

    monitor.warning.set(true);

    expect(
      await screen.findByRole('dialog', { name: 'Su sesión se cerrará por inactividad' }),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Seguir conectado' }));
    expect(monitor.stayConnected).toHaveBeenCalledOnce();
  });
});
