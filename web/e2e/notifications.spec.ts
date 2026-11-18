import { expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { signInAt } from './support/sign-in';

// Campana de avisos y página `/notificaciones` (feature-notifications) contra la Api simulada.

const notice = (id: string, isRead = false) => ({
  id,
  type: 'certificate.expiring',
  title: `Aviso ${id}`,
  body: 'El certificado de firma vence pronto.',
  is_read: isRead,
  created_at: '2026-11-17T15:30:00Z',
});

test('the bell count follows the 60-second poll and drops after marking all', async ({ page }) => {
  // Reloj simulado: se adelantan los 60 s del sondeo sin esperarlos.
  await page.clock.install();
  let unread = 2;
  let readAll = 0;
  await signInAt(page, '/');
  await page.route(/\/api\/v1\/notifications(\?|$)/, (route) =>
    route.fulfill({
      json: {
        items: unread > 0 ? [notice('n-1'), notice('n-2')] : [notice('n-1', true)],
        next_cursor: null,
        unread_count: unread,
      },
    }),
  );
  await page.route('**/api/v1/notifications/read-all', (route) => {
    readAll++;
    unread = 0;
    return route.fulfill({ json: { marked_count: 2 } });
  });

  // La primera consulta pasó antes de registrar la ruta (sin avisos); el siguiente ciclo trae los dos.
  await page.clock.runFor(60_000);
  const bell = page.getByRole('button', { name: 'Avisos, 2 sin leer' });
  await expect(bell).toBeVisible();

  await bell.click();
  const panel = page.getByRole('region', { name: 'Avisos' });
  await expect(panel.getByText('Aviso n-1')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await panel.getByRole('button', { name: 'Marcar todos como leídos' }).click();
  await expect(page.getByRole('button', { name: 'Avisos', exact: true })).toBeVisible();
  expect(readAll).toBe(1);
});

test('the notifications page pages on the server and filters the unread ones', async ({ page }) => {
  const queries: string[] = [];
  await signInAt(page, '/notificaciones');
  await page.route(/\/api\/v1\/notifications(\?|$)/, (route) => {
    const url = new URL(route.request().url());
    queries.push(url.search);
    if (url.searchParams.get('limit') === '5') {
      return route.fulfill({ json: { items: [], next_cursor: null, unread_count: 1 } });
    }
    return route.fulfill({
      json: {
        items: [notice(url.searchParams.get('cursor') ? 'n-21' : 'n-1')],
        next_cursor: url.searchParams.get('cursor') ? null : 'cursor-2',
        unread_count: 1,
      },
    });
  });
  await page.reload();

  const table = page.getByRole('table', { name: 'Avisos' });
  await expect(table.getByText('Aviso n-1')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Siguiente' }).click();
  await expect(table.getByText('Aviso n-21')).toBeVisible();
  expect(queries).toContain('?cursor=cursor-2&limit=20');

  await page.getByLabel('Mostrar').selectOption('unread');
  await expect(page).toHaveURL('/notificaciones?estado=sin-leer');
  await expect.poll(() => queries.at(-1)).toBe('?unread=true&limit=20');
});
