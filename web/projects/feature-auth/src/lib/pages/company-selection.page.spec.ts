import { AppError } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderAuthRoute } from '../testing/render-auth-route';

const SIGNED_IN = {
  authenticated: true,
  companyId: null,
  companies: ['3f2a9c1e-0000-4000-8000-000000000001', '3f2a9c1e-0000-4000-8000-000000000002'],
};

describe('CompanySelectionPage', () => {
  it('lists the companies of the session with their identifier', async () => {
    await renderAuthRoute('/empresas/seleccionar', {
      ...SIGNED_IN,
      companyId: SIGNED_IN.companies[1],
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Seleccionar empresa' }),
    ).toBeTruthy();
    const list = screen.getByRole('list', { name: 'Empresas disponibles' });
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(within(items[0]!).getByText('Empresa 1')).toBeTruthy();
    expect(within(items[0]!).getByText(`Identificador: ${SIGNED_IN.companies[0]}`)).toBeTruthy();
    expect(within(items[1]!).getByText('Empresa activa')).toBeTruthy();
    expect(
      within(items[1]!).getByRole('button', { name: 'Trabajar con la empresa 2' }),
    ).toBeTruthy();
  });

  it('switches the session to the chosen company and goes back to the requested page', async () => {
    const { session, router } = await renderAuthRoute(
      '/empresas/seleccionar?returnUrl=%2Fdocumentos',
      SIGNED_IN,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Trabajar con la empresa 2' }));

    await waitFor(() => expect(router.url).toBe('/documentos'));
    expect(session.selectCompany).toHaveBeenCalledExactlyOnceWith(SIGNED_IN.companies[1]);
  });

  it('goes home after choosing when nothing asked for the session', async () => {
    const { router } = await renderAuthRoute('/empresas/seleccionar', SIGNED_IN);

    fireEvent.click(await screen.findByRole('button', { name: 'Trabajar con la empresa 1' }));

    await waitFor(() => expect(router.url).toBe('/'));
  });

  it('marks the chosen company as busy and blocks the others while switching', async () => {
    const { session } = await renderAuthRoute('/empresas/seleccionar', SIGNED_IN);
    let finish!: () => void;
    session.selectCompany.mockImplementationOnce(
      () => new Promise<void>((resolve) => (finish = resolve)),
    );

    const first = await screen.findByRole('button', { name: 'Trabajar con la empresa 1' });
    fireEvent.click(first);

    await waitFor(() => expect(first.getAttribute('aria-busy')).toBe('true'));
    const second = screen.getByRole('button', { name: 'Trabajar con la empresa 2' });
    expect((second as HTMLButtonElement).disabled).toBe(true);
    finish();
  });

  it('stays on the page with an error when the API refuses the company', async () => {
    const { session, router } = await renderAuthRoute('/empresas/seleccionar', SIGNED_IN);
    session.selectCompany.mockRejectedValueOnce(
      new AppError('identity.company_not_allowed', 'No pertenece a la empresa.', 403),
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Trabajar con la empresa 1' }));

    expect((await screen.findByRole('alert')).textContent).toContain(
      'No tiene acceso a esa empresa. Elija otra.',
    );
    expect(router.url).toBe('/empresas/seleccionar');
    expect(
      (screen.getByRole('button', { name: 'Trabajar con la empresa 2' }) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
  });

  it('sends the user to the login when the session ended meanwhile', async () => {
    const { session, router } = await renderAuthRoute(
      '/empresas/seleccionar?returnUrl=%2Fdocumentos',
      SIGNED_IN,
    );
    session.selectCompany.mockImplementationOnce(async () => {
      session.isAuthenticated.set(false);
      throw new AppError('identity.invalid_refresh_token', 'Sesión vencida.', 401);
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Trabajar con la empresa 1' }));

    await waitFor(() => expect(router.url).toBe('/login?returnUrl=%2Fdocumentos'));
  });

  it('explains when the user has no companies', async () => {
    await renderAuthRoute('/empresas/seleccionar', { authenticated: true, companies: [] });

    expect(await screen.findByText('No tiene empresas asignadas')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Ir al inicio' }).getAttribute('href')).toBe('/');
    expect(screen.queryByRole('list')).toBeNull();
  });

  it('requires a session', async () => {
    const { router } = await renderAuthRoute('/empresas/seleccionar');

    await waitFor(() => expect(router.url).toBe('/login?returnUrl=%2Fempresas%2Fseleccionar'));
  });
});
