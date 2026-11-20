import { provideRouter, RouterLink } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { ButtonLinkDirective } from './button-link.directive';

async function renderLink(attributes = '') {
  await render(`<a uiButtonLink routerLink="/documentos/nuevo" ${attributes}>Emitir factura</a>`, {
    imports: [ButtonLinkDirective, RouterLink],
    providers: [provideRouter([])],
  });
  return screen.getByRole('link', { name: 'Emitir factura' });
}

describe('ButtonLinkDirective', () => {
  it('keeps a real link (role and href) with the look of a primary ui-button', async () => {
    const link = await renderLink();

    expect(link.getAttribute('href')).toBe('/documentos/nuevo');
    expect(link.classList).toContain('p-button');
    expect(link.classList).toContain('ui-button-link');
    expect(link.classList).not.toContain('p-button-secondary');
    expect(link.classList).not.toContain('p-button-text');
  });

  it('maps the secondary and danger variants like ui-button', async () => {
    const link = await renderLink('variant="secondary"');

    expect(link.classList).toContain('p-button-secondary');
  });

  it('maps the ghost variant to a borderless button', async () => {
    const link = await renderLink('variant="ghost"');

    expect(link.classList).toContain('p-button-text');
  });

  it('maps the sizes like ui-button', async () => {
    const small = await renderLink('size="small"');

    expect(small.classList).toContain('p-button-sm');
  });
});
