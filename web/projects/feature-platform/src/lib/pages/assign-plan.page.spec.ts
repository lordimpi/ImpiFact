import { fireEvent, screen, within } from '@testing-library/angular';
import { expectRequest, problem, renderPlatformRoute } from '../testing/platform-testing';

const PLANS = [
  { id: 1, name: 'Pyme 300', period: 1, state: true },
  { id: 2, name: 'Anual 5000', period: 2, state: true },
];

async function openWithPlans() {
  const result = await renderPlatformRoute('/plataforma/empresas');
  (await expectRequest(result.http, '/api/v1/plans')).flush({ success: true, data: PLANS });
  await screen.findByRole('option', { name: 'Pyme 300' });
  return result;
}

function fillAndAssign(nit: string, plan: string) {
  fireEvent.input(screen.getByLabelText(/NIT de la empresa/), { target: { value: nit } });
  // Signal Forms lee el evento `input` del <select>; el navegador manda también `change`.
  const select = screen.getByLabelText(/^Plan/);
  fireEvent.change(select, { target: { value: plan } });
  fireEvent.input(select, { target: { value: plan } });
  fireEvent.click(screen.getByRole('button', { name: 'Asignar plan' }));
}

describe('AssignPlanPage', () => {
  it('assigns a plan to the company with that NIT', async () => {
    const { http } = await openWithPlans();
    expect(screen.getByText(/todavía no lista las empresas/)).toBeTruthy();

    fillAndAssign('900123456', '2');

    const request = await expectRequest(http, '/api/v1/companies/900123456/plan');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ plan_id: 2 });
    request.flush({
      success: true,
      plan: { id: 2, name: 'Anual 5000' },
      start_plan_date: '2026-11-17T20:00:00Z',
    });

    const status = await screen.findByRole('status');
    expect(
      within(status).getByText(/Se asignó el plan Anual 5000 a la empresa 900123456/),
    ).toBeTruthy();
  });

  it('explains a NIT without a company', async () => {
    const { http } = await openWithPlans();
    fillAndAssign('800000000', '1');

    const { body, options } = problem(404, 'company.not_found', 'Not found');
    (await expectRequest(http, '/api/v1/companies/800000000/plan')).flush(body, options);

    expect(await screen.findByText('No hay una empresa con ese NIT.')).toBeTruthy();
  });

  it('checks the NIT and the plan before calling the API', async () => {
    const { http } = await openWithPlans();

    fillAndAssign('90012-3', '');

    expect(await screen.findByText('Escriba un número entero sin puntos ni comas.')).toBeTruthy();
    expect(screen.getByText('Este campo es obligatorio.')).toBeTruthy();
    http.expectNone((request) => request.url.includes('/companies/'));
  });
});
