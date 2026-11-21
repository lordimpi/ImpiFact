import { Locator, expect } from '@playwright/test';

/**
 * Types an ISO date (`YYYY-MM-DD`) into the input of a ui date picker (`ui-date-picker`) as the person does: key by
 * key as dd/mm/aaaa (the Optimus input ignores `fill`), then closes the calendar with Escape so it covers nothing.
 */
export async function typeDate(input: Locator, iso: string): Promise<void> {
  const [year, month, day] = iso.split('-');
  await input.click();
  await input.press('ControlOrMeta+a');
  await input.press('Backspace');
  await input.pressSequentially(`${day}/${month}/${year}`);
  await input.press('Escape');
  await expect(input).toHaveValue(`${day}/${month}/${year}`);
}
