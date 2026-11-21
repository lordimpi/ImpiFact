import AxeBuilder from '@axe-core/playwright';
import { Page } from '@playwright/test';

// WCAG = pautas de accesibilidad web; se revisan los niveles A y AA hasta la versión 2.2.
const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/**
 * Violaciones de accesibilidad graves o críticas de la página, según axe. Mide después de que terminen las
 * animaciones: un diálogo recién abierto funde su fondo oscurecido y, a mitad de ese fundido, el texto que queda detrás
 * da un contraste falso.
 */
export async function seriousViolations(page: Page) {
  await settled(page);
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
 * Serious or critical violations of the page in the light and in the dark theme of the panel. Each run sets
 * `data-theme` on <html> (what the top bar toggle does) and the original theme comes back at the end.
 * Uso: `toEqual({ light: [], dark: [] })`.
 */
export async function seriousViolationsInBothThemes(page: Page) {
  const original = await page.evaluate(() => document.documentElement.dataset['theme'] ?? 'light');
  const light = await violationsIn(page, 'light');
  const dark = await violationsIn(page, 'dark');
  await setTheme(page, original);
  return { light, dark };
}

async function setTheme(page: Page, theme: string) {
  await page.evaluate((value) => (document.documentElement.dataset['theme'] = value), theme);
}

async function violationsIn(page: Page, theme: 'light' | 'dark') {
  await setTheme(page, theme);
  return seriousViolations(page);
}

/**
 * Espera a que el navegador pinte y terminen las transiciones y animaciones finitas (cambio de tema, botones de Optimus
 * UI que animan el fondo y el texto, diálogos que funden su fondo): medido a mitad de una, el contraste sale falso.
 */
async function settled(page: Page) {
  await page.evaluate(async () => {
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    // Las animaciones sin fin (esqueletos de carga, indicadores) no se esperan; una cancelada cuenta como terminada.
    const finite = document
      .getAnimations()
      .filter((animation) => animation.effect?.getTiming().iterations !== Infinity);
    await Promise.all(finite.map((animation) => animation.finished.catch(() => undefined)));
  });
}
