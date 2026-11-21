import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormField, disabled, form, required } from '@angular/forms/signals';
import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import { FieldComponent } from '../field/field.component';
import { ComboboxComponent, ComboboxLabels, ComboboxStatus } from './combobox.component';

const LABELS: ComboboxLabels = {
  placeholder: 'Escriba para buscar el municipio…',
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

// jsdom has no matchMedia nor scrollIntoView; the Optimus overlay and list use them.
beforeAll(() => {
  window.matchMedia ??= ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  })) as typeof window.matchMedia;
  Element.prototype.scrollIntoView ??= () => undefined;
});

async function setUp(
  inputs: Partial<{
    value: string;
    status: ComboboxStatus;
    hasMore: boolean;
    locked: boolean;
    options: { id: number; name: string }[];
  }> = {},
) {
  const value = signal(inputs.value ?? '');
  const options = signal(inputs.options ?? OPTIONS);
  const status = signal(inputs.status ?? 'ready');
  const locked = signal(inputs.locked ?? false);
  const termChange = vi.fn();
  const loadMore = vi.fn();
  const retry = vi.fn();
  const touch = vi.fn();
  const view = await render(
    `<ui-field label="Municipio" error="">
       <ui-combobox [(value)]="value" [options]="options()" [status]="status()" [hasMore]="hasMore"
         [labels]="labels" [locked]="locked()" (termChange)="termChange($event)" (loadMore)="loadMore()"
         (retry)="retry()" (touch)="touch()" />
     </ui-field>`,
    {
      imports: [FieldComponent, ComboboxComponent],
      componentProperties: {
        value,
        options,
        status,
        locked,
        hasMore: inputs.hasMore ?? false,
        labels: LABELS,
        termChange,
        loadMore,
        retry,
        touch,
      },
    },
  );
  await view.fixture.whenStable();
  const combobox = screen.getByRole('combobox', { name: 'Municipio' });
  const stable = () => view.fixture.whenStable();
  const openList = async () => {
    fireEvent.click(combobox);
    await stable();
    return screen.findByRole('searchbox');
  };
  return {
    view,
    value,
    options,
    status,
    locked,
    combobox,
    stable,
    openList,
    termChange,
    loadMore,
    retry,
    touch,
  };
}

