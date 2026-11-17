import { render, screen } from '@testing-library/angular';
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
