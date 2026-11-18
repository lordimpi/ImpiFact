import { render, screen } from '@testing-library/angular';
import { FieldComponent } from './field.component';
import { SelectDirective } from './select.directive';

describe('SelectDirective', () => {
  it('labels, describes and marks a native select inside ui-field', async () => {
    await render(
      `<ui-field label="Régimen" hint="Según el RUT" error="Elija un régimen" [required]="true">
        <select uiSelect><option value="">Sin seleccionar</option></select>
      </ui-field>`,
      { imports: [FieldComponent, SelectDirective] },
    );

    const select = screen.getByRole('combobox', { name: /Régimen/ });
    const hint = screen.getByText('Según el RUT');
    const error = screen.getByText('Elija un régimen');
    expect(select.getAttribute('aria-describedby')).toBe(`${hint.id} ${error.id}`);
    expect(select.getAttribute('aria-invalid')).toBe('true');
    expect(select.getAttribute('aria-required')).toBe('true');
    expect(select.classList).toContain('ui-select');
  });
});
