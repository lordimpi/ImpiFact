import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import {
  COMPANY_ID,
  COMPANY_PATH,
  expectRequest,
  problem,
  renderCompaniesRoute,
  respond,
} from '../testing/companies-testing';
import { optionNames, pickOption, stubOptimusDom } from '../testing/optimus-testing';

const USERS_URL = `${COMPANY_PATH}/users`;

/** The signed-in owner (`user-1`), a second owner, a seller and an inactive seller, sorted as the API sends them. */
const ME = {
  id: 'user-1',
  email: 'ana@acme.test',
  name: 'Ana Gómez',
  phone: '3001234567',
  role: 'owner',
  is_active: true,
};
const CARLOS = {
  id: 'user-2',
  email: 'carlos@acme.test',
  name: 'Carlos Ruiz',
  phone: null,
  role: 'seller',
  is_active: true,
};
const DIANA = {
  id: 'user-3',
  email: 'diana@acme.test',
  name: 'Diana Peña',
  phone: '3109876543',
  role: 'seller',
  is_active: false,
};
const USERS = [ME, CARLOS, DIANA];

const toastTexts = () =>
  TestBed.inject(ToastStore)
    .toasts()
    .map((toast) => toast.text);

beforeAll(stubOptimusDom);

async function open(items: object[] = USERS) {
  const rendered = await renderCompaniesRoute('/empresa/usuarios');
  await screen.findByRole('heading', { level: 1, name: 'Usuarios' });
  await respond(rendered.http, USERS_URL, { items });
  const table = await screen.findByRole('table', { name: 'Usuarios de la empresa' });
  await within(table).findByText(CARLOS.email);
  return { ...rendered, table };
}

/** The table row that holds `text`. */
function row(table: HTMLElement, text: string): HTMLElement {
  const cell = within(table).getByText(text);
  const found = cell.closest('[role="row"], tr');
  if (!found) {
    throw new Error(`No row for ${text}`);
  }
  return found as HTMLElement;
}

async function expectPatch(http: HttpTestingController, userId: string) {
  const request = await expectRequest(http, `${USERS_URL}/${userId}`);
  expect(request.request.method).toBe('PATCH');
  return request;
}

