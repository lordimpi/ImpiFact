import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormField, disabled, form, required } from '@angular/forms/signals';
import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import { FieldComponent } from '../field/field.component';
import { DatePickerComponent, isoDate, parseIsoDate } from './date-picker.component';

// jsdom has no matchMedia nor scrollIntoView; the Optimus overlay uses them.
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

@Component({
  imports: [FieldComponent, DatePickerComponent, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-field label="Vence el" hint="Vacío si no vence" [error]="error()">
      <ui-date-picker [formField]="keyForm.expires_at" minDate="2026-11-10" maxDate="2026-12-31" />
    </ui-field>
  `,
})
class KeyHost {
  readonly model = signal({ expires_at: '2026-11-20' });
  readonly locked = signal(false);
  readonly error = signal<string | null>(null);
  readonly keyForm = form(this.model, (path) => {
    required(path.expires_at);
    disabled(path.expires_at, () => this.locked());
  });
}

async function setUp() {
  const view = await render(KeyHost);
  await view.fixture.whenStable();
  const host = view.fixture.componentInstance;
  const input = screen.getByRole('combobox', { name: 'Vence el' }) as HTMLInputElement;
  return { view, host, input, stable: () => view.fixture.whenStable() };
}

describe('isoDate and parseIsoDate', () => {
  it('turn a local date into YYYY-MM-DD and back, and reject what is not a date', () => {
    expect(isoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(isoDate(null)).toBe('');
    expect(parseIsoDate('2026-01-05')).toEqual(new Date(2026, 0, 5));
    expect(parseIsoDate('')).toBeNull();
    expect(parseIsoDate('2026-02-30')).toBeNull();
    expect(parseIsoDate('05/01/2026')).toBeNull();
  });
});

describe('DatePickerComponent', () => {
  it('is labelled and described by its field and shows the value as dd/mm/yyyy', async () => {
    const { input } = await setUp();

    const label = screen.getByText('Vence el', { selector: 'label' });
    expect(label.getAttribute('for')).toBe(input.id);
    expect(input.getAttribute('aria-describedby')).toBe(screen.getByText('Vacío si no vence').id);
    expect(input.value).toBe('20/11/2026');
    expect(screen.getByRole('button', { name: 'Elegir la fecha' })).toBeTruthy();
  });

  it('opens a Spanish calendar and writes the chosen day as an ISO date', async () => {
    const { host, stable } = await setUp();

    fireEvent.click(screen.getByRole('button', { name: 'Elegir la fecha' }));
    await stable();
    expect(await screen.findByText('noviembre')).toBeTruthy();
    expect(screen.getAllByText('do').length).toBeGreaterThan(0);
    fireEvent.click(screen.getByText('25', { selector: '[data-date="2026-10-25"]' }));
    await stable();

    expect(host.model().expires_at).toBe('2026-11-25');
  });

  it('does not let the person choose a day outside the min and max dates', async () => {
    const { host, stable } = await setUp();

    fireEvent.click(screen.getByRole('button', { name: 'Elegir la fecha' }));
    await stable();
    const day = await screen.findByText('5', { selector: '[data-date="2026-10-5"]' });
    fireEvent.click(day);
    await stable();

    expect(day.classList).toContain('p-disabled');
    expect(host.model().expires_at).toBe('2026-11-20');
  });

  it('follows the disabled state and the touched state of the signal form', async () => {
    const { host, input, stable } = await setUp();

    fireEvent.focus(input);
    fireEvent.blur(input);
    expect(host.keyForm.expires_at().touched()).toBe(true);

    host.locked.set(true);
    await stable();
    expect(input.disabled).toBe(true);
  });

  it('is invalid only while ui-field shows the error', async () => {
    const { host, input, stable } = await setUp();
    host.model.set({ expires_at: '' });
    await stable();
    expect(input.value).toBe('');
    expect(input.getAttribute('aria-invalid')).toBeNull();

    host.error.set('Indique la fecha');
    await stable();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.classList).toContain('p-invalid');
  });

  it('works with value and valueChange outside signal forms', async () => {
    const value = signal('');
    const view = await render(`<ui-date-picker ariaLabel="Desde" [(value)]="value" />`, {
      imports: [DatePickerComponent],
      componentProperties: { value },
    });
    await view.fixture.whenStable();
    const input = screen.getByRole('combobox', { name: 'Desde' }) as HTMLInputElement;

    fireEvent.keyDown(input, { key: '6' });
    fireEvent.input(input, { target: { value: '03/02/2026' } });

    await waitFor(() => expect(value()).toBe('2026-02-03'));
  });
});
