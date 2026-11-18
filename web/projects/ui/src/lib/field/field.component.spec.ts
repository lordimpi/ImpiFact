import { fireEvent, render, screen } from '@testing-library/angular';
import { UI_TEXTS } from '../i18n/ui-texts';
import { FieldComponent } from './field.component';
import { InputDirective } from './input.directive';

async function renderField(attrs: string) {
  await render(`<ui-field ${attrs}><input uiInput /></ui-field>`, {
    imports: [FieldComponent, InputDirective],
  });
  return screen.getByRole('textbox');
}

describe('FieldComponent', () => {
  it('labels the projected control', async () => {
    await renderField('label="NIT"');

    expect(screen.getByRole('textbox', { name: 'NIT' })).toBeTruthy();
  });

  it('describes the control with the hint', async () => {
    const input = await renderField('label="NIT" hint="Sin dígito de verificación"');

    const describedBy = input.getAttribute('aria-describedby') ?? '';
    const hint = screen.getByText('Sin dígito de verificación');
    expect(describedBy.split(' ')).toContain(hint.id);
    expect(input.getAttribute('aria-invalid')).toBeNull();
  });

  it('marks the control invalid and describes it with the error', async () => {
    const input = await renderField(
      'label="NIT" hint="Solo números" error="El NIT es obligatorio"',
    );

    const error = screen.getByText('El NIT es obligatorio');
    const hint = screen.getByText('Solo números');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe(`${hint.id} ${error.id}`);
  });

  it('marks the control required without exposing the asterisk to assistive technology', async () => {
    const input = await renderField('label="Razón social" [required]="true"');

    expect(input.getAttribute('aria-required')).toBe('true');
    expect(screen.getByRole('textbox', { name: 'Razón social' })).toBe(input);
  });

  describe('help', () => {
    const HELP = 'Número de Identificación Tributaria, sin el dígito de verificación.';

    async function renderHelp(providers: unknown[] = []) {
      await render(`<ui-field label="NIT" [help]="help"><input uiInput /></ui-field>`, {
        imports: [FieldComponent, InputDirective],
        componentProperties: { help: HELP },
        providers: providers as never[],
      });
      return {
        input: screen.getByRole('textbox', { name: 'NIT' }),
        button: screen.getByRole('button'),
        tooltip: screen.getByRole('tooltip', { hidden: true }),
      };
    }

    it('describes the control with the help text, also while the tooltip is closed', async () => {
      const { input, button, tooltip } = await renderHelp();

      expect(tooltip.textContent?.trim()).toBe(HELP);
      expect((input.getAttribute('aria-describedby') ?? '').split(' ')).toContain(tooltip.id);
      expect(button.getAttribute('aria-describedby')).toBe(tooltip.id);
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(button.getAttribute('type')).toBe('button');
    });

    it('names the info button from the translated texts', async () => {
      const { button } = await renderHelp([
        { provide: UI_TEXTS, useValue: { fieldHelp: (label: string) => `Ayuda sobre ${label}` } },
      ]);

      expect(button.getAttribute('aria-label')).toBe('Ayuda sobre NIT');
    });

    it('opens on keyboard focus and on hover, and Escape closes it', async () => {
      const { button, tooltip } = await renderHelp();

      fireEvent.focus(button);
      expect(button.getAttribute('aria-expanded')).toBe('true');
      expect(tooltip.classList).toContain('ui-field__tooltip--open');

      fireEvent.keyDown(button, { key: 'Escape' });
      expect(button.getAttribute('aria-expanded')).toBe('false');

      fireEvent.blur(button);
      fireEvent.mouseEnter(button);
      expect(button.getAttribute('aria-expanded')).toBe('true');
      fireEvent.mouseLeave(button);
      expect(button.getAttribute('aria-expanded')).toBe('false');
    });

    it('toggles on tap for touch screens', async () => {
      const { button } = await renderHelp();

      fireEvent.click(button);
      expect(button.getAttribute('aria-expanded')).toBe('true');
      fireEvent.click(button);
      expect(button.getAttribute('aria-expanded')).toBe('false');
    });

    it('shows no info button without help', async () => {
      await renderField('label="NIT"');

      expect(screen.queryByRole('button')).toBeNull();
    });
  });

  it('gives each field its own control id', async () => {
    await render(
      `<ui-field label="Uno"><input uiInput /></ui-field><ui-field label="Dos"><input uiInput /></ui-field>`,
      { imports: [FieldComponent, InputDirective] },
    );

    const one = screen.getByRole('textbox', { name: 'Uno' });
    const two = screen.getByRole('textbox', { name: 'Dos' });
    expect(one.id).not.toBe(two.id);
  });
});
