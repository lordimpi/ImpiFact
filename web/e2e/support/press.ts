import { Locator, Page } from '@playwright/test';

/**
 * Presses `target` with the mouse the way a person does: the button goes down, stays a moment and comes up.
 * Playwright `click()` sends both at once, so it misses a layout shift between the press and the release (for
 * example a validation message that appears when the press blurs a field and moves the button away).
 */
export async function pressLikeAPerson(page: Page, target: Locator): Promise<void> {
  // Like a person, first bring it into view (scrolling does not move the focus).
  await target.scrollIntoViewIfNeeded();
  const box = await target.boundingBox();
  if (!box) {
    throw new Error('The target is not visible.');
  }
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.waitForTimeout(150);
  await page.mouse.up();
}
