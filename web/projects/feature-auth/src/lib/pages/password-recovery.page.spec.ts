import { fireEvent, screen } from '@testing-library/angular';
import { expectRequest, problem, renderAuthRoute } from '../testing/render-auth-route';

const URL = '/api/v1/auth/password-reset/request';
const NEUTRAL =
  'Si el correo está registrado, le enviamos un enlace para restablecer la contraseña. Revise su bandeja de entrada y la carpeta de correo no deseado.';

const isRequest = (request: { method: string; url: string }) =>
  request.method === 'POST' && request.url === URL;

async function open() {
  const rendered = await renderAuthRoute('/recuperar');
  await screen.findByRole('heading', { level: 1, name: 'Recuperar la contraseña' });
  return rendered;
}

function send(email: string) {
  fireEvent.input(screen.getByLabelText(/Correo electrónico/), { target: { value: email } });
  fireEvent.click(screen.getByRole('button', { name: 'Enviar el enlace' }));
}

describe('PasswordRecoveryPage', () => {
  it('asks for the email and links back to the login', async () => {
    await open();

    expect(screen.getByLabelText(/Correo electrónico/).getAttribute('type')).toBe('email');
    expect(screen.queryByText(/fase de desarrollo|todavía no está disponible/)).toBeNull();
    expect(screen.getByRole('link', { name: 'Volver a iniciar sesión' }).getAttribute('href')).toBe(
      '/login',
    );
  });

  it('checks the email format before calling the Api', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Enviar el enlace' }));
    expect(await screen.findByText('Escriba su correo electrónico.')).toBeTruthy();

    send('no-es-un-correo');
    expect(
      await screen.findByText(
        'Escriba un correo electrónico válido, por ejemplo nombre@empresa.com.',
      ),
    ).toBeTruthy();
    http.expectNone(URL);
  });

  it('sends the email and shows the same neutral confirmation', async () => {
    const { http } = await open();

    send('ana@example.test');
    const request = await expectRequest(http, isRequest);
    expect(request.request.body).toEqual({ email: 'ana@example.test' });
    request.flush({ message: 'Mensaje del Api' }, { status: 202, statusText: 'Accepted' });

    expect((await screen.findByRole('status')).textContent).toContain(NEUTRAL);
    expect(screen.queryByText('Mensaje del Api')).toBeNull();
    expect(screen.queryByLabelText(/Correo electrónico/)).toBeNull();
    expect(screen.getByRole('link', { name: 'Volver a iniciar sesión' }).getAttribute('href')).toBe(
      '/login',
    );
    http.verify();
  });

  it('shows the neutral confirmation also when the Api fails for another reason', async () => {
    const { http } = await open();

    send('ana@example.test');
    const request = await expectRequest(http, isRequest);
    const { body, options } = problem(500, 'http.unexpected', 'Error interno.');
    request.flush(body, options);

    expect((await screen.findByRole('status')).textContent).toContain(NEUTRAL);
    expect(screen.queryByText('Error interno.')).toBeNull();
  });

  it('shows a format error from the Api next to the field', async () => {
    const { http } = await open();

    send('ana@example.test');
    const request = await expectRequest(http, isRequest);
    const { body, options } = problem(422, 'validation.failed', 'Revise los datos.', {
      email: ['El correo no admite caracteres de control.'],
    });
    request.flush(body, options);

    expect(await screen.findByText('El correo no admite caracteres de control.')).toBeTruthy();
    expect(screen.queryByText(NEUTRAL)).toBeNull();
    expect(screen.getByLabelText(/Correo electrónico/)).toBeTruthy();
  });

  it('says there were too many attempts on 429', async () => {
    const { http } = await open();

    send('ana@example.test');
    const request = await expectRequest(http, isRequest);
    const { body, options } = problem(429, 'rate_limit.exceeded', 'Demasiadas solicitudes.');
    request.flush(body, options);

    expect((await screen.findByRole('alert')).textContent).toBe(
      'Hubo demasiados intentos. Espere un minuto e intente de nuevo.',
    );
    expect(screen.queryByText(NEUTRAL)).toBeNull();
  });

  it('does not claim the link was sent when there is no connection', async () => {
    const { http } = await open();

    send('ana@example.test');
    const request = await expectRequest(http, isRequest);
    request.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect((await screen.findByRole('alert')).textContent).toBe(
      'No se pudo enviar la solicitud. Revise su conexión e intente de nuevo.',
    );
    expect(screen.queryByText(NEUTRAL)).toBeNull();
  });
});
