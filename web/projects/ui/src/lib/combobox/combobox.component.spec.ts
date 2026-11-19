import { signal } from '@angular/core';
import { fireEvent, render, screen } from '@testing-library/angular';
import { FieldComponent } from '../field/field.component';
import { ComboboxComponent, ComboboxLabels, ComboboxStatus } from './combobox.component';

const LABELS: ComboboxLabels = {
  idle: 'Seleccione primero el departamento.',
  loading: 'Buscando…',
  error: 'No se pudo cargar la lista.',
  empty: 'Ningún municipio coincide.',
  retry: 'Reintentar',
};

const OPTIONS = [
  { id: 362, name: 'Popayán' },
  { id: 363, name: 'Almaguer' },
  { id: 364, name: 'Argelia' },
];

async function setUp(
  inputs: Partial<{
    value: string;
    status: ComboboxStatus;
    hasMore: boolean;
    options: typeof OPTIONS;
  }> = {},
) {
  const value = signal(inputs.value ?? '');
  const termChange = vi.fn();
  const loadMore = vi.fn();
  const retry = vi.fn();
  const touch = vi.fn();
  const view = await render(
    `<ui-field label="Municipio" error="">
       <ui-combobox [(value)]="value" [options]="options" [status]="status" [hasMore]="hasMore" [labels]="labels"
         (termChange)="termChange($event)" (loadMore)="loadMore()" (retry)="retry()" (touch)="touch()" />
     </ui-field>`,
    {
      imports: [FieldComponent, ComboboxComponent],
      componentProperties: {
        value,
        options: inputs.options ?? OPTIONS,
        status: inputs.status ?? 'ready',
        hasMore: inputs.hasMore ?? false,
        labels: LABELS,
        termChange,
        loadMore,
        retry,
        touch,
      },
    },
  );
  const input = screen.getByRole('combobox', { name: 'Municipio' }) as HTMLInputElement;
  return { view, value, input, termChange, loadMore, retry, touch };
}

describe('ComboboxComponent', () => {
  it('follows the ARIA combobox pattern and starts closed', async () => {
    const { input } = await setUp();

    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    expect(input.getAttribute('autocomplete')).toBe('off');
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('shows the name of the saved value', async () => {
    const { input } = await setUp({ value: '362' });

    expect(input.value).toBe('Popayán');
  });

  it('opens the list on ArrowDown and moves the active option with the keyboard', async () => {
    const { input } = await setUp();

    fireEvent.keyDown(input, { key: 'ArrowDown' });
    const listbox = screen.getByRole('listbox', { name: 'Municipio' });
    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(input.getAttribute('aria-controls')).toBe(listbox.id);
    const options = screen.getAllByRole('option');
    expect(options.map((option) => option.textContent?.trim())).toEqual([
      'Popayán',
      'Almaguer',
      'Argelia',
    ]);
    expect(input.getAttribute('aria-activedescendant')).toBe(options[0]!.id);

    fireEvent.keyDown(input, { key: 'ArrowDown' });
    expect(input.getAttribute('aria-activedescendant')).toBe(options[1]!.id);
    fireEvent.keyDown(input, { key: 'ArrowUp' });
    expect(input.getAttribute('aria-activedescendant')).toBe(options[0]!.id);
  });

  it('chooses the active option with Enter without submitting the form', async () => {
    const { input, value, view } = await setUp();

    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    input.dispatchEvent(enter);
    view.detectChanges();

    expect(enter.defaultPrevented).toBe(true);
    expect(value()).toBe('363');
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('filters by typing (the server searches) and chooses with a click', async () => {
    const { input, value, termChange, view } = await setUp();

    fireEvent.input(input, { target: { value: 'popa' } });
    expect(termChange).toHaveBeenLastCalledWith('popa');
    expect(input.getAttribute('aria-expanded')).toBe('true');

    fireEvent.click(screen.getByRole('option', { name: 'Popayán' }));
    await view.fixture.whenStable();

    expect(value()).toBe('362');
    expect(input.value).toBe('Popayán');
    expect(termChange).toHaveBeenLastCalledWith('');

    fireEvent.keyDown(input, { key: 'ArrowDown' });
    const chosen = screen.getByRole('option', { name: 'Popayán' });
    expect(chosen.getAttribute('aria-selected')).toBe('true');
    expect(input.getAttribute('aria-activedescendant')).toBe(chosen.id);
  });

  it('closes with Escape and puts back the saved name', async () => {
    const { input } = await setUp({ value: '362' });

    fireEvent.input(input, { target: { value: 'xx' } });
    fireEvent.keyDown(input, { key: 'Escape' });
    expect(input.getAttribute('aria-expanded')).toBe('false');
    fireEvent.keyDown(input, { key: 'Escape' });

    expect(input.value).toBe('Popayán');
  });

  it('clears the value when the text is erased and marks the field touched on blur', async () => {
    const { input, value, touch } = await setUp({ value: '362' });

    fireEvent.input(input, { target: { value: '' } });
    fireEvent.blur(input);

    expect(value()).toBe('');
    expect(touch).toHaveBeenCalled();
  });

  it('asks for the next page when the keyboard reaches the last option', async () => {
    const { input, loadMore } = await setUp({ hasMore: true });

    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    expect(loadMore).not.toHaveBeenCalled();
    fireEvent.keyDown(input, { key: 'ArrowDown' });

    expect(loadMore).toHaveBeenCalledTimes(1);
  });

  it('asks for the next page when the list is scrolled to the end, with no button', async () => {
    const { input, loadMore } = await setUp({ hasMore: true });
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    const listbox = screen.getByRole('listbox');
    Object.defineProperties(listbox, {
      scrollHeight: { value: 600 },
      clientHeight: { value: 200 },
      scrollTop: { value: 390, writable: true },
    });

    fireEvent.scroll(listbox);

    expect(loadMore).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button')).toBeNull();
  });

  it.each<[ComboboxStatus, string]>([
    ['idle', LABELS.idle!],
    ['loading', LABELS.loading],
  ])('announces the %s state', async (status, text) => {
    await setUp({ status, options: [] });

    expect(screen.getByText(text)).toBeTruthy();
  });

  it('announces an empty search', async () => {
    const { input } = await setUp({ options: [] });
    fireEvent.input(input, { target: { value: 'zzz' } });

    expect(screen.getByText(LABELS.empty)).toBeTruthy();
  });

  it('offers to retry a failed search', async () => {
    const { retry } = await setUp({ status: 'error', options: [] });

    expect(screen.getByText(LABELS.error)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: LABELS.retry }));
    expect(retry).toHaveBeenCalled();
  });
});
