import { screen, within } from '@testing-library/angular';
import { expectRequest, problem, renderPlatformRoute } from '../testing/platform-testing';

describe('PlanCompaniesPage', () => {
  it('lists the companies that have the plan', async () => {
    const { http } = await renderPlatformRoute('/plataforma/planes/1');

    (await expectRequest(http, '/api/v1/plans/1')).flush({
      success: true,
      data: { id: 1, name: 'Pyme 300', period: 1, state: true },
    });
    (await expectRequest(http, '/api/v1/plans/1/companies')).flush({
      success: true,
      user: [
        {
          name: 'ACME SAS',
          identification_number: '900123456',
          dv: '8',
          email: 'admin@acme.test',
          start_plan_date: '2026-11-01T05:00:00Z',
          state: true,
          type_plan_id: 1,
        },
      ],
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Empresas del plan Pyme 300' }),
    ).toBeTruthy();
    const table = screen.getByRole('table', { name: 'Empresas con el plan' });
    expect(await within(table).findByText('ACME SAS')).toBeTruthy();
    expect(within(table).getByText('900123456-8')).toBeTruthy();
    expect(within(table).getByText('1 de noviembre de 2026')).toBeTruthy();
    const breadcrumb = screen.getByRole('navigation', { name: 'Ruta de navegación' });
    expect(
      within(breadcrumb)
        .getByRole('link', { name: 'Planes de la plataforma' })
        .getAttribute('href'),
    ).toBe('/plataforma/planes');
    expect(
      within(breadcrumb).getByText('Empresas del plan Pyme 300').getAttribute('aria-current'),
    ).toBe('page');
  });

  it('says when the plan does not exist', async () => {
    const { http } = await renderPlatformRoute('/plataforma/planes/9');
    const { body, options } = problem(404, 'plan.not_found', 'Not found');
    (await expectRequest(http, '/api/v1/plans/9')).flush(body, options);
    (await expectRequest(http, '/api/v1/plans/9/companies')).flush(body, options);

    expect(await screen.findByText('El plan no existe.')).toBeTruthy();
  });
});