describe('ComboboxComponent', () => {
  it('is a closed combobox labelled and described by its field', async () => {
    const { combobox } = await setUp();

    expect(combobox.getAttribute('aria-expanded')).toBe('false');
    expect(combobox.getAttribute('aria-labelledby')).toBe(
      screen.getByText('Municipio', { selector: 'label' }).id,
    );
    expect(combobox.textContent?.trim()).toBe(LABELS.placeholder);
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('shows the name of the saved value', async () => {
    const { combobox } = await setUp({ value: '362' });

    expect(combobox.textContent?.trim()).toBe('Popayán');
  });

  it('opens a list labelled by the field with a search box', async () => {
    const { combobox, openList } = await setUp();

    const search = await openList();

    expect(combobox.getAttribute('aria-expanded')).toBe('true');
    expect(search.getAttribute('placeholder')).toBe(LABELS.placeholder);
    const listbox = screen.getByRole('listbox', { name: 'Municipio' });
    expect(
      Array.from(listbox.querySelectorAll('[role="option"]')).map((option) =>
        option.textContent?.trim(),
      ),
    ).toEqual(['Popayán', 'Almaguer', 'Argelia']);
  });

  it('emits the typed term (the server searches) and does not filter the options itself', async () => {
    const { openList, termChange, stable } = await setUp();
    const search = await openList();

    fireEvent.input(search, { target: { value: 'popa' } });
    await stable();

    expect(termChange).toHaveBeenLastCalledWith('popa');
    expect(screen.getAllByRole('option')).toHaveLength(3);
  });

  it('chooses with a click and clears the search when the list closes', async () => {
    const { openList, value, combobox, termChange, touch, stable } = await setUp();
    const search = await openList();
    fireEvent.input(search, { target: { value: 'popa' } });

    fireEvent.click(screen.getByRole('option', { name: 'Popayán' }));
    await stable();

    expect(value()).toBe('362');
    expect(combobox.textContent?.trim()).toBe('Popayán');
    // Closing the list (after its leave animation) clears the search and marks the field touched.
    await waitFor(() => expect(termChange).toHaveBeenLastCalledWith(''));
    expect(touch).toHaveBeenCalled();
  });

  it('chooses with the keyboard from the search box', async () => {
    const { openList, value, stable } = await setUp();
    const search = await openList();

    fireEvent.keyDown(search, { key: 'ArrowDown', code: 'ArrowDown' });
    fireEvent.keyDown(search, { key: 'ArrowDown', code: 'ArrowDown' });
    fireEvent.keyDown(search, { key: 'Enter', code: 'Enter' });
    await stable();

    expect(value()).toBe('363');
  });

  it('keeps the name of the chosen option when a later search does not bring it', async () => {
    const { options, combobox, stable } = await setUp({ value: '362' });

    options.set([{ id: 400, name: 'Timbío' }]);
    await stable();

    expect(combobox.textContent?.trim()).toBe('Popayán');
  });

  it('asks for the next page when the keyboard reaches the last option', async () => {
    const { openList, loadMore, stable } = await setUp({ hasMore: true });
    const search = await openList();

    fireEvent.keyDown(search, { key: 'ArrowDown', code: 'ArrowDown' });
    fireEvent.keyDown(search, { key: 'ArrowDown', code: 'ArrowDown' });
    await stable();
    expect(loadMore).not.toHaveBeenCalled();
    fireEvent.keyDown(search, { key: 'ArrowDown', code: 'ArrowDown' });
    await stable();

    expect(loadMore).toHaveBeenCalled();
  });

  it('asks for the next page when the list is scrolled to its end, with no button', async () => {
    const { openList, loadMore } = await setUp({ hasMore: true });
    await openList();
    const container = screen.getByRole('listbox').parentElement as HTMLElement;
    Object.defineProperties(container, {
      scrollHeight: { value: 600 },
      clientHeight: { value: 200 },
      scrollTop: { value: 390, writable: true },
    });

    fireEvent.scroll(container);

    expect(loadMore).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('is locked (disabled, out of the tab order) and keeps its value', async () => {
    const { combobox, locked, value, stable } = await setUp({ value: '362', locked: true });

    expect(combobox.getAttribute('aria-disabled')).toBe('true');
    expect(combobox.getAttribute('tabindex')).toBe('-1');
    expect(combobox.textContent?.trim()).toBe('Popayán');

    locked.set(false);
    await stable();
    expect(combobox.getAttribute('aria-disabled')).toBe('false');
    expect(value()).toBe('362');
  });

  it.each<[ComboboxStatus, string]>([
    ['idle', LABELS.idle!],
    ['loading', LABELS.loading],
  ])('announces the %s state', async (status, text) => {
    const { combobox } = await setUp({ status, options: [] });

    const message = screen.getByText(text);
    expect(combobox.getAttribute('aria-describedby')).toContain(message.parentElement!.id);
  });

  it('announces an empty search, also inside the open list', async () => {
    const { openList } = await setUp({ options: [] });
    await openList();

    expect(screen.getAllByText(LABELS.empty)).toHaveLength(2);
  });

  it('offers to retry a failed search', async () => {
    const { retry } = await setUp({ status: 'error', options: [] });

    expect(screen.getByText(LABELS.error)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: LABELS.retry }));
    expect(retry).toHaveBeenCalled();
  });
});

@Component({
  imports: [FieldComponent, ComboboxComponent, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-field label="Unidad de medida">
      <ui-combobox [formField]="productForm.unit" [options]="options" [labels]="labels" />
    </ui-field>
  `,
})
class ProductHost {
  readonly options = OPTIONS;
  readonly labels = LABELS;
  readonly model = signal({ unit: '363' });
  readonly blocked = signal(false);
  readonly productForm = form(this.model, (path) => {
    required(path.unit);
    disabled(path.unit, () => this.blocked());
  });
}

describe('ComboboxComponent with Signal Forms', () => {
  it('shows, writes and disables the value of the form', async () => {
    const view = await render(ProductHost);
    await view.fixture.whenStable();
    const host = view.fixture.componentInstance;
    const combobox = screen.getByRole('combobox', { name: 'Unidad de medida' });
    expect(combobox.textContent?.trim()).toBe('Almaguer');

    fireEvent.click(combobox);
    await view.fixture.whenStable();
    fireEvent.click(await screen.findByRole('option', { name: 'Argelia' }));
    await view.fixture.whenStable();
    expect(host.model().unit).toBe('364');
    await waitFor(() => expect(host.productForm.unit().touched()).toBe(true));

    host.blocked.set(true);
    await view.fixture.whenStable();
    expect(combobox.getAttribute('aria-disabled')).toBe('true');
  });
});
