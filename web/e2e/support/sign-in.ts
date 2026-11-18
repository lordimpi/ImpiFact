import { Page, expect } from '@playwright/test';
import { COMPANIES, MockApiOptions, USER, mockApi } from './mock-api';

/**
 * Entra con la Api simulada y una sola empresa (queda activa) y llega a `path` por el `returnUrl` del login.
 * Por defecto entra como propietario con los alcances de su rol.
 */
export async function signInAt(page: Page, path: string, options: MockApiOptions = {}) {
  const api = await mockApi(page, {
    companies: [COMPANIES[0]!],
    roles: ['owner'],
    scopes: ['documents:read', 'documents:write', 'config:write'],
    ...options,
  });
  await page.goto(`/login?returnUrl=${encodeURIComponent(path)}`);
  await page.getByLabel('Correo electrónico').fill(USER.email);
  await page.getByLabel('Contraseña').fill(USER.password);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page).toHaveURL(path);
  return api;
}
