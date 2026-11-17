import { render, screen } from '@testing-library/angular';
import { ButtonComponent } from './button.component';

describe('ButtonComponent', () => {
  it('renders a native button with the given label and type button by default', async () => {
    await render(ButtonComponent, { inputs: { label: 'Guardar' } });

    const button = screen.getByRole('button', { name: 'Guardar' });
    expect(button.getAttribute('type')).toBe('button');
  });

  it('uses the given type so it can submit a form', async () => {
    await render(ButtonComponent, { inputs: { label: 'Enviar', type: 'submit' } });

    expect(screen.getByRole('button', { name: 'Enviar' }).getAttribute('type')).toBe('submit');
  });

  it('emits pressed when clicked', async () => {
    const pressed = vi.fn();
    await render(ButtonComponent, { inputs: { label: 'Guardar' }, on: { pressed } });

    screen.getByRole('button', { name: 'Guardar' }).click();

    expect(pressed).toHaveBeenCalledTimes(1);
  });

  it('does not emit pressed while disabled', async () => {
    const pressed = vi.fn();
    await render(ButtonComponent, {
      inputs: { label: 'Guardar', disabled: true },
      on: { pressed },
    });

    const button = screen.getByRole('button', { name: 'Guardar' }) as HTMLButtonElement;
    button.click();

    expect(button.disabled).toBe(true);
    expect(pressed).not.toHaveBeenCalled();
  });

  it('is busy and disabled while loading, keeping its accessible name', async () => {
    const pressed = vi.fn();
    await render(ButtonComponent, { inputs: { label: 'Guardar', loading: true }, on: { pressed } });

    const button = screen.getByRole('button', { name: 'Guardar' }) as HTMLButtonElement;
    button.click();

    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.disabled).toBe(true);
    expect(pressed).not.toHaveBeenCalled();
  });

  it('applies the danger variant', async () => {
    await render(ButtonComponent, { inputs: { label: 'Borrar', variant: 'danger' } });

    expect(screen.getByRole('button', { name: 'Borrar' }).classList).toContain('p-button-danger');
  });
});
