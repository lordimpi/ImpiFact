import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Optimus } from '@openng/optimus-ui/config';
import { render } from '@testing-library/angular';
import { ButtonComponent } from '../button/button.component';
import { provideUiTheme } from './provide-ui-theme';

function injectedCss(): string {
  return Array.from(document.head.querySelectorAll('style'))
    .map((style) => style.textContent ?? '')
    .join('\n');
}

// El tema se carga con un import() durante el arranque: se espera a que terminen los inicializadores.
async function renderThemedButton() {
  const view = await render(ButtonComponent, {
    inputs: { label: 'Guardar' },
    providers: [provideUiTheme()],
  });
  await TestBed.inject(ApplicationInitStatus).donePromise;
  view.detectChanges();
  return view;
}

describe('provideUiTheme', () => {
  it('maps the Optimus UI variables onto the ImpiFact design tokens', async () => {
    await renderThemedButton();

    const css = injectedCss();
    expect(css).toContain('--p-primary-color:var(--if-color-primary)');
    expect(css).toContain('--p-form-field-border-color:var(--if-color-border-strong)');
    expect(css).toContain('--p-button-danger-background:var(--if-color-danger)');
  });

  it('does not emit an Optimus dark scheme: the tokens decide light or dark', async () => {
    await renderThemedButton();

    expect(injectedCss()).not.toContain('prefers-color-scheme');
  });

  it('holds the application start until the theme is applied', async () => {
    TestBed.configureTestingModule({ providers: [provideUiTheme()] });
    const status = TestBed.inject(ApplicationInitStatus);

    await status.donePromise;

    expect(status.done).toBe(true);
    expect(TestBed.inject(Optimus).theme()).toMatchObject({
      options: { darkModeSelector: 'none', cssLayer: false },
    });
  });
});