describe('CompanyUsersPage', () => {
  it('lists the users of the active company with role, status and the current user marked', async () => {
    const { http, table } = await open();

    const headers = within(table)
      .getAllByRole('columnheader')
      .map((header) => header.textContent?.trim());
    expect(headers).toEqual(['Nombre', 'Correo', 'Teléfono', 'Rol', 'Estado', 'Acciones']);

    const mine = row(table, ME.email);
    expect(within(mine).getByText('Ana Gómez')).toBeTruthy();
    expect(within(mine).getByText('(usted)')).toBeTruthy();
    expect(within(mine).getByText('Dueño')).toBeTruthy();
    expect(within(mine).getByText('Activo')).toBeTruthy();

    const carlos = row(table, CARLOS.email);
    expect(within(carlos).queryByText('(usted)')).toBeNull();
    expect(within(carlos).getByText('Vendedor')).toBeTruthy();
    expect(within(carlos).getByText('Sin teléfono')).toBeTruthy();

    expect(within(row(table, DIANA.email)).getByText('Inactivo')).toBeTruthy();
    // The owner's page has no breadcrumb and never looks the company up among the platform companies.
    expect(screen.queryByRole('navigation', { name: 'Ruta de navegación' })).toBeNull();
    http.expectNone((candidate) => candidate.url === '/api/v1/platform/companies');
  });

  it('shows an empty state when the company has no users', async () => {
    const { http } = await renderCompaniesRoute('/empresa/usuarios');
    await respond(http, USERS_URL, { items: [] });

    expect(await screen.findByText('La empresa aún no tiene usuarios')).toBeTruthy();
  });

  it('shows an error with a retry when the list cannot be read', async () => {
    const { http } = await renderCompaniesRoute('/empresa/usuarios');
    const error = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, USERS_URL)).flush(error.body, error.options);

    expect(await screen.findByText('No se pudieron consultar los usuarios')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await respond(http, USERS_URL, { items: USERS });
    expect(await screen.findByText(CARLOS.email)).toBeTruthy();
  });

  it('keeps the page from sellers and sends them home', async () => {
    const { router, http } = await renderCompaniesRoute('/empresa/usuarios', {
      roles: ['seller'],
    });

    await waitFor(() => expect(router.url).toBe('/'));
    expect(screen.queryByRole('heading', { name: 'Usuarios' })).toBeNull();
    http.expectNone(USERS_URL);
  });

  it('does not offer to change the role of or deactivate the current user, and says why', async () => {
    const { table } = await open();
    const mine = row(table, ME.email);

    expect(within(mine).queryByRole('button')).toBeNull();
    expect(
      within(mine).getByText('Es su cuenta: no puede cambiar su rol ni desactivarla.'),
    ).toBeTruthy();

    const carlos = row(table, CARLOS.email);
    expect(
      within(carlos).getByRole('button', { name: 'Cambiar a dueño: Carlos Ruiz' }),
    ).toBeTruthy();
    expect(within(carlos).getByRole('button', { name: 'Desactivar a Carlos Ruiz' })).toBeTruthy();
    expect(
      within(row(table, DIANA.email)).getByRole('button', { name: 'Reactivar a Diana Peña' }),
    ).toBeTruthy();
  });

  it('changes the role of a user and updates the row', async () => {
    const { http, table } = await open();

    fireEvent.click(
      within(row(table, CARLOS.email)).getByRole('button', { name: /^Cambiar a dueño/ }),
    );
    const request = await expectPatch(http, CARLOS.id);
    expect(request.request.body).toEqual({ role: 'owner' });
    request.flush({ ...CARLOS, role: 'owner' });

    await waitFor(() => expect(within(row(table, CARLOS.email)).getByText('Dueño')).toBeTruthy());
    expect(
      within(row(table, CARLOS.email)).getByRole('button', {
        name: 'Cambiar a vendedor: Carlos Ruiz',
      }),
    ).toBeTruthy();
    await waitFor(() => expect(toastTexts()).toContain('Carlos Ruiz ahora es dueño.'));
  });

  it('deactivates a user only after the confirmation and marks the row inactive', async () => {
    const { http, table } = await open();

    fireEvent.click(within(row(table, CARLOS.email)).getByRole('button', { name: /^Desactivar/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar a Carlos Ruiz' });
    expect(
      within(dialog).getByText(
        'Carlos Ruiz perderá el acceso a esta empresa y se cerrarán sus sesiones abiertas. Lo que emitió conserva su autor, y usted puede reactivarlo cuando quiera.',
      ),
    ).toBeTruthy();
    http.expectNone(`${USERS_URL}/${CARLOS.id}`);

    fireEvent.click(within(dialog).getByRole('button', { name: 'Desactivar' }));
    const request = await expectPatch(http, CARLOS.id);
    expect(request.request.body).toEqual({ is_active: false });
    request.flush({ ...CARLOS, is_active: false });

    await waitFor(() =>
      expect(within(row(table, CARLOS.email)).getByText('Inactivo')).toBeTruthy(),
    );
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Desactivar a Carlos Ruiz' })).toBeNull(),
    );
    expect(toastTexts()).toContain('Carlos Ruiz quedó inactivo y se cerraron sus sesiones.');
  });

  it('cancels the deactivation without calling the API', async () => {
    const { http, table } = await open();

    fireEvent.click(within(row(table, CARLOS.email)).getByRole('button', { name: /^Desactivar/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar a Carlos Ruiz' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Desactivar a Carlos Ruiz' })).toBeNull(),
    );
    http.expectNone(`${USERS_URL}/${CARLOS.id}`);
    expect(within(row(table, CARLOS.email)).getByText('Activo')).toBeTruthy();
  });

  it('reactivates an inactive user without asking', async () => {
    const { http, table } = await open();

    fireEvent.click(within(row(table, DIANA.email)).getByRole('button', { name: /^Reactivar/ }));
    const request = await expectPatch(http, DIANA.id);
    expect(request.request.body).toEqual({ is_active: true });
    request.flush({ ...DIANA, is_active: true });

    await waitFor(() => expect(within(row(table, DIANA.email)).getByText('Activo')).toBeTruthy());
    expect(toastTexts()).toContain('Diana Peña puede volver a entrar a la empresa.');
  });

  it.each([
    [
      422,
      'user.last_owner',
      'La empresa debe tener al menos un dueño activo: nombre otro dueño antes de hacer este cambio.',
    ],
    [422, 'user.cannot_deactivate_self', 'No puede desactivar su propia cuenta.'],
    [422, 'user.cannot_remove_own_owner_role', 'No puede quitarse a usted mismo el rol de dueño.'],
  ])('explains a %s %s from the API in a toast and keeps the row', async (status, code, text) => {
    const { http, table } = await open();

    fireEvent.click(
      within(row(table, CARLOS.email)).getByRole('button', { name: /^Cambiar a dueño/ }),
    );
    const error = problem(status, code, 'Mensaje del Api.');
    (await expectPatch(http, CARLOS.id)).flush(error.body, error.options);

    await waitFor(() => expect(toastTexts()).toContain(text));
    expect(toastTexts()).not.toContain('Mensaje del Api.');
    expect(within(row(table, CARLOS.email)).getByText('Vendedor')).toBeTruthy();
  });

  it.each([
    [404, 'user.not_found', 'Este usuario ya no pertenece a la empresa; la lista se actualizó.'],
    [
      409,
      'concurrency.conflict',
      'Otra persona cambió estos datos al mismo tiempo; la lista se actualizó.',
    ],
  ])('refreshes the list by itself after a %s %s', async (status, code, text) => {
    const { http, table } = await open();
    // No manual reload: the page keeps the list current by itself.
    expect(screen.queryByRole('button', { name: 'Recargar' })).toBeNull();

    fireEvent.click(
      within(row(table, CARLOS.email)).getByRole('button', { name: /^Cambiar a dueño/ }),
    );
    const error = problem(status, code, 'Mensaje del Api.');
    (await expectPatch(http, CARLOS.id)).flush(error.body, error.options);

    await waitFor(() => expect(toastTexts()).toContain(text));
    await respond(http, USERS_URL, { items: [...USERS.filter((user) => user.id !== CARLOS.id)] });
    await waitFor(() => expect(screen.queryByText(CARLOS.email)).toBeNull());
  });

  describe('create dialog', () => {
    async function openDialog() {
      const rendered = await open();
      fireEvent.click(screen.getByRole('button', { name: 'Nuevo usuario' }));
      const dialog = await screen.findByRole('dialog', { name: 'Nuevo usuario' });
      return { ...rendered, dialog };
    }

    const type = (dialog: HTMLElement, label: RegExp, value: string) =>
      fireEvent.input(within(dialog).getByLabelText(label), { target: { value } });

    function fill(dialog: HTMLElement, password = 'clave-segura-123') {
      type(dialog, /^Correo electrónico/, 'eva@acme.test');
      type(dialog, /^Nombre/, 'Eva Torres');
      type(dialog, /^Contraseña/, password);
    }

    it('shows the fields with the panel placeholders and the two roles', async () => {
      const { dialog } = await openDialog();

      expect(
        within(dialog)
          .getByLabelText(/^Correo electrónico/)
          .getAttribute('placeholder'),
      ).toBe('nombre@empresa.com');
      expect(
        within(dialog)
          .getByLabelText(/^Nombre/)
          .getAttribute('placeholder'),
      ).toBe('Ana Gómez');
      expect(
        within(dialog)
          .getByLabelText(/^Teléfono/)
          .getAttribute('placeholder'),
      ).toBe('3001234567');
      expect(
        within(dialog)
          .getByLabelText(/^Contraseña/)
          .getAttribute('placeholder'),
      ).toBeNull();
      expect(within(dialog).getByRole('button', { name: /Mostrar la contraseña/ })).toBeTruthy();
      expect(await optionNames(within(dialog).getByRole('combobox', { name: /^Rol/ }))).toEqual([
        'Dueño',
        'Vendedor',
      ]);
    });

    it('asks for a password of at least 10 characters before calling the API', async () => {
      const { http, dialog } = await openDialog();
      fill(dialog, 'corta');

      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));

      expect(await within(dialog).findByText('Escriba al menos 10 caracteres.')).toBeTruthy();
      http.expectNone('/api/v1/users');
    });

    it('creates a seller of the active company, closes and reads the list again', async () => {
      const { http, dialog } = await openDialog();
      fill(dialog);
      type(dialog, /^Teléfono/, '3115550000');
      await pickOption(within(dialog).getByRole('combobox', { name: /^Rol/ }), 'Vendedor');

      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));
      const request = await expectRequest(http, '/api/v1/users');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        company_id: COMPANY_ID,
        email: 'eva@acme.test',
        name: 'Eva Torres',
        phone: '3115550000',
        password: 'clave-segura-123',
        role: 'seller',
      });
      request.flush({
        id: 'user-9',
        company_id: COMPANY_ID,
        email: 'eva@acme.test',
        name: 'Eva Torres',
        role: 'seller',
      });

      await respond(http, USERS_URL, {
        items: [...USERS, { ...CARLOS, id: 'user-9', email: 'eva@acme.test', name: 'Eva Torres' }],
      });
      expect(await screen.findByText('eva@acme.test')).toBeTruthy();
      await waitFor(() =>
        expect(screen.queryByRole('dialog', { name: 'Nuevo usuario' })).toBeNull(),
      );
      expect(toastTexts()).toContain(
        'Se creó el usuario eva@acme.test como vendedor; ya puede iniciar sesión.',
      );
    });

    it('shows a taken email next to the email field and keeps the dialog open', async () => {
      const { http, dialog } = await openDialog();
      fill(dialog);

      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));
      const error = problem(409, 'user.email_taken', 'Ya existe un usuario con ese correo.');
      (await expectRequest(http, '/api/v1/users')).flush(error.body, error.options);

      expect(await within(dialog).findByText('Ya existe un usuario con ese correo.')).toBeTruthy();
      expect(
        within(dialog)
          .getByLabelText(/^Correo electrónico/)
          .getAttribute('aria-invalid'),
      ).toBe('true');
      expect(screen.getByRole('dialog', { name: 'Nuevo usuario' })).toBeTruthy();
    });
  });
});

