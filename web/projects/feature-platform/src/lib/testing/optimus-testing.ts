import { fireEvent, screen, waitFor, within } from '@testing-library/angular';

/*
 * Test helpers for the Optimus controls of ui (`ui-select`, `ui-date-picker`, `ui-file-upload`) in jsdom. Only the
 * .spec.ts files use them; they are not part of the public API.
 */

/** jsdom has no matchMedia nor scrollIntoView; the Optimus overlays and lists use them. Call it in `beforeAll`. */
export function stubOptimusDom(): void {
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
}

/** Opens the list of a `ui-select`, clicks the option and waits for the list to close. */
export async function pickOption(combobox: HTMLElement, name: string): Promise<void> {
  fireEvent.click(combobox);
  fireEvent.click(await screen.findByRole('option', { name }));
  await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
}

/**
 * Opens a `ui-menu-button` by its accessible name and clicks one of its menu items. The Optimus menu listens for
 * clicks on the content inside the `menuitem`, so the click goes to its label, as a pointer would.
 */
export async function pickMenuItem(menuButton: string, item: string): Promise<void> {
  fireEvent.click(screen.getByRole('button', { name: menuButton }));
  const menuitem = await screen.findByRole('menuitem', { name: item });
  fireEvent.click(within(menuitem).getByText(item));
}

/** Names of the options of a `ui-select`: opens its list, reads them and closes it again. */
export async function optionNames(combobox: HTMLElement): Promise<string[]> {
  fireEvent.click(combobox);
  const list = await screen.findByRole('listbox');
  const names = within(list)
    .getAllByRole('option')
    .map((option) => option.textContent?.trim() ?? '');
  fireEvent.click(combobox);
  await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
  return names;
}

/** Types a `dd/mm/aaaa` date in the input of a `ui-date-picker`, as a person does. */
export function typeDate(input: HTMLElement, text: string): void {
  fireEvent.keyDown(input, { key: text.slice(-1) });
  fireEvent.input(input, { target: { value: text } });
}
