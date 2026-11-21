import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormField, disabled, form, required } from '@angular/forms/signals';
import { fireEvent, render, screen } from '@testing-library/angular';
import { FieldComponent } from './field.component';
import { SelectComponent } from './select.component';

// Catalog options keep numeric ids; the form holds them as text, like the native selects did.
const REGIMES = [
  { id: 1, name: 'Responsable de IVA' },
  { id: 2, name: 'No responsable de IVA' },
];

@Component({
  imports: [FieldComponent, SelectComponent, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-field label="Régimen" hint="Según el RUT" [error]="error()">
      <ui-select
        [options]="regimes"
        placeholder="Seleccione un régimen"
        [formField]="regimeForm.regime"
      />
    </ui-field>
  `,
})
class RegimeHost {
  readonly regimes = REGIMES;
  readonly model = signal({ regime: '' });
  readonly locked = signal(false);
  readonly error = signal<string | null>(null);
  readonly regimeForm = form(this.model, (path) => {
    required(path.regime);
    disabled(path.regime, () => this.locked());
  });
}

async function setUp() {
  const view = await render(RegimeHost);
  await view.fixture.whenStable();
  const host = view.fixture.componentInstance;
  const combobox = screen.getByRole('combobox', { name: 'Régimen' });
  return { view, host, combobox };
}

describe('SelectComponent', () => {
  // jsdom has no matchMedia nor scrollIntoView; the Optimus overlay and list use them.
  beforeAll(() => {
    window.matchMedia ??= ((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    })) as typeof window.matchMedia;
    Element.prototype.scrollIntoView ??= () => undefined;
  });

  it('takes the id, label and description of the enclosing ui-field', async () => {
    const { combobox } = await setUp();

    const label = screen.getByText('Régimen', { selector: 'label' });
    const hint = screen.getByText('Según el RUT');
    expect(label.getAttribute('for')).toBe(combobox.id);
    expect(combobox.getAttribute('aria-labelledby')).toBe(label.id);
    expect(combobox.getAttribute('aria-describedby')).toBe(hint.id);
    expect(combobox.getAttribute('aria-invalid')).toBeNull();
    expect(combobox.textContent?.trim()).toBe('Seleccione un régimen');
    // The arrow is decoration: the combobox itself opens the list.
    const trigger = combobox.closest('ui-select')?.querySelector('.p-select-dropdown');
    expect(trigger?.getAttribute('aria-hidden')).toBe('true');
  });

  it('shows the value written by the signal form', async () => {
    const { view, host, combobox } = await setUp();

    host.model.set({ regime: '2' });
    await view.fixture.whenStable();

    expect(combobox.textContent?.trim()).toBe('No responsable de IVA');
  });

  it('writes the chosen option into the signal form as text', async () => {
    const { view, host, combobox } = await setUp();

    fireEvent.click(combobox);
    await view.fixture.whenStable();
    // The open list is named after the field, not the generic «Opciones».
    expect(await screen.findByRole('listbox', { name: 'Régimen' })).toBeTruthy();
    fireEvent.click(await screen.findByRole('option', { name: 'Responsable de IVA' }));
    await view.fixture.whenStable();

    expect(host.model().regime).toBe('1');
    expect(combobox.textContent?.trim()).toBe('Responsable de IVA');
    expect(host.regimeForm.regime().dirty()).toBe(true);
  });

  it('follows the disabled state of the signal form', async () => {
    const { view, host, combobox } = await setUp();

    host.locked.set(true);
    await view.fixture.whenStable();

    expect(combobox.getAttribute('aria-disabled')).toBe('true');
    expect(combobox.getAttribute('tabindex')).toBe('-1');
  });

  it('marks the field touched on blur and is invalid only while ui-field shows the error', async () => {
    const { view, host, combobox } = await setUp();
    const select = combobox.closest('p-select') as HTMLElement;

    // Required and empty but untouched: the form knows it is invalid, the control is not red yet.
    expect(host.regimeForm.regime().invalid()).toBe(true);
    expect(select.classList).not.toContain('p-invalid');

    fireEvent.focus(combobox);
    fireEvent.blur(combobox);
    expect(host.regimeForm.regime().touched()).toBe(true);

    host.error.set('Elija un régimen');
    await view.fixture.whenStable();
    const error = screen.getByText('Elija un régimen');
    expect(select.classList).toContain('p-invalid');
    expect(combobox.getAttribute('aria-invalid')).toBe('true');
    expect(combobox.getAttribute('aria-describedby')).toBe(
      `${screen.getByText('Según el RUT').id} ${error.id}`,
    );
  });

  it('works without signal forms through value and valueChange', async () => {
    const value = signal('2');
    const view = await render(
      `<ui-select ariaLabel="Estado" [options]="options" [(value)]="value" />`,
      {
        imports: [SelectComponent],
        componentProperties: { value, options: REGIMES },
      },
    );
    await view.fixture.whenStable();
    const combobox = screen.getByRole('combobox', { name: 'Estado' });
    expect(combobox.textContent?.trim()).toBe('No responsable de IVA');

    fireEvent.click(combobox);
    await view.fixture.whenStable();
    fireEvent.click(await screen.findByRole('option', { name: 'Responsable de IVA' }));

    expect(value()).toBe('1');
  });
});
