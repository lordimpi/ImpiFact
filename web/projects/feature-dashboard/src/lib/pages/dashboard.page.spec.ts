import { SKIP_ERROR_TOAST } from '@impifact/core';
import { fireEvent, screen, within } from '@testing-library/angular';
import { expectRequest, problem, renderDashboardRoute } from '../testing/dashboard-testing';

const SUMMARY_URL = '/api/v1/dashboard/summary';

/** Los 12 meses del más antiguo al actual, como los devuelve el Api; `totals` cambia algunos. */
function months(totals: Record<string, number> = {}) {
  return Array.from({ length: 12 }, (_, index) => {
    const date = new Date(Date.UTC(2025, 11 + index, 1));
    const month = date.toISOString().slice(0, 7);
    const total = totals[month] ?? 0;
    return { month, invoice: total, credit_note: 0, debit_note: 0, other: 0, total };
  });
}

const SUMMARY = {
  generated_at: '2026-11-17T15:00:00Z',
  current_month: {
    month: '2026-11',
    total: 42,
    by_status: [
      { status: 'Accepted', count: 40 },
      { status: 'Rejected', count: 2 },
    ],
  },
  months: months({ '2026-10': 55, '2026-11': 42 }),
  plan: {
    name: 'Pyme 300',
    period: 'monthly',
    is_active: true,
    expired: false,
    starts_at: '2026-11-01T05:00:00Z',
    period_end: '2026-12-01T04:59:59Z',
    quotas: [{ kind: 'invoice', used: 270, limit: 300, remaining: 30 }],
  },
  certificate: {
    name: 'ACME SAS',
    not_after: '2026-12-05T04:59:59Z',
    days_left: 17,
    expired: false,
  },
  resolutions: [
    {
      id: 'r-1',
      document_type_id: 1,
      kind: 'invoice',
      prefix: 'SETP',
      resolution_number: '18760000001',
      range_from: 1,
      range_to: 1000,
      next_number: 960,
      remaining_numbers: 41,
      valid_to: '2027-06-30',
      days_left: 225,
      numbers_low: true,
      expiring: false,
      expired: false,
    },
  ],
};

describe('DashboardPage', () => {
  it('renders the summary cards from the server', async () => {
    const { http } = await renderDashboardRoute('/');

    const request = await expectRequest(http, SUMMARY_URL);
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
    request.flush(SUMMARY);

    expect(await screen.findByRole('heading', { level: 1, name: 'Tablero' })).toBeTruthy();
    const month = screen.getByRole('region', { name: 'Documentos de noviembre de 2026' });
    expect(within(month).getByText('42')).toBeTruthy();
    expect(within(month).getByText('Aceptado')).toBeTruthy();
    expect(within(month).getByText('Rechazado')).toBeTruthy();

    const chart = screen.getByRole('img', {
      name: /El mes con más documentos fue oct\.? 2026, con 55/,
    });
    expect(chart.querySelectorAll('.chart-bar')).toHaveLength(12);
    expect(screen.getByRole('table', { name: 'Documentos por mes' })).toBeTruthy();

    const meter = screen.getByRole('meter', { name: 'Facturas y notas' });
    expect(meter.getAttribute('aria-valuetext')).toBe('270 de 300 usados, quedan 30');
    expect(screen.getByText('El periodo termina el 30 de noviembre de 2026.')).toBeTruthy();

    expect(screen.getByText('Vence en 17 días (4 de diciembre de 2026).')).toBeTruthy();
    const resolutions = screen.getByRole('region', { name: 'Resoluciones por agotarse' });
    expect(within(resolutions).getByText(/SETP/)).toBeTruthy();
    expect(within(resolutions).getByText('Quedan 41 números.')).toBeTruthy();
  });

  it('shows the empty states of a new company', async () => {
    const { http } = await renderDashboardRoute('/');
    (await expectRequest(http, SUMMARY_URL)).flush({
      ...SUMMARY,
      current_month: { month: '2026-11', total: 0, by_status: [] },
      months: months(),
      plan: null,
      certificate: null,
      resolutions: [],
    });

    expect(await screen.findByText('Aún no hay documentos este mes.')).toBeTruthy();
    expect(screen.getByText('No hay documentos en los últimos 12 meses.')).toBeTruthy();
    expect(screen.getByText('Su empresa no tiene un plan contratado.')).toBeTruthy();
    expect(screen.getByText(/No hay un certificado activo/)).toBeTruthy();
    expect(screen.getByText('Ninguna resolución está por agotarse.')).toBeTruthy();
  });

  it('shows the error state and retries', async () => {
    const { http } = await renderDashboardRoute('/');
    const { body, options } = problem(500, 'internal', 'Falla');
    (await expectRequest(http, SUMMARY_URL)).flush(body, options);

    expect(await screen.findByText('No se pudo cargar el tablero')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    (await expectRequest(http, SUMMARY_URL)).flush(SUMMARY);
    expect(await screen.findByText('Pyme 300')).toBeTruthy();
  });

  it('leaves the root route to the home page for other roles', async () => {
    const { http } = await renderDashboardRoute('/', { roles: ['customer'] });

    expect(await screen.findByText('stub')).toBeTruthy();
    http.expectNone(SUMMARY_URL);
  });
});
