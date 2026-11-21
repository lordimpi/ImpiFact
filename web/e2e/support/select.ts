import { Locator, Page, expect } from '@playwright/test';

/**
 * Chooses an option of an Optimus select of ui (`ui-select`, `ui-combobox`): opens its list, types `search` in the
 * search box of the list when given (ui-combobox searches on the server) and clicks the option. The list closes and
 * the control shows the chosen option.
 */
export async function pickOption(
  page: Page,
  combobox: Locator,
  option: string,
  search?: string,
): Promise<void> {
  // A list that is still closing (its leave animation) would leave two search boxes on the page.
  await expect(page.getByRole('listbox')).toHaveCount(0);
  await combobox.click();
  if (search !== undefined) {
    await page.getByRole('searchbox').fill(search);
  }
  await page.getByRole('option', { name: option, exact: true }).click();
  await expect(page.getByRole('listbox')).toBeHidden();
  await expect(combobox).toHaveText(option);
}
