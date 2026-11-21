import { Locator, Page, expect } from '@playwright/test';

/**
 * Opens a `ui-menu-button` of ui by its accessible name (for example «Acciones de Beta Ltda») and returns its menu,
 * already visible. The menu is appended to <body>, outside the table row.
 */
export async function openMenu(page: Page, button: string): Promise<Locator> {
  await page.getByRole('button', { name: button, exact: true }).click();
  const menu = page.getByRole('menu', { name: button, exact: true });
  await expect(menu).toBeVisible();
  return menu;
}

/** Opens a `ui-menu-button` by its accessible name and chooses one of its items; the menu closes. */
export async function pickMenuItem(page: Page, button: string, item: string): Promise<void> {
  const menu = await openMenu(page, button);
  await menu.getByRole('menuitem', { name: item, exact: true }).click();
  await expect(menu).toBeHidden();
}
