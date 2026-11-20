import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { DeferBlockBehavior, TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import {
  InactivityMonitor,
  MyCompaniesStore,
  NotificationsStore,
  SessionStore,
  ToastStore,
} from '@impifact/core';
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
  // Empresas con nombre (`GET /api/v1/me/companies`); la barra superior muestra la activa.
  const directory = {
    active: signal<{ id: string; name: string } | null>(null),
    byId: () => null,
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
  return { session, monitor, directory, notifications };
}

describe('App shell', () => {
  async function renderShell(options: Parameters<typeof fakes>[0] = {}) {
    const { session, monitor, directory, notifications } = fakes(options);
    const result = await render(App, {
      // The sidebar and the top bar are deferred blocks: they render as in the app once their trigger fires.
      deferBlockBehavior: DeferBlockBehavior.Playthrough,
      providers: [
        provideRouter(routes, withComponentInputBinding()),
        provideTransloco({
          config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
          loader: InMemoryLoader,
        }),
        { provide: SessionStore, useValue: session },
        { provide: InactivityMonitor, useValue: monitor },
        { provide: MyCompaniesStore, useValue: directory },
        { provide: NotificationsStore, useValue: notifications },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    return { ...result, session, monitor, directory, notifications };
  }

  it('shows only the content, without sidebar or top bar, to an anonymous visitor', async () => {
    await renderShell();

    expect(await screen.findByRole('link', { name: 'Saltar al contenido' })).toBeTruthy();
    expect(screen.getByRole('main')).toBeTruthy();
    expect(screen.queryByRole('navigation', { name: 'Navegación principal' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Menú de navegación' })).toBeNull();
  });

  it('renders the sidebar with the brand and the menu from the es-CO translations', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'], roles: ['customer'] });

    expect(await screen.findByText('ImpiFact')).toBeTruthy();
    const nav = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Inicio' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Saltar al contenido' })).toBeTruthy();
    expect(screen.getByRole('main')).toBeTruthy();
  });

  it('groups the menu by section and hides the empty sections', async () => {
    const view = await renderShell({
      authenticated: true,
      companies: ['company-1'],
      roles: ['owner'],
      scopes: ['documents:read', 'documents:write'],
    });
    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });

    const issue = within(nav).getByRole('list', { name: 'Emisión' });
    expect(within(issue).getByRole('link', { name: 'Nueva factura' }).getAttribute('href')).toBe(
      '/documentos/nuevo',
    );
    expect(within(issue).getByRole('link', { name: 'Nota crédito' }).getAttribute('href')).toBe(
      '/documentos/nota-credito',
    );
    expect(within(issue).getByRole('link', { name: 'Nota débito' }).getAttribute('href')).toBe(
      '/documentos/nota-debito',
    );
    const query = within(nav).getByRole('list', { name: 'Consulta' });
    expect(within(query).getByRole('link', { name: 'Documentos' })).toBeTruthy();
    expect(within(nav).getByRole('list', { name: 'Ajustes' })).toBeTruthy();
    expect(within(nav).queryByRole('list', { name: 'Plataforma' })).toBeNull();

    // Issuing needs the `documents:write` scope, as its routes do.
    view.session.scopes.set(['documents:read']);
    await waitFor(() => expect(within(nav).queryByRole('list', { name: 'Emisión' })).toBeNull());
    expect(within(nav).queryByRole('link', { name: 'Nueva factura' })).toBeNull();
  });

  it('marks the entry of the current page', async () => {
    const { navigate } = await renderShell({
      authenticated: true,
      companies: ['company-1'],
      roles: ['customer'],
    });
    await navigate('/');

    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    await waitFor(() =>
      expect(within(nav).getByRole('link', { name: 'Inicio' }).getAttribute('aria-current')).toBe(
        'page',
      ),
    );
    expect(
      within(nav).getByRole('link', { name: 'Avisos' }).getAttribute('aria-current'),
    ).toBeNull();
  });

  it('opens the menu drawer from the top bar and closes it with Escape', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'], roles: ['customer'] });
    const toggle = await screen.findByRole('button', { name: 'Menú de navegación' });
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe('app-sidebar');
    const sidebar = document.getElementById('app-sidebar') as HTMLElement;
    expect(sidebar.classList).not.toContain('is-open');

    const body = toggle.closest('.shell-body') as HTMLElement;
    expect(body.hasAttribute('inert')).toBe(false);

    fireEvent.click(toggle);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(sidebar.classList).toContain('is-open');
    // The open drawer is modal: the top bar, the content and the skip link are inert.
    expect(body.hasAttribute('inert')).toBe(true);
    expect(screen.getByText('Saltar al contenido').hasAttribute('inert')).toBe(true);

    const home = within(sidebar).getByRole('link', { name: 'Inicio' });
    fireEvent.keyDown(home, { key: 'Escape' });
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(sidebar.classList).not.toContain('is-open');
    expect(body.hasAttribute('inert')).toBe(false);
    await waitFor(() => expect(document.activeElement).toBe(toggle));
  });

  it('closes the menu drawer with its close button', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'], roles: ['customer'] });
    const toggle = await screen.findByRole('button', { name: 'Menú de navegación' });
    fireEvent.click(toggle);

    fireEvent.click(screen.getByRole('button', { name: 'Cerrar el menú' }));

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    await waitFor(() => expect(document.activeElement).toBe(toggle));
  });

  it('shows the company selection without the sidebar', async () => {
    const { navigate } = await renderShell({
      authenticated: true,
      companies: ['company-1', 'company-2'],
    });
    expect(await screen.findByRole('navigation', { name: 'Navegación principal' })).toBeTruthy();

    await navigate('/empresas/seleccionar');

    await waitFor(() =>
      expect(screen.queryByRole('navigation', { name: 'Navegación principal' })).toBeNull(),
    );
    expect(screen.queryByRole('button', { name: /Mi cuenta/ })).toBeNull();
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

  it('shows the company configuration and the production wizard only to an owner', async () => {
    const view = await renderShell({ authenticated: true, companies: ['company-1'] });
    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Empresa' }).getAttribute('href')).toBe(
      '/empresa',
    );
    expect(within(nav).getByRole('link', { name: 'Paso a producción' }).getAttribute('href')).toBe(
      '/empresa/produccion',
    );

    view.session.roles.set(['seller']);
    await waitFor(() => expect(within(nav).queryByRole('link', { name: 'Empresa' })).toBeNull());
    expect(within(nav).queryByRole('link', { name: 'Paso a producción' })).toBeNull();
  });

  it('shows the name of the active company in the top bar', async () => {
    const view = await renderShell({ authenticated: true, companies: ['company-1'] });
    await screen.findByRole('button', { name: /Mi cuenta/ });
    expect(screen.queryByText('Acme SAS')).toBeNull();

    view.directory.active.set({ id: 'company-1', name: 'Acme SAS' });

    expect(await screen.findByText('Acme SAS')).toBeTruthy();
    expect(screen.queryByRole('link', { name: /elegir otra empresa/ })).toBeNull();
  });

  it('links the active company to the company selection when the user has several', async () => {
    const view = await renderShell({ authenticated: true, companies: ['company-1', 'company-2'] });
    view.directory.active.set({ id: 'company-1', name: 'Acme SAS' });

    const link = await screen.findByRole('link', { name: 'Acme SAS, elegir otra empresa activa' });
    expect(link.getAttribute('href')).toBe('/empresas/seleccionar');
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

  it('shows the API keys and plan entries only to an owner', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'], roles: ['owner'] });
    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Llaves de API' }).getAttribute('href')).toBe(
      '/llaves-api',
    );
    expect(within(nav).getByRole('link', { name: 'Mi plan' }).getAttribute('href')).toBe('/plan');
    expect(within(nav).queryByRole('link', { name: 'Planes' })).toBeNull();
  });

  it('shows the platform entries to an administrator with the platform scope', async () => {
    await renderShell({ authenticated: true, roles: ['platform_admin'], scopes: ['platform:*'] });
    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Planes' }).getAttribute('href')).toBe(
      '/plataforma/planes',
    );
    expect(within(nav).getByRole('link', { name: 'Empresas' }).getAttribute('href')).toBe(
      '/plataforma/empresas',
    );
    expect(within(nav).queryByRole('link', { name: 'Llaves de API' })).toBeNull();
  });

  it('hides the platform entries from an administrator inside a company', async () => {
    await renderShell({ authenticated: true, companies: ['company-1'], roles: ['platform_admin'] });
    const nav = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).queryByRole('link', { name: 'Planes' })).toBeNull();
  });

  it('sets the document title from the translations', async () => {
    await renderShell();
    await screen.findByRole('main');

    expect(TestBed.inject(Title).getTitle()).toBe('ImpiFact · Panel');
  });

  it('renders the core toasts with the support code and lets the user close them', async () => {
    await renderShell();
    await screen.findByRole('main');
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
    await screen.findByRole('main');

    TestBed.inject(ToastStore).show({ kind: 'error', text: 'Falló', correlationId: 'abc-123' });
    fireEvent.click(await screen.findByRole('button', { name: 'Copiar código' }));

    expect(writeText).toHaveBeenCalledWith('abc-123');
    expect(await screen.findByText('Código copiado.')).toBeTruthy();
  });

  it('shows the home page on the root route to a signed-in user without the dashboard', async () => {
    const { navigate } = await renderShell({
      authenticated: true,
      companies: ['company-1'],
      roles: ['customer'],
    });
    await navigate('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Panel de ImpiFact' }),
    ).toBeTruthy();
    TestBed.inject(HttpTestingController).expectNone('/api/v1/dashboard/summary');
  });

  it('opens the dashboard on the root route to an owner or a seller', async () => {
    const { navigate } = await renderShell({
      authenticated: true,
      companies: ['company-1'],
      roles: ['seller'],
    });
    await navigate('/');

    await waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/v1/dashboard/summary'),
    );
  });

  it('sends an anonymous visitor of a protected route to the login and keeps the route', async () => {
    const { navigate } = await renderShell();
    await navigate('/');

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeTruthy();
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2F');
  });

  it('hides the user menu without a session', async () => {
    await renderShell();
    await screen.findByRole('main');

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