describe('CompanyUsersPage for the platform admin', () => {
  const BETA_NIT = '800111222';
  const BETA_USERS_URL = `/api/v1/companies/${BETA_NIT}/users`;
  const LOOKUP_URL = '/api/v1/platform/companies';
  /** The company of the URL and another one whose name holds the same digits: only the exact NIT counts. */
  const BETA = {
    id: 'c-2',
    name: 'Beta Ltda',
    nit: BETA_NIT,
    dv: '3',
    email: 'beta@beta.test',
    is_active: true,
    plan_id: null,
    plan_name: null,
  };
  const OTHER = { ...BETA, id: 'c-9', name: `Distribuidora ${BETA_NIT}1 SAS`, nit: '9001112223' };

  /** A platform admin (no active company, `platform:*`); `user-1` is also the id of a user of the list. */
  function renderAsAdmin(nit = BETA_NIT) {
    return renderCompaniesRoute(`/plataforma/empresas/${nit}/usuarios`, {
      roles: ['platform_admin'],
      scopes: ['platform:*'],
      companyId: null,
      directory: [],
    });
  }

  async function answerLookup(http: HttpTestingController, items: object[]) {
    const request = await expectRequest(
      http,
      (candidate) => candidate.method === 'GET' && candidate.url === LOOKUP_URL,
    );
    request.flush({ items, next_cursor: null });
    return request;
  }

  async function openAsAdmin() {
    const rendered = await renderAsAdmin();
    await answerLookup(rendered.http, [OTHER, BETA]);
    await screen.findByRole('heading', { level: 1, name: 'Usuarios de Beta Ltda' });
    await respond(rendered.http, BETA_USERS_URL, { items: USERS });
    const table = await screen.findByRole('table', { name: 'Usuarios de la empresa' });
    await within(table).findByText(CARLOS.email);
    return { ...rendered, table };
  }

  it('finds the company of the URL by its exact NIT and lists its users', async () => {
    const { http } = await renderAsAdmin();

    const lookup = await answerLookup(http, [OTHER, BETA]);
    expect(lookup.request.params.get('search')).toBe(BETA_NIT);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Usuarios de Beta Ltda' }),
    ).toBeTruthy();
    await respond(http, BETA_USERS_URL, { items: USERS });
    expect(await screen.findByText(CARLOS.email)).toBeTruthy();
  });

  it('shows the way back to the platform companies in a breadcrumb', async () => {
    const { router } = await openAsAdmin();

    const trail = screen.getByRole('navigation', { name: 'Ruta de navegación' });
    expect(trail.textContent).toContain('Usuarios de Beta Ltda');
    fireEvent.click(within(trail).getByRole('link', { name: 'Empresas de la plataforma' }));

    await waitFor(() => expect(router.url).toBe('/plataforma/empresas'));
  });

  it('marks no row as the current user and offers the actions on every row', async () => {
    const { table } = await openAsAdmin();

    expect(within(table).queryByText('(usted)')).toBeNull();
    expect(
      within(table).queryByText('Es su cuenta: no puede cambiar su rol ni desactivarla.'),
    ).toBeNull();
    const ana = row(table, ME.email);
    expect(within(ana).getByRole('button', { name: 'Cambiar a vendedor: Ana Gómez' })).toBeTruthy();
    expect(within(ana).getByRole('button', { name: 'Desactivar a Ana Gómez' })).toBeTruthy();
  });

  it('changes the role and deactivates through the NIT of the URL', async () => {
    const { http, table } = await openAsAdmin();

    fireEvent.click(
      within(row(table, CARLOS.email)).getByRole('button', { name: /^Cambiar a dueño/ }),
    );
    const role = await expectRequest(http, `${BETA_USERS_URL}/${CARLOS.id}`);
    expect(role.request.method).toBe('PATCH');
    expect(role.request.body).toEqual({ role: 'owner' });
    role.flush({ ...CARLOS, role: 'owner' });
    await waitFor(() => expect(toastTexts()).toContain('Carlos Ruiz ahora es dueño.'));

    fireEvent.click(within(row(table, ME.email)).getByRole('button', { name: /^Desactivar/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar a Ana Gómez' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Desactivar' }));
    const deactivate = await expectRequest(http, `${BETA_USERS_URL}/${ME.id}`);
    expect(deactivate.request.method).toBe('PATCH');
    expect(deactivate.request.body).toEqual({ is_active: false });
    deactivate.flush({ ...ME, is_active: false });

    await waitFor(() => expect(within(row(table, ME.email)).getByText('Inactivo')).toBeTruthy());
    http.expectNone(`${COMPANY_PATH}/users/${CARLOS.id}`);
  });

  it('creates the new user in the company of the URL', async () => {
    const { http } = await openAsAdmin();
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo usuario' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo usuario' });
    const type = (label: RegExp, value: string) =>
      fireEvent.input(within(dialog).getByLabelText(label), { target: { value } });
    type(/^Correo electrónico/, 'luis@beta.test');
    type(/^Nombre/, 'Luis Vendedor');
    type(/^Contraseña/, 'clave-segura-123');
    await pickOption(within(dialog).getByRole('combobox', { name: /^Rol/ }), 'Vendedor');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));
    const request = await expectRequest(http, '/api/v1/users');
    expect(request.request.body).toEqual({
      company_id: 'c-2',
      email: 'luis@beta.test',
      name: 'Luis Vendedor',
      phone: null,
      password: 'clave-segura-123',
      role: 'seller',
    });
    request.flush({
      id: 'user-9',
      company_id: 'c-2',
      email: 'luis@beta.test',
      name: 'Luis Vendedor',
      role: 'seller',
    });

    await respond(http, BETA_USERS_URL, {
      items: [...USERS, { ...CARLOS, id: 'user-9', email: 'luis@beta.test', name: 'Luis' }],
    });
    expect(await screen.findByText('luis@beta.test')).toBeTruthy();
  });

  it('says when no company has the NIT of the URL and links back to the list', async () => {
    const { http, router } = await renderAsAdmin('123');

    await answerLookup(http, [OTHER]);

    expect(await screen.findByText('No encontramos esa empresa')).toBeTruthy();
    expect(
      screen.getByText(
        'Ninguna empresa de la plataforma tiene el NIT 123. Vuelva a la lista y elíjala de nuevo.',
      ),
    ).toBeTruthy();
    http.expectNone((candidate) => candidate.url.endsWith('/users'));
    fireEvent.click(screen.getByRole('link', { name: 'Volver a Empresas de la plataforma' }));
    await waitFor(() => expect(router.url).toBe('/plataforma/empresas'));
  });

  it('treats a company the users API does not know as not found', async () => {
    const { http } = await renderAsAdmin();
    await answerLookup(http, [BETA]);

    const error = problem(404, 'user.company_not_found', 'No existe.');
    (await expectRequest(http, BETA_USERS_URL)).flush(error.body, error.options);

    expect(await screen.findByText('No encontramos esa empresa')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Volver a Empresas de la plataforma' })).toBeTruthy();
  });

  it('explains when the company cannot be read and retries', async () => {
    const { http } = await renderAsAdmin();
    const error = problem(503, 'unavailable', 'No disponible.');
    (
      await expectRequest(
        http,
        (candidate) => candidate.method === 'GET' && candidate.url === LOOKUP_URL,
      )
    ).flush(error.body, error.options);

    expect(await screen.findByText('No se pudo consultar la empresa')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await answerLookup(http, [BETA]);
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Usuarios de Beta Ltda' }),
    ).toBeTruthy();
  });

  it('keeps the page from an owner without the platform scope', async () => {
    const { router, http } = await renderCompaniesRoute(
      `/plataforma/empresas/${BETA_NIT}/usuarios`,
    );

    await waitFor(() => expect(router.url).toBe('/'));
    http.expectNone((candidate) => candidate.url === LOOKUP_URL);
  });
});
