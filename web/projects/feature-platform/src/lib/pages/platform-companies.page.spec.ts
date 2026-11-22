import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { pickMenuItem, pickOption, stubOptimusDom } from '../testing/optimus-testing';
import { expectRequest, problem, renderPlatformRoute } from '../testing/platform-testing';

const PLANS = [
  { id: 1, name: 'Pyme 300', period: 1, state: true },
  { id: 2, name: 'Anual 5000', period: 2, state: true },
];

const ACME = {
  id: 'c-1',
  name: 'Acme SAS',
  nit: '900123456',
  dv: '7',
  email: 'facturas@acme.test',
  is_active: true,
  plan_id: 1,
  plan_name: 'Pyme 300',
};
const BETA = {
  id: 'c-2',
  name: 'Beta Ltda',
  nit: '800111222',
  dv: '3',
  email: 'beta@beta.test',
  is_active: false,
  plan_id: null,
  plan_name: null,
};

const LIST_URL = '/api/v1/platform/companies';
const isList = (request: { method: string; url: string }) =>
  request.method === 'GET' && request.url === LIST_URL;

async function respondList(
  http: HttpTestingController,
  items: object[],
  nextCursor: string | null = null,
): Promise<TestRequest> {
  const request = await expectRequest(http, isList);
  request.flush({ items, next_cursor: nextCursor });
  return request;
}

async function open(companies: object[] = [ACME, BETA], nextCursor: string | null = null) {
  const result = await renderPlatformRoute('/plataforma/empresas');
  (await expectRequest(result.http, '/api/v1/plans')).flush({ success: true, data: PLANS });
  await respondList(result.http, companies, nextCursor);
  await screen.findByRole('table', { name: 'Empresas de la plataforma' });
  return result;
}

