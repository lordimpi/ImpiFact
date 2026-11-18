import { fireEvent, screen, waitFor } from '@testing-library/angular';
import { expectRequest, problem, renderAuthRoute } from '../testing/render-auth-route';

const URL = '/api/v1/auth/password-reset/confirm';
const TOKEN = 'tok_9f8e7d6c5b4a39281706f5e4d3c2b1a0';
const NEW_PASSWORD = 'Otra-clave-segura-2026';
const INVALID_LINK =
  'El enlace para restablecer la contraseña no sirve: venció, ya se usó o se pidió otro más nuevo.';

const isConfirm = (request: { method: string; url: string }) =>
  request.method === 'POST' && request.url === URL;

const referrerMeta = () => document.head.querySelector('meta[name="referrer"]');

async function open(url = `/restablecer?token=${TOKEN}`, session = {}) {
  const rendered = await renderAuthRoute(url, session);
  await screen.findByRole('heading', { level: 1, name: 'Restablecer la contraseña' });
  return rendered;
}

function fill(password: string, confirmation = password) {
  fireEvent.input(screen.getByLabelText(/^Contraseña nueva/), { target: { value: password } });
  fireEvent.input(screen.getByLabelText(/^Confirme la contraseña nueva/), {
    target: { value: confirmation },
  });
}

const save = () => fireEvent.click(screen.getByRole('button', { name: 'Guardar la contraseña' }));

describe('PasswordResetPage', () => {
  afterEach(() => referrerMeta()?.remove());

  it('takes the token out of the visible URL and sends no referrer while open', async () => {
    const { router } = await open();

    await waitFor(() => expect(router.url).toBe('/restablecer'));
    expect(window.location.href).not.toContain(TOKEN);
    expect(referrerMeta()?.getAttribute('content')).toBe('no-referrer');

    await router.navigateByUrl('/login');
    await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' });
    expect(referrerMeta()).toBeNull();
  });

  it('shows and hides the passwords', async () => {
    await open();

    const password = screen.getByLabelText(/^Contraseña nueva/);
    const confirmation = screen.getByLabelText(/^Confirme la contraseña nueva/);
    expect(password.getAttribute('type')).toBe('password');
    expect(confirmation.getAttribute('type')).toBe('password');

    fireEvent.click(screen.getByRole('checkbox', { name: 'Mostrar las contraseñas' }));
    expect(password.getAttribute('type')).toBe('text');
    expect(confirmation.getAttribute('type')).toBe('text');
  });

  it('checks the length and that both passwords match before calling the Api', async () => {
    const { http } = await open();

    save();
    expect(await screen.findByText('Escriba la contraseña nueva.')).toBeTruthy();

    fill('corta');
    save();
    expect(
      await screen.findByText('La contraseña debe tener al menos 10 caracteres.'),
    ).toBeTruthy();

    fill(NEW_PASSWORD, 'Otra-clave-distinta-2026');
    save();
    expect(await screen.findByText('Las contraseñas no coinciden.')).toBeTruthy();
    http.expectNone(URL);
  });

  it('saves the new password and goes to the login with a notice', async () => {
    const { http, router } = await open();

    fill(NEW_PASSWORD);
    save();
    const request = await expectRequest(http, isConfirm);
    expect(request.request.body).toEqual({ token: TOKEN, new_password: NEW_PASSWORD });
    request.flush(null, { status: 204, statusText: 'No Content' });

    await waitFor(() => expect(router.url).toBe('/login?reason=password-reset'));
    expect(
      await screen.findByText(
        'Su contraseña quedó cambiada. Inicie sesión con la contraseña nueva.',
      ),
    ).toBeTruthy();
  });

  it('closes the stale session of this browser before going to the login', async () => {
    const { http, router, session } = await open(`/restablecer?token=${TOKEN}`, {
      authenticated: true,
      companyId: 'company-1',
    });

    fill(NEW_PASSWORD);
    save();
    (await expectRequest(http, isConfirm)).flush(null, { status: 204, statusText: 'No Content' });

    await waitFor(() => expect(router.url).toBe('/login?reason=password-reset'));
    expect(session.logout).toHaveBeenCalledTimes(1);
  });

  it('explains an invalid link and offers to request a new one', async () => {
    const { http } = await open();

    fill(NEW_PASSWORD);
    save();
    const { body, options } = problem(422, 'identity.invalid_reset_token', 'Token inválido.');
    (await expectRequest(http, isConfirm)).flush(body, options);

    expect((await screen.findByRole('alert')).textContent).toContain(INVALID_LINK);
    expect(screen.getByRole('link', { name: 'Pedir un enlace nuevo' }).getAttribute('href')).toBe(
      '/recuperar',
    );
    expect(screen.queryByLabelText(/^Contraseña nueva/)).toBeNull();
  });

  it('treats a concurrent confirmation (409) as a used link', async () => {
    const { http } = await open();

    fill(NEW_PASSWORD);
    save();
    const { body, options } = problem(409, 'persistence.conflict', 'Conflicto.');
    (await expectRequest(http, isConfirm)).flush(body, options);

    expect((await screen.findByRole('alert')).textContent).toContain(INVALID_LINK);
  });

  it('explains a link without a token without calling the Api', async () => {
    const { http } = await open('/restablecer');

    expect((await screen.findByRole('alert')).textContent).toContain(INVALID_LINK);
    expect(screen.queryByLabelText(/^Contraseña nueva/)).toBeNull();
    http.expectNone(URL);
  });

  it('shows a password policy error from the Api next to the field', async () => {
    const { http } = await open();

    fill(NEW_PASSWORD);
    save();
    const { body, options } = problem(422, 'validation.failed', 'Revise los datos.', {
      new_password: ['La contraseña es demasiado común.'],
    });
    (await expectRequest(http, isConfirm)).flush(body, options);

    expect(await screen.findByText('La contraseña es demasiado común.')).toBeTruthy();
    expect(screen.getByLabelText(/^Contraseña nueva/)).toBeTruthy();
    expect(screen.queryByText(INVALID_LINK)).toBeNull();
  });

  it('says there were too many attempts on 429', async () => {
    const { http } = await open();

    fill(NEW_PASSWORD);
    save();
    const { body, options } = problem(429, 'rate_limit.exceeded', 'Demasiadas solicitudes.');
    (await expectRequest(http, isConfirm)).flush(body, options);

    expect((await screen.findByRole('alert')).textContent).toBe(
      'Hubo demasiados intentos. Espere un minuto e intente de nuevo.',
    );
    expect(screen.getByLabelText(/^Contraseña nueva/)).toBeTruthy();
  });
});
