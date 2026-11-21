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
  it(
    'lists the users of the active company with role, status and the current user marked',
    { timeout: 10_000 },
    async () => {
      const { table } = await open();

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
    },
  );

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
    [404, 'user.not_found', 'Este usuario ya no pertenece a la empresa; recargue la lista.'],
    [409, 'concurrency.conflict', 'Otro usuario cambió estos datos; recargue la lista.'],
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

  it('reloads the list on request', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Recargar' }));

    await respond(http, USERS_URL, {
      items: [...USERS, { ...CARLOS, id: 'user-4', email: 'eva@acme.test', name: 'Eva' }],
    });
    expect(await screen.findByText('eva@acme.test')).toBeTruthy();
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
