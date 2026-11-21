import { fireEvent, render, screen } from '@testing-library/angular';
import { HelpComponent } from './help.component';

const TEXT = 'Número de Identificación Tributaria, sin el dígito de verificación.';

async function renderHelp() {
  await render(
    `<input aria-describedby="nit-help" aria-label="NIT" />
     <ui-help [text]="text" label="NIT" tooltipId="nit-help" />`,
    { imports: [HelpComponent], componentProperties: { text: TEXT } },
  );
  return {
    button: screen.getByRole('button', { name: 'Ayuda: NIT' }),
    tooltip: screen.getByRole('tooltip', { hidden: true }),
  };
}

describe('HelpComponent', () => {
  it('shows the Optimus info-circle icon, hidden from screen readers, instead of a letter', async () => {
    const { button } = await renderHelp();

    const icon = button.querySelector('svg[data-p-icon="info-circle"]');
    expect(icon).not.toBeNull();
    expect(icon?.getAttribute('aria-hidden')).toBe('true');
    expect(button.textContent?.trim()).toBe('Ayuda: NIT');
  });

  it('keeps the explanation in the DOM with the given id, so the control describes itself with it', async () => {
    const { button, tooltip } = await renderHelp();

    expect(tooltip.id).toBe('nit-help');
    expect(tooltip.textContent?.trim()).toBe(TEXT);
    expect(screen.getByRole('textbox', { description: TEXT })).toBeTruthy();
    expect(button.getAttribute('aria-describedby')).toBe('nit-help');
    expect(button.getAttribute('aria-expanded')).toBe('false');
  });

  it('opens on focus, hover and tap and closes with Escape', async () => {
    const { button, tooltip } = await renderHelp();

    fireEvent.focus(button);
    expect(tooltip.classList).toContain('ui-help__tooltip--open');
    fireEvent.keyDown(button, { key: 'Escape' });
    expect(tooltip.classList).not.toContain('ui-help__tooltip--open');

    fireEvent.blur(button);
    fireEvent.mouseEnter(button);
    expect(button.getAttribute('aria-expanded')).toBe('true');
    fireEvent.mouseLeave(button);

    fireEvent.click(button);
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });
});
