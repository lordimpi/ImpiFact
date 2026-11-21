import { AppError } from '@impifact/core';
import { fireEvent, screen, waitFor } from '@testing-library/angular';
import { renderAuthRoute } from '../testing/render-auth-route';

describe('LoginPage', () => {
  async function fillAndSubmit(email: string, password: string) {
    fireEvent.input(screen.getByLabelText(/Correo electrónico/), { target: { value: email } });
    fireEvent.input(screen.getByLabelText(/Contraseña/), { target: { value: password } });
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
  }

  it('renders labelled fields, the submit button and the recovery link', async () => {
    await renderAuthRoute('/login');

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeTruthy();
    const email = screen.getByLabelText(/Correo electrónico/) as HTMLInputElement;
    expect(email.type).toBe('email');
    expect(email.autocomplete).toBe('username');
    expect(email.placeholder).toBe('nombre@empresa.com');
    const password = screen.getByLabelText(/Contraseña/) as HTMLInputElement;
    expect(password.type).toBe('password');
    expect(password.autocomplete).toBe('current-password');
    // A password field never shows an example password.
    expect(password.placeholder).toBe('');
    expect(screen.getByRole('link', { name: '¿Olvidó su contraseña?' }).getAttribute('href')).toBe(
      '/recuperar',
    );
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('explains a logout for inactivity', async () => {
    await renderAuthRoute('/login?reason=inactivity');

    expect(
      (await screen.findByRole('status')).textContent?.includes(
        'Su sesión se cerró por inactividad',
      ),
    ).toBe(true);
  });

  it('shows the required errors next to each field and does not call the API', async () => {
    const { session } = await renderAuthRoute('/login');
    await screen.findByRole('heading', { name: 'Iniciar sesión' });

    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));

    expect(await screen.findByText('Escriba su correo electrónico.')).toBeTruthy();
    expect(screen.getByText('Escriba su contraseña.')).toBeTruthy();
    expect(screen.getByLabelText(/Correo electrónico/).getAttribute('aria-invalid')).toBe('true');
    expect(session.login).not.toHaveBeenCalled();
  });

  it('rejects an email without the right shape', async () => {
    const { session } = await renderAuthRoute('/login');
    await screen.findByRole('heading', { name: 'Iniciar sesión' });

    await fillAndSubmit('ana', 'secreta-larga');

    expect(await screen.findByText(/Escriba un correo electrónico válido/)).toBeTruthy();
    expect(session.login).not.toHaveBeenCalled();
  });

  it('logs in and goes back to the page that asked for the session', async () => {
    const { session, router } = await renderAuthRoute('/login?returnUrl=%2Fdocumentos', {
      companyId: 'company-1',
      companies: ['company-1'],
    });
    await screen.findByRole('heading', { name: 'Iniciar sesión' });

    await fillAndSubmit('ana@example.test', 'secreta-larga');

    await waitFor(() => expect(router.url).toBe('/documentos'));
    expect(session.login).toHaveBeenCalledWith({
      email: 'ana@example.test',
      password: 'secreta-larga',
    });
  });

  it('goes home after login when nothing asked for the session', async () => {
    const { router } = await renderAuthRoute('/login', { companyId: 'company-1' });
    await screen.findByRole('heading', { name: 'Iniciar sesión' });

    await fillAndSubmit('ana@example.test', 'secreta-larga');

    await waitFor(() => expect(router.url).toBe('/'));
  });

  it('ignores a return address outside the panel', async () => {
    const { router } = await renderAuthRoute('/login?returnUrl=%2F%2Fotro.sitio%2Fx', {
      companyId: 'company-1',
    });
    await screen.findByRole('heading', { name: 'Iniciar sesión' });

    await fillAndSubmit('ana@example.test', 'secreta-larga');

    await waitFor(() => expect(router.url).toBe('/'));
  });

  it('asks for a company when the user has several and none is active', async () => {
    const { router } = await renderAuthRoute('/login?returnUrl=%2Fdocumentos', {
      companyId: null,
      companies: ['company-1', 'company-2'],
    });
    await screen.findByRole('heading', { name: 'Iniciar sesión' });

    await fillAndSubmit('ana@example.test', 'secreta-larga');

    await waitFor(() => expect(router.url).toBe('/empresas/seleccionar?returnUrl=%2Fdocumentos'));
  });

  it('shows the wrong credentials error and keeps the form usable', async () => {
    const { session, router } = await renderAuthRoute('/login');
    await screen.findByRole('heading', { name: 'Iniciar sesión' });
    session.login.mockRejectedValueOnce(
      new AppError('identity.invalid_credentials', 'Credenciales inválidas.', 401),
    );

    await fillAndSubmit('ana@example.test', 'equivocada');

    expect((await screen.findByRole('alert')).textContent).toContain(
      'El correo o la contraseña no son correctos.',
    );
    expect(router.url).toBe('/login');
    const button = screen.getByRole('button', { name: 'Ingresar' }) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
  });

  it('shows the field errors that the API returns next to their field', async () => {
    const { session } = await renderAuthRoute('/login');
    await screen.findByRole('heading', { name: 'Iniciar sesión' });
    session.login.mockRejectedValueOnce(
      new AppError('validation.failed', 'Datos inválidos.', 400, {
        email: ['El correo no está registrado como usuario del panel.'],
      }),
    );

    await fillAndSubmit('ana@example.test', 'secreta-larga');

    expect(
      await screen.findByText('El correo no está registrado como usuario del panel.'),
    ).toBeTruthy();
    expect(screen.getByLabelText(/Correo electrónico/).getAttribute('aria-invalid')).toBe('true');
  });

  it('shows the API message for other errors', async () => {
    const { session } = await renderAuthRoute('/login');
    await screen.findByRole('heading', { name: 'Iniciar sesión' });
    session.login.mockRejectedValueOnce(
      new AppError('network.unavailable', 'No hay conexión con el servidor.', 0),
    );

    await fillAndSubmit('ana@example.test', 'secreta-larga');

    expect((await screen.findByRole('alert')).textContent).toContain(
      'No hay conexión con el servidor.',
    );
  });

  it('marks the button as busy while the login is in flight', async () => {
    const { session } = await renderAuthRoute('/login');
    await screen.findByRole('heading', { name: 'Iniciar sesión' });
    let finish!: () => void;
    session.login.mockImplementationOnce(() => new Promise<void>((resolve) => (finish = resolve)));

    await fillAndSubmit('ana@example.test', 'secreta-larga');

    const button = screen.getByRole('button', { name: 'Ingresar' }) as HTMLButtonElement;
    await waitFor(() => expect(button.getAttribute('aria-busy')).toBe('true'));
    expect(button.disabled).toBe(true);
    finish();
  });

  it('sends a signed-in user to the home page', async () => {
    const { router } = await renderAuthRoute('/login', { authenticated: true });

    await waitFor(() => expect(router.url).toBe('/'));
  });
});