const rowsOf = () =>
  within(screen.getByRole('table', { name: 'Empresas de la plataforma' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent?.trim()),
    );

/** Chooses the plan in the ui-select of the dialog (its list opens over the page). */
async function choosePlan(dialog: HTMLElement, plan: string) {
  const select = within(dialog).getByRole('combobox', { name: /^Plan/ });
  expect(select.textContent?.trim()).toBe('Elija un plan');
  await pickOption(select, plan);
}

beforeAll(stubOptimusDom);

describe('PlatformCompaniesPage', () => {
  it('lists the companies of the platform with their current plan', async () => {
    const { http } = await renderPlatformRoute('/plataforma/empresas');
    (await expectRequest(http, '/api/v1/plans')).flush({ success: true, data: PLANS });
    const request = await respondList(http, [ACME, BETA]);
    expect(request.request.params.get('limit')).toBe('20');
    expect(request.request.params.has('search')).toBe(false);

    await waitFor(() =>
      expect(rowsOf().map((row) => row.slice(0, 5))).toEqual([
        ['Acme SAS', '900123456-7', 'facturas@acme.test', 'Pyme 300', 'Activa'],
        ['Beta Ltda', '800111222-3', 'beta@beta.test', 'Sin plan', 'Inactiva'],
      ]),
    );
    expect(screen.queryByText(/todavía no lista las empresas/)).toBeNull();
    expect(screen.queryByLabelText(/NIT de la empresa/)).toBeNull();
  });

  it('searches on the server after a pause in typing', async () => {
    const { http } = await open();

    expect(
      (screen.getByRole('searchbox', { name: 'Buscar empresa' }) as HTMLInputElement).placeholder,
    ).toBe('Buscar por razón social o NIT');
    fireEvent.input(screen.getByRole('searchbox', { name: 'Buscar empresa' }), {
      target: { value: 'bet' },
    });
    fireEvent.input(screen.getByRole('searchbox', { name: 'Buscar empresa' }), {
      target: { value: 'beta' },
    });

    const search = await respondList(http, [BETA]);
    expect(search.request.params.get('search')).toBe('beta');
    await waitFor(() => expect(rowsOf()).toHaveLength(1));
    http.expectNone(isList);
  });

  it('loads more companies with the cursor', async () => {
    const { http } = await open([ACME], 'cursor-2');

    fireEvent.click(screen.getByRole('button', { name: 'Cargar más empresas' }));

    const more = await respondList(http, [BETA]);
    expect(more.request.params.get('cursor')).toBe('cursor-2');
    await waitFor(() => expect(rowsOf()).toHaveLength(2));
    expect(screen.queryByRole('button', { name: 'Cargar más empresas' })).toBeNull();
  });

  it('says when the platform has no companies', async () => {
    await open([]);

    expect(await screen.findByText('La plataforma aún no tiene empresas')).toBeTruthy();
  });

  it('explains when the companies could not be read and retries', async () => {
    const { http } = await renderPlatformRoute('/plataforma/empresas');
    (await expectRequest(http, '/api/v1/plans')).flush({ success: true, data: PLANS });
    const { body, options } = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, isList)).flush(body, options);

    expect(await screen.findByText('No se pudieron consultar las empresas')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await respondList(http, [ACME]);
    await waitFor(() => expect(rowsOf()).toHaveLength(1));
  });

  it('assigns a plan to the company picked from the list', async () => {
    const { http } = await open();

    await pickMenuItem('Acciones de Beta Ltda', 'Asignar plan');
    const dialog = await screen.findByRole('dialog', { name: 'Asignar plan a Beta Ltda' });
    await choosePlan(dialog, 'Anual 5000');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar plan' }));

    const request = await expectRequest(http, '/api/v1/companies/800111222/plan');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ plan_id: 2 });
    request.flush({
      success: true,
      plan: { id: 2, name: 'Anual 5000' },
      start_plan_date: '2026-11-17T20:00:00Z',
    });

    expect(
      await screen.findByText(/Se asignó el plan Anual 5000 a Beta Ltda \(NIT 800111222\)/),
    ).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(rowsOf()[1]?.[3]).toBe('Anual 5000');
  });

  it('asks for the plan before calling the API', async () => {
    const { http } = await open();

    await pickMenuItem('Acciones de Acme SAS', 'Asignar plan');
    const dialog = await screen.findByRole('dialog', { name: 'Asignar plan a Acme SAS' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar plan' }));

    expect(await within(dialog).findByText('Este campo es obligatorio.')).toBeTruthy();
    http.expectNone((request) => request.url.includes('/plan'));
  });

  it('explains a company that no longer exists', async () => {
    const { http } = await open();
    await pickMenuItem('Acciones de Acme SAS', 'Asignar plan');
    const dialog = await screen.findByRole('dialog', { name: 'Asignar plan a Acme SAS' });
    await choosePlan(dialog, 'Anual 5000');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar plan' }));

    const { body, options } = problem(404, 'company.not_found', 'Not found');
    (await expectRequest(http, '/api/v1/companies/900123456/plan')).flush(body, options);

    expect(await within(dialog).findByText('La empresa ya no existe.')).toBeTruthy();
  });
  it('gathers the actions of every row in one «Acciones» menu', async () => {
    await open();

    const rows = within(screen.getByRole('table', { name: 'Empresas de la plataforma' }))
      .getAllByRole('row')
      .slice(1);
    expect(
      rows.map((row) =>
        within(row)
          .getAllByRole('button')
          .map((b) => b.textContent?.trim()),
      ),
    ).toEqual([['Acciones'], ['Acciones']]);
    const button = within(rows[1]!).getByRole('button', { name: 'Acciones de Beta Ltda' });
    expect(button.getAttribute('aria-haspopup')).toBe('menu');
    expect(screen.queryByRole('link', { name: /^Usuarios de/ })).toBeNull();

    fireEvent.click(button);

    const menu = await screen.findByRole('menu', { name: 'Acciones de Beta Ltda' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent?.trim()),
    ).toEqual(['Asignar plan', 'Crear usuario', 'Usuarios', 'Reactivar']);
  });

  it('opens the users of the company from its row menu', async () => {
    await open();

    await pickMenuItem('Acciones de Beta Ltda', 'Usuarios');

    await waitFor(() =>
      expect(TestBed.inject(Router).url).toBe('/plataforma/empresas/800111222/usuarios'),
    );
  });

  it('keeps the NIT with its DV on one line', async () => {
    await open();

    const cell = within(screen.getByRole('table', { name: 'Empresas de la plataforma' })).getByRole(
      'cell',
      { name: '900123456-7' },
    );
    expect(cell.classList).toContain('ui-table__cell--nowrap');
  });

  it('opens the form of a new company', async () => {
    await open();

    fireEvent.click(screen.getByRole('button', { name: 'Nueva empresa' }));

    await waitFor(() => expect(TestBed.inject(Router).url).toBe('/plataforma/empresas/nueva'));
  });

  describe('user of a company', () => {
    const input = (dialog: HTMLElement, label: RegExp) =>
      within(dialog).getByLabelText(label) as HTMLInputElement;
    const type = (dialog: HTMLElement, label: RegExp, value: string) =>
      fireEvent.input(input(dialog, label), { target: { value } });

    async function openUserDialog(company = 'Beta Ltda') {
      const result = await open();
      await pickMenuItem(`Acciones de ${company}`, 'Crear usuario');
      const dialog = await screen.findByRole('dialog', { name: `Crear usuario de ${company}` });
      return { ...result, dialog };
    }

    function fill(dialog: HTMLElement) {
      type(dialog, /^Correo electrónico/, 'vende@beta.test');
      type(dialog, /^Nombre/, 'Luis Vendedor');
      type(dialog, /^Teléfono/, '3001234567');
      type(dialog, /^Contraseña/, 'una-clave-larga-1');
    }

    const submitUser = (dialog: HTMLElement) =>
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));

    it('creates a seller of the company picked from the list and forgets the password', async () => {
      const { http, dialog } = await openUserDialog();
      expect(input(dialog, /^Contraseña/).type).toBe('password');
      expect(input(dialog, /^Correo electrónico/).placeholder).toBe('nombre@empresa.com');
      expect(input(dialog, /^Nombre/).placeholder).toBe('Ana Gómez');
      expect(input(dialog, /^Teléfono/).placeholder).toBe('3001234567');
      fill(dialog);
      const role = within(dialog).getByRole('combobox', { name: /^Rol/ });
      expect(role.textContent?.trim()).toBe('Dueño');
      await pickOption(role, 'Vendedor');

      submitUser(dialog);
      const request = await expectRequest(http, '/api/v1/users');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        company_id: 'c-2',
        email: 'vende@beta.test',
        name: 'Luis Vendedor',
        phone: '3001234567',
        password: 'una-clave-larga-1',
        role: 'seller',
      });
      // La contraseña sale del formulario apenas se envía.
      expect(input(dialog, /^Contraseña/).value).toBe('');
      request.flush(
        {
          id: 'u-2',
          email: 'vende@beta.test',
          name: 'Luis Vendedor',
          role: 'seller',
          company_id: 'c-2',
        },
        { status: 201, statusText: 'Created' },
      );

      expect(
        await screen.findByText(
          'Se creó el usuario vende@beta.test como vendedor de Beta Ltda; ya puede iniciar sesión.',
        ),
      ).toBeTruthy();
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

      // Al abrirlo otra vez, el formulario empieza vacío.
      await pickMenuItem('Acciones de Beta Ltda', 'Crear usuario');
      const again = await screen.findByRole('dialog', { name: 'Crear usuario de Beta Ltda' });
      expect(input(again, /^Correo electrónico/).value).toBe('');
      expect(input(again, /^Contraseña/).value).toBe('');
    });

    it('shows the password policy error of the API under the password', async () => {
      const { http, dialog } = await openUserDialog();
      fill(dialog);
      type(dialog, /^Contraseña/, 'corta');

      submitUser(dialog);
      const error = problem(422, 'validation.failed', 'Los datos no son válidos.', {
        password: ['La contraseña debe tener al menos 10 caracteres.'],
      });
      (await expectRequest(http, '/api/v1/users')).flush(error.body, error.options);

      expect(
        await within(dialog).findByText('La contraseña debe tener al menos 10 caracteres.'),
      ).toBeTruthy();
      expect(input(dialog, /^Contraseña/).getAttribute('aria-invalid')).toBe('true');
      expect(input(dialog, /^Contraseña/).value).toBe('');
    });

    it('shows a repeated email next to the email field', async () => {
      const { http, dialog } = await openUserDialog();
      fill(dialog);

      submitUser(dialog);
      const error = problem(409, 'user.email_taken', 'Ya existe un usuario con ese correo.');
      (await expectRequest(http, '/api/v1/users')).flush(error.body, error.options);

      expect(await within(dialog).findByText('Ya existe un usuario con ese correo.')).toBeTruthy();
      expect(input(dialog, /^Correo electrónico/).getAttribute('aria-invalid')).toBe('true');
    });

    it('asks for the required fields before calling the API', async () => {
      const { http, dialog } = await openUserDialog();

      submitUser(dialog);

      expect((await within(dialog).findAllByText('Este campo es obligatorio.')).length).toBe(3);
      http.expectNone('/api/v1/users');
    });
  });
});

