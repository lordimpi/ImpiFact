import { screen } from '@testing-library/angular';
import { renderAuthRoute } from '../testing/render-auth-route';

describe('PasswordRecoveryPage', () => {
  it('says that recovery is not available yet and links back to the login', async () => {
    await renderAuthRoute('/recuperar');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Recuperar la contraseña' }),
    ).toBeTruthy();
    expect(screen.getByText(/todavía no está disponible/)).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Volver a iniciar sesión' }).getAttribute('href')).toBe(
      '/login',
    );
  });
});
