import { fireEvent, render, screen } from '@testing-library/angular';
import { DigitsOnlyDirective } from './digits-only.directive';

async function renderInput(enabled = true) {
  const changes: string[] = [];
  await render(
    `<input aria-label="NIT" [uiDigitsOnly]="enabled" (input)="changes.push($any($event.target).value)" />`,
    { imports: [DigitsOnlyDirective], componentProperties: { enabled, changes } },
  );
  return { input: screen.getByRole('textbox', { name: 'NIT' }) as HTMLInputElement, changes };
}

function beforeInput(input: HTMLInputElement, data: string, inputType = 'insertText') {
  const event = new InputEvent('beforeinput', { data, inputType, bubbles: true, cancelable: true });
  input.dispatchEvent(event);
  return event;
}

describe('DigitsOnlyDirective', () => {
  it('asks for the numeric keyboard', async () => {
    const { input } = await renderInput();

    expect(input.getAttribute('inputmode')).toBe('numeric');
  });

  it('refuses a typed letter before it reaches the field', async () => {
    const { input } = await renderInput();

    const event = beforeInput(input, 'a');

    expect(event.defaultPrevented).toBe(true);
    expect(input.value).toBe('');
  });

  it('lets a typed digit through', async () => {
    const { input } = await renderInput();

    expect(beforeInput(input, '7').defaultPrevented).toBe(false);
  });

  it('keeps only the digits of a pasted text and tells the form', async () => {
    const { input, changes } = await renderInput();
    input.value = '12';
    input.setSelectionRange(2, 2);

    const event = beforeInput(input, '900.123-4', 'insertFromPaste');

    expect(event.defaultPrevented).toBe(true);
    expect(input.value).toBe('129001234');
    expect(changes.at(-1)).toBe('129001234');
  });

  it('cleans a value that arrived another way, such as autofill', async () => {
    const { input, changes } = await renderInput();

    fireEvent.input(input, { target: { value: '300-555 1234' } });

    expect(input.value).toBe('3005551234');
    expect(changes.at(-1)).toBe('3005551234');
  });

  it('leaves letters alone when it is off (for example, a passport)', async () => {
    const { input } = await renderInput(false);

    expect(beforeInput(input, 'a').defaultPrevented).toBe(false);
    fireEvent.input(input, { target: { value: 'AB123' } });
    expect(input.value).toBe('AB123');
    expect(input.getAttribute('inputmode')).toBeNull();
  });
});
