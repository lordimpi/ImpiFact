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
