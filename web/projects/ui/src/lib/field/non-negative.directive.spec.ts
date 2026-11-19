import { Component } from '@angular/core';
import { createEvent, fireEvent, render, screen } from '@testing-library/angular';
import { NonNegativeDirective } from './non-negative.directive';

@Component({
  imports: [NonNegativeDirective],
  template: `<label>Cantidad <input uiNonNegative type="number" step="any" /></label>`,
})
class HostComponent {}

function keydown(input: HTMLElement, key: string): boolean {
  const event = createEvent.keyDown(input, { key });
  fireEvent(input, event);
  return event.defaultPrevented;
}

describe('NonNegativeDirective', () => {
  it('blocks the sign and exponent keys but lets digits, the decimal point and editing keys through', async () => {
    await render(HostComponent);
    const input = screen.getByLabelText('Cantidad');
    for (const key of ['-', '+', 'e', 'E']) {
      expect(keydown(input, key)).toBe(true);
    }
    for (const key of ['1', '0', '.', ',', 'Backspace', 'ArrowLeft', 'Tab']) {
      expect(keydown(input, key)).toBe(false);
    }
    expect(input.getAttribute('min')).toBe('0');
    expect(input.getAttribute('inputmode')).toBe('decimal');
  });

  it('refuses pasted text that is not a non-negative number', async () => {
    await render(HostComponent);
    const input = screen.getByLabelText('Cantidad');
    const paste = (text: string) => {
      const event = createEvent.paste(input, { clipboardData: { getData: () => text } });
      fireEvent(input, event);
      return event.defaultPrevented;
    };
    expect(paste('-5')).toBe(true);
    expect(paste('1e5')).toBe(true);
    expect(paste('12.5')).toBe(false);
  });
});
