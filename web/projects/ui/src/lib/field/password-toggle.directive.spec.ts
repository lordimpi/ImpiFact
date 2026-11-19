import { Component } from '@angular/core';
import { fireEvent, render, screen } from '@testing-library/angular';
import { FieldComponent } from './field.component';
import { InputDirective } from './input.directive';
import { PasswordToggleDirective } from './password-toggle.directive';

@Component({
  imports: [FieldComponent, InputDirective, PasswordToggleDirective],
  template: `
    <ui-field label="Contraseña">
      <input uiInput uiPasswordToggle type="password" autocomplete="current-password" />
    </ui-field>
  `,
})
class HostComponent {}

describe('PasswordToggleDirective', () => {
  it('reveals and hides the password with a labelled toggle button', async () => {
    await render(HostComponent);
    const input = screen.getByLabelText('Contraseña') as HTMLInputElement;
    const toggle = await screen.findByRole('button', { name: 'Mostrar la contraseña' });
    expect(input.type).toBe('password');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    expect(toggle.getAttribute('type')).toBe('button');

    fireEvent.click(toggle);
    await screen.findByRole('button', { name: 'Mostrar la contraseña', pressed: true });
    expect(input.type).toBe('text');
    expect(document.activeElement).toBe(input);

    fireEvent.click(toggle);
    await screen.findByRole('button', { name: 'Mostrar la contraseña', pressed: false });
    expect(input.type).toBe('password');
  });
});
