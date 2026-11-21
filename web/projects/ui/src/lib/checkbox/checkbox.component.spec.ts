import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormField, disabled, form } from '@angular/forms/signals';
import { fireEvent, render, screen } from '@testing-library/angular';
import { CheckboxComponent } from './checkbox.component';

@Component({
  imports: [CheckboxComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p id="accept-hint">Se envía a la DIAN.</p>
    <ui-checkbox
      [checked]="accepted()"
      (checkedChange)="accepted.set($event)"
      [disabled]="locked()"
      ariaDescribedBy="accept-hint"
    >
      Entiendo que los documentos tendrán efecto fiscal real.
    </ui-checkbox>
    <ui-checkbox label="Mostrar cuántos documentos hay" />
  `,
})
class PlainHost {
  readonly accepted = signal(false);
  readonly locked = signal(false);
}

@Component({
  imports: [CheckboxComponent, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-checkbox label="Plan activo" [formField]="planForm.state" />`,
})
class FormHost {
  readonly model = signal({ state: true });
  readonly locked = signal(false);
  readonly planForm = form(this.model, (path) => {
    disabled(path.state, () => this.locked());
  });
}

const ACCEPT = 'Entiendo que los documentos tendrán efecto fiscal real.';

function box(name: string): HTMLInputElement {
  return screen.getByRole('checkbox', { name }) as HTMLInputElement;
}

describe('CheckboxComponent', () => {
  it('renders a real checkbox named by its projected or input label', async () => {
    await render(PlainHost);

    expect(box(ACCEPT).type).toBe('checkbox');
    expect(box('Mostrar cuántos documentos hay').checked).toBe(false);
    expect(box(ACCEPT).getAttribute('aria-describedby')).toBe('accept-hint');
  });

  it('shows the checked value and emits the new one when the person clicks the label', async () => {
    const view = await render(PlainHost);
    const host = view.fixture.componentInstance;

    host.accepted.set(true);
    await view.fixture.whenStable();
    expect(box(ACCEPT).checked).toBe(true);

    fireEvent.click(screen.getByText(ACCEPT));
    await view.fixture.whenStable();
    expect(host.accepted()).toBe(false);
    expect(box(ACCEPT).checked).toBe(false);

    fireEvent.click(box(ACCEPT));
    await view.fixture.whenStable();
    expect(host.accepted()).toBe(true);
  });

  it('cannot be changed while disabled', async () => {
    const view = await render(PlainHost);
    const host = view.fixture.componentInstance;

    host.locked.set(true);
    await view.fixture.whenStable();
    expect(box(ACCEPT).disabled).toBe(true);

    fireEvent.click(screen.getByText(ACCEPT));
    await view.fixture.whenStable();
    expect(host.accepted()).toBe(false);
  });

  it('is a Signal Forms checkbox control: value in and out, and the disabled state', async () => {
    const view = await render(FormHost);
    const host = view.fixture.componentInstance;
    await view.fixture.whenStable();

    expect(box('Plan activo').checked).toBe(true);
    fireEvent.click(screen.getByText('Plan activo'));
    await view.fixture.whenStable();
    expect(host.model().state).toBe(false);

    host.model.set({ state: true });
    await view.fixture.whenStable();
    expect(box('Plan activo').checked).toBe(true);

    host.locked.set(true);
    await view.fixture.whenStable();
    expect(box('Plan activo').disabled).toBe(true);
  });
});
