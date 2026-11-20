import AxeBuilder from '@axe-core/playwright';
import { Page } from '@playwright/test';

// WCAG = pautas de accesibilidad web; se revisan los niveles A y AA hasta la versión 2.2.
const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/** Violaciones de accesibilidad graves o críticas de la página, según axe. */
export async function seriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
  return results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => ({
      id: violation.id,
      impact: violation.impact,
      targets: violation.nodes.map((node) => node.target.join(' ')),
    }));
}

/**
 * Serious or critical violations of the page under a light and under a dark system preference
 * (`prefers-color-scheme`). The panel is light only (`data-theme="light"` on <html>), so both runs check the same
 * light theme: the dark run proves a dark system preference does not leak into the panel and break its contrast.
 * Al terminar quita la emulación del esquema de color. Uso: `toEqual({ light: [], dark: [] })`.
 */
export async function seriousViolationsInBothThemes(page: Page) {
  const light = await violationsIn(page, 'light');
  const dark = await violationsIn(page, 'dark');
  await page.emulateMedia({ colorScheme: null });
  return { light, dark };
}

async function violationsIn(page: Page, colorScheme: 'light' | 'dark') {
  await page.emulateMedia({ colorScheme });
  // Espera a que el navegador pinte con el esquema nuevo y terminen las transiciones de color (los botones de
  // Optimus UI animan el fondo y el texto): medido a mitad de la transición, el contraste sale falso.
  await page.evaluate(async () => {
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    // Las animaciones sin fin (esqueletos de carga, indicadores) no se esperan; una cancelada cuenta como terminada.
    const finite = document
      .getAnimations()
      .filter((animation) => animation.effect?.getTiming().iterations !== Infinity);
    await Promise.all(finite.map((animation) => animation.finished.catch(() => undefined)));
  });
  return seriousViolations(page);
}
