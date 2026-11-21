import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import { MenuButtonComponent, UiMenuItem } from './menu-button.component';

const ITEMS: UiMenuItem[] = [
  { id: 'xlsx', label: 'Excel (.xlsx)' },
  { id: 'csv', label: 'CSV' },
];

async function renderMenu(inputs: Record<string, unknown> = {}) {
  const selected = vi.fn();
  await render(MenuButtonComponent, {
    inputs: { label: 'Exportar', items: ITEMS, ...inputs },
    on: { selected },
  });
  return { button: screen.getByRole('button', { name: 'Exportar' }), selected };
}

describe('MenuButtonComponent', () => {
  it('is a menu button: aria-haspopup menu, collapsed, with a decorative chevron', async () => {
    const { button } = await renderMenu();

    expect(button.getAttribute('aria-haspopup')).toBe('menu');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.getAttribute('type')).toBe('button');
    const chevron = button.querySelector('svg[data-p-icon="chevron-down"]');
    expect(chevron?.getAttribute('aria-hidden')).toBe('true');
  });

  it('opens a menu with one menuitem per option and marks the button expanded', async () => {
    const { button } = await renderMenu();

    fireEvent.click(button);

    const menu = await screen.findByRole('menu', { name: 'Exportar' });
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.getAttribute('aria-controls')).toBe(menu.id);
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual([
      'Excel (.xlsx)',
      'CSV',
    ]);
  });

  it('emits the id of the chosen option, closes and gives focus back to the button', async () => {
    const { button, selected } = await renderMenu();

    fireEvent.click(button);
    fireEvent.click(await screen.findByText('CSV'));

    expect(selected).toHaveBeenCalledExactlyOnceWith('csv');
    await waitFor(() => expect(button.getAttribute('aria-expanded')).toBe('false'));
    await waitFor(() => expect(document.activeElement).toBe(button));
  });

  it('moves with the arrow keys, picks with Enter and closes with Escape back on the button', async () => {
    const { button, selected } = await renderMenu();

    fireEvent.click(button);
    const menu = await screen.findByRole('menu');
    fireEvent.keyDown(menu, { code: 'ArrowDown', key: 'ArrowDown' });
    fireEvent.keyDown(menu, { code: 'ArrowDown', key: 'ArrowDown' });
    expect(menu.getAttribute('aria-activedescendant')).toContain('csv');
    fireEvent.keyDown(menu, { code: 'Escape', key: 'Escape' });

    await waitFor(() => expect(button.getAttribute('aria-expanded')).toBe('false'));
    expect(document.activeElement).toBe(button);
    expect(selected).not.toHaveBeenCalled();

    fireEvent.click(button);
    const reopened = await screen.findByRole('menu');
    fireEvent.keyDown(reopened, { code: 'ArrowDown', key: 'ArrowDown' });
    fireEvent.keyDown(reopened, { code: 'Enter', key: 'Enter' });

    expect(selected).toHaveBeenCalledExactlyOnceWith('xlsx');
  });

  it('is disabled and busy while loading', async () => {
    const { button } = await renderMenu({ loading: true });

    expect((button as HTMLButtonElement).disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
  });
});
