import { Page, expect, test } from '@playwright/test';
import { seriousViolationsInBothThemes } from './support/axe';
import { mockApi } from './support/mock-api';

// Recuperación de contraseña (feature-auth): pedir el enlace en `/recuperar` y fijar la contraseña nueva en
// `/restablecer?token=...`, contra la Api simulada.

const TOKEN = 'tok-e2e-9f8e7d6c5b4a39281706f5e4d3c2b1a0';
const NEW_PASSWORD = 'Otra-clave-segura-2026';
const NEUTRAL =
  'Si el correo está registrado, le enviamos un enlace para restablecer la contraseña. Revise su bandeja de entrada y la carpeta de correo no deseado.';

interface ResetCalls {
  requests: unknown[];
  confirms: { body: unknown; referer: string | undefined }[];
}

/** Simula los dos endpoints como el Api: el pedido responde siempre 202; la confirmación, 204 o 422. */
async function mockPasswordReset(page: Page): Promise<ResetCalls> {
  const calls: ResetCalls = { requests: [], confirms: [] };
  await page.route('**/api/v1/auth/password-reset/request', (route) => {
    calls.requests.push(route.request().postDataJSON());
    return route.fulfill({
      status: 202,
      json: { message: 'Si el correo está registrado, enviamos un enlace.' },
    });
  });
  await page.route('**/api/v1/auth/password-reset/confirm', async (route) => {
    const body = route.request().postDataJSON() as { token: string };
    calls.confirms.push({
      body,
      referer: (await route.request().headerValue('referer')) ?? undefined,
    });
    if (body.token !== TOKEN) {
      return route.fulfill({
        status: 422,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          status: 422,
          code: 'identity.invalid_reset_token',
          message: 'El enlace no es válido.',
          correlation_id: 'e2e-correlation',
        }),
      });
    }
    return route.fulfill({ status: 204 });
  });
  return calls;
}

async function choosePassword(page: Page) {
  await page.getByRole('textbox', { name: 'Contraseña nueva', exact: true }).fill(NEW_PASSWORD);
  await page.getByLabel('Confirme la contraseña nueva').fill(NEW_PASSWORD);
  await page.getByRole('button', { name: 'Guardar la contraseña' }).click();
}

test('the login links to the recovery, which shows the same message for any email', async ({
  page,
}) => {
  await mockApi(page);
  const calls = await mockPasswordReset(page);
  await page.goto('/login');

  await page.getByRole('link', { name: '¿Olvidó su contraseña?' }).click();
  await expect(page).toHaveURL('/recuperar');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Recuperar la contraseña' }),
  ).toBeVisible();
  await expect(page.getByText(/fase de desarrollo|todavía no está disponible/)).toHaveCount(0);
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByLabel('Correo electrónico').fill('nadie@example.test');
  await page.getByRole('button', { name: 'Enviar el enlace' }).click();

  await expect(page.getByRole('status')).toHaveText(NEUTRAL);
  expect(calls.requests).toEqual([{ email: 'nadie@example.test' }]);
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await page.getByRole('link', { name: 'Volver a iniciar sesión' }).click();
  await expect(page).toHaveURL('/login');
});

test('a valid link sets the new password and goes to the login', async ({ page }) => {
  await mockApi(page);
  const calls = await mockPasswordReset(page);
  await page.goto(`/restablecer?token=${TOKEN}`);

  await expect(
    page.getByRole('heading', { level: 1, name: 'Restablecer la contraseña' }),
  ).toBeVisible();
  // El token sale de la URL visible y del historial, y la página no manda `Referer`.
  await expect(page).toHaveURL('/restablecer');
  expect(page.url()).not.toContain(TOKEN);
  await expect(page.locator('meta[name="referrer"]')).toHaveAttribute('content', 'no-referrer');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await choosePassword(page);

  await expect(page).toHaveURL('/login?reason=password-reset');
  await expect(page.getByRole('status')).toHaveText(
    'Su contraseña quedó cambiada. Inicie sesión con la contraseña nueva.',
  );
  expect(calls.confirms).toEqual([
    { body: { token: TOKEN, new_password: NEW_PASSWORD }, referer: undefined },
  ]);
  await expect(page.locator('meta[name="referrer"]')).toHaveCount(0);
});

test('an invalid link explains it and offers a new one', async ({ page }) => {
  await mockApi(page);
  await mockPasswordReset(page);
  await page.goto('/restablecer?token=vencido');
  await expect(page).toHaveURL('/restablecer');

  await choosePassword(page);

  await expect(page.getByRole('alert')).toContainText(
    'El enlace para restablecer la contraseña no sirve: venció, ya se usó o se pidió otro más nuevo.',
  );
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await page.getByRole('link', { name: 'Pedir un enlace nuevo' }).click();
  await expect(page).toHaveURL('/recuperar');
});
