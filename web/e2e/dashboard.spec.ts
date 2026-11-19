import { expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { signInAt } from './support/sign-in';

// Tablero de `/` (feature-dashboard) y plan de la empresa (`/plan`, feature-plans) contra la Api simulada.

const months = Array.from({ length: 12 }, (_, index) => {
  const month = new Date(Date.UTC(2025, 11 + index, 1)).toISOString().slice(0, 7);
  const total = index === 10 ? 55 : index === 11 ? 42 : index;
  return { month, invoice: total, credit_note: 0, debit_note: 0, other: 0, total };
});

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
  months,
  plan: {
    name: 'Pyme 300',
    period: 'monthly',
    is_active: true,
    expired: false,
    starts_at: '2026-11-01T05:00:00Z',
    period_end: '2026-12-01T04:59:59Z',
    quotas: [
      { kind: 'invoice', used: 270, limit: 300, remaining: 30 },
      { kind: 'payroll', used: 3, limit: null, remaining: null },
    ],
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

test('the dashboard renders the summary from the server', async ({ page }) => {
  await signInAt(page, '/');
  await page.route('**/api/v1/dashboard/summary', (route) => route.fulfill({ json: SUMMARY }));
  await page.reload();

  await expect(page.getByRole('heading', { level: 1, name: 'Tablero' })).toBeVisible();
  const month = page.getByRole('region', { name: 'Documentos de noviembre de 2026' });
  await expect(month.getByText('Aceptado')).toBeVisible();
  await expect(page.getByRole('img', { name: /El mes con más documentos fue/ })).toBeVisible();
  await expect(page.getByRole('meter', { name: 'Facturas y notas' })).toHaveAttribute(
    'aria-valuetext',
    '270 de 300 usados, quedan 30',
  );
  await expect(page.getByText('Vence en 17 días (4 de diciembre de 2026).')).toBeVisible();
  await expect(page.getByText('Quedan 41 números.')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByText('Ver los datos en una tabla').click();
  await expect(page.getByRole('table', { name: 'Documentos por mes' })).toBeVisible();
});

test('the plan page shows what is left per document kind', async ({ page }) => {
  await signInAt(page, '/plan');
  await page.route('**/api/v1/dashboard/summary', (route) => route.fulfill({ json: SUMMARY }));
  await page.reload();

  await expect(page.getByRole('heading', { level: 1, name: 'Mi plan' })).toBeVisible();
  await expect(page.getByText('Pyme 300')).toBeVisible();
  await expect(page.getByText('3 usados, sin límite')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
});
