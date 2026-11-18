import { SKIP_ERROR_TOAST } from '@impifact/core';
import { fireEvent, screen, within } from '@testing-library/angular';
import { expectRequest, problem, renderPlansRoute } from '../testing/plans-testing';

const SUMMARY_URL = '/api/v1/dashboard/summary';

const PLAN = {
  name: 'Pyme 300',
  period: 'monthly',
  is_active: true,
  expired: false,
  starts_at: '2026-11-01T05:00:00Z',
  period_end: '2026-12-01T04:59:59Z',
  quotas: [
    { kind: 'invoice', used: 120, limit: 300, remaining: 180 },
    { kind: 'payroll', used: 3, limit: null, remaining: null },
  ],
};

const summary = (plan: unknown) => ({
  generated_at: '2026-11-17T15:00:00Z',
  current_month: { month: '2026-11', total: 0, by_status: [] },
  months: [],
  plan,
  certificate: null,
  resolutions: [],
});

describe('CurrentPlanPage', () => {
  it('shows the plan, its period end and what is left per document kind', async () => {
    const { http } = await renderPlansRoute('/plan');

    const request = await expectRequest(http, SUMMARY_URL);
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
    request.flush(summary(PLAN));

    expect(await screen.findByRole('heading', { level: 1, name: 'Mi plan' })).toBeTruthy();
    expect(await screen.findByText('Pyme 300')).toBeTruthy();
    expect(screen.getByText('Mensual')).toBeTruthy();
    expect(screen.getByText('Vigente')).toBeTruthy();
    expect(screen.getByText('30 de noviembre de 2026')).toBeTruthy();

    const invoices = screen.getByRole('meter', { name: 'Facturas y notas' });
    expect(invoices.getAttribute('aria-valuetext')).toBe('120 de 300 usados, quedan 180');
    expect(screen.getByText('3 usados, sin límite')).toBeTruthy();
    expect(screen.queryByRole('meter', { name: 'Nómina electrónica' })).toBeNull();
  });

  it('explains when the company has no plan', async () => {
    const { http } = await renderPlansRoute('/plan');
    (await expectRequest(http, SUMMARY_URL)).flush(summary(null));

    expect(await screen.findByText('Su empresa no tiene un plan contratado')).toBeTruthy();
  });

  it('marks an expired plan', async () => {
    const { http } = await renderPlansRoute('/plan');
    (await expectRequest(http, SUMMARY_URL)).flush(
      summary({ ...PLAN, expired: true, is_active: false }),
    );

    const status = await screen.findByText('Vencido');
    expect(within(status.closest('dl')!).getByText('Pyme 300')).toBeTruthy();
  });

  it('shows the error state and retries', async () => {
    const { http } = await renderPlansRoute('/plan');
    const { body, options } = problem(500, 'internal', 'Falla');
    (await expectRequest(http, SUMMARY_URL)).flush(body, options);

    expect(await screen.findByText('No se pudo cargar el plan')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    (await expectRequest(http, SUMMARY_URL)).flush(summary(PLAN));
    expect(await screen.findByText('Pyme 300')).toBeTruthy();
  });
});
