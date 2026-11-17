import { render } from '@testing-library/angular';
import { ButtonComponent } from '../button/button.component';
import { provideUiTheme } from './provide-ui-theme';

function injectedCss(): string {
  return Array.from(document.head.querySelectorAll('style'))
    .map((style) => style.textContent ?? '')
    .join('\n');
}

describe('provideUiTheme', () => {
  it('maps the Optimus UI variables onto the ImpiFact design tokens', async () => {
    await render(ButtonComponent, { inputs: { label: 'Guardar' }, providers: [provideUiTheme()] });

    const css = injectedCss();
    expect(css).toContain('--p-primary-color:var(--if-color-primary)');
    expect(css).toContain('--p-form-field-border-color:var(--if-color-border-strong)');
    expect(css).toContain('--p-button-danger-background:var(--if-color-danger)');
  });

  it('does not emit an Optimus dark scheme: the tokens decide light or dark', async () => {
    await render(ButtonComponent, { inputs: { label: 'Guardar' }, providers: [provideUiTheme()] });

    expect(injectedCss()).not.toContain('prefers-color-scheme');
  });
});
