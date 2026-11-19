import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderCompaniesRoute } from '../testing/companies-testing';

describe('Company routes and layout', () => {
  it('sends the shortcut to the data of the active company', async () => {
    const { router } = await renderCompaniesRoute('/empresa');

    await waitFor(() => expect(router.url).toBe('/empresas/company-1/datos'));
  });

  it('sends the production shortcut to the wizard of the active company', async () => {
    const { router, http } = await renderCompaniesRoute('/empresa/produccion');

    await waitFor(() => expect(router.url).toBe('/empresas/company-1/produccion'));
    http.match(() => true);
  });

  it('asks to switch companies when the URL names another company', async () => {
    const { router } = await renderCompaniesRoute('/empresas/company-2/datos');

    await waitFor(() =>
      expect(router.url).toBe('/empresas/seleccionar?returnUrl=%2Fempresas%2Fcompany-2%2Fdatos'),
    );
  });

  it('is only for the owner', async () => {
    const { router } = await renderCompaniesRoute('/empresas/company-1/datos', {
      roles: ['seller'],
    });

    await waitFor(() => expect(router.url).toBe('/'));
  });

  it('shows the company name, its NIT and the sections', async () => {
    await renderCompaniesRoute('/empresas/company-1/datos');

    expect(await screen.findByText('Acme SAS')).toBeTruthy();
    expect(screen.getByText('NIT 900123456-7')).toBeTruthy();
    const nav = screen.getByRole('navigation', { name: 'Secciones de la configuración' });
    const links = within(nav)
      .getAllByRole('link')
      .map((link) => [link.textContent?.trim(), link.getAttribute('href')]);
    expect(links).toEqual([
      ['Datos', '/empresas/company-1/datos'],
      ['Ambientes', '/empresas/company-1/ambientes'],
      ['Software', '/empresas/company-1/software'],
      ['Certificado', '/empresas/company-1/certificado'],
      ['Resoluciones', '/empresas/company-1/resoluciones'],
      ['Paso a producción', '/empresas/company-1/produccion'],
    ]);
    expect(within(nav).getByRole('link', { name: 'Datos' }).getAttribute('aria-current')).toBe(
      'page',
    );
  });

  it('explains when the company list could not be read and retries', async () => {
    const { directory } = await renderCompaniesRoute('/empresas/company-1/datos', {
      directory: null,
    });

    expect(await screen.findByText('No encontramos la empresa')).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Datos de la empresa' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(directory.reload).toHaveBeenCalled();
  });
});