describe('PlatformCompaniesPage activation', { timeout: 15_000 }, () => {
  const DEACTIVATE = '/api/v1/platform/companies/900123456/deactivate';
  const REACTIVATE = '/api/v1/platform/companies/800111222/reactivate';

  afterEach(() => {
    // The confirm dialog lives under <body>, outside any test fixture.
    document.body.querySelectorAll('ui-confirm-dialog').forEach((host) => host.remove());
  });

  /** The confirm dialog; the first question loads its chunk, which can take longer than the default wait. */
  const confirmDialog = (name: string) =>
    screen.findByRole('alertdialog', { name }, { timeout: 8000 });

  // The inactive company (Beta) gets «Reactivar»: see «gathers the actions of every row in one «Acciones» menu».
  it('offers Desactivar to an active company', async () => {
    await open();

    fireEvent.click(screen.getByRole('button', { name: 'Acciones de Acme SAS' }));
    const menu = await screen.findByRole('menu', { name: 'Acciones de Acme SAS' });

    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent?.trim()),
    ).toEqual(['Asignar plan', 'Crear usuario', 'Usuarios', 'Desactivar']);
  });

  it('deactivates after the confirmation that explains the effects and updates the row by itself', async () => {
    const { http } = await open();

    await pickMenuItem('Acciones de Acme SAS', 'Desactivar');
    const dialog = await confirmDialog('¿Desactivar Acme SAS?');
    expect(dialog.textContent).toContain('dejará de emitir documentos');
    expect(dialog.textContent).toContain('Sus API keys se revocan para siempre');
    expect(dialog.textContent).toContain('sus usuarios pierden el acceso');
    expect(dialog.textContent).toContain('Las facturas y demás documentos se conservan');
    within(dialog).getByRole('button', { name: 'Desactivar empresa' }).click();

    const request = await expectRequest(http, DEACTIVATE);
    expect(request.request.method).toBe('POST');
    request.flush({
      ...ACME,
      is_active: false,
      changed: true,
      revoked_api_keys: 2,
      signed_out_users: 3,
    });

    expect(
      await screen.findByText(
        'Se desactivó Acme SAS (NIT 900123456). API keys revocadas: 2. Usuarios con la sesión cerrada: 3.',
      ),
    ).toBeTruthy();
    await waitFor(() => expect(rowsOf()[0]?.[4]).toBe('Inactiva'));
    http.expectNone(isList);
  });

  it('does not call the API when the person cancels', async () => {
    const { http } = await open();

    await pickMenuItem('Acciones de Acme SAS', 'Desactivar');
    const dialog = await confirmDialog('¿Desactivar Acme SAS?');
    within(dialog).getByRole('button', { name: 'Cancelar' }).click();

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    http.expectNone(DEACTIVATE);
    expect(rowsOf()[0]?.[4]).toBe('Activa');
  });

  it('reactivates an inactive company and says the old keys stay revoked', async () => {
    const { http } = await open();

    await pickMenuItem('Acciones de Beta Ltda', 'Reactivar');
    const dialog = await confirmDialog('¿Reactivar Beta Ltda?');
    expect(dialog.textContent).toContain('Las API keys revocadas no vuelven');
    within(dialog).getByRole('button', { name: 'Reactivar empresa' }).click();
    (await expectRequest(http, REACTIVATE)).flush({ ...BETA, is_active: true, changed: true });

    expect(await screen.findByText(/Se reactivó Beta Ltda \(NIT 800111222\)/)).toBeTruthy();
    await waitFor(() => expect(rowsOf()[1]?.[4]).toBe('Activa'));
  });

  it('shows a toast when the credential is not the platform admin', async () => {
    const { http } = await open();

    await pickMenuItem('Acciones de Acme SAS', 'Desactivar');
    within(await confirmDialog('¿Desactivar Acme SAS?'))
      .getByRole('button', { name: 'Desactivar empresa' })
      .click();
    const error = problem(403, 'company.platform_only', 'Solo la plataforma.');
    (await expectRequest(http, DEACTIVATE)).flush(error.body, error.options);

    const toasts = TestBed.inject(ToastStore);
    await waitFor(() =>
      expect(toasts.toasts().map((toast) => toast.text)).toContain(
        'Solo el administrador de la plataforma puede desactivar o reactivar empresas.',
      ),
    );
    expect(rowsOf()[0]?.[4]).toBe('Activa');
    http.expectNone(isList);
  });

  it('shows a toast and reads the list again on a conflict', async () => {
    const { http } = await open();

    await pickMenuItem('Acciones de Acme SAS', 'Desactivar');
    within(await confirmDialog('¿Desactivar Acme SAS?'))
      .getByRole('button', { name: 'Desactivar empresa' })
      .click();
    const error = problem(409, 'concurrency.conflict', 'Conflicto.');
    (await expectRequest(http, DEACTIVATE)).flush(error.body, error.options);

    await respondList(http, [{ ...ACME, is_active: false }, BETA]);
    const toasts = TestBed.inject(ToastStore);
    expect(toasts.toasts().map((toast) => toast.text)).toContain(
      'Otra persona cambió la empresa al mismo tiempo. La lista se actualizó; revise su estado e intente de nuevo.',
    );
    await waitFor(() => expect(rowsOf()[0]?.[4]).toBe('Inactiva'));
  });
});
