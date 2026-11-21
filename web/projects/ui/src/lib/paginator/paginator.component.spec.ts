import { render, screen, within } from '@testing-library/angular';
import { UiPagination, UiPaginatorLabels } from './pagination';
import { PaginatorComponent } from './paginator.component';

const labels: UiPaginatorLabels = {
  navigation: 'Paginación',
  previous: 'Anterior',
  next: 'Siguiente',
  page: 'Página {page}',
};

async function renderPaginator(pagination: UiPagination) {
  const pageChange = vi.fn();
  const { fixture } = await render(PaginatorComponent, {
    inputs: { pagination, labels },
    on: { pageChange },
  });
  const nav = screen.getByRole('navigation', { name: 'Paginación' });
  const button = (name: string) => within(nav).getByRole('button', { name }) as HTMLButtonElement;
  /** Simulates the parent receiving the next page from the Api. */
  const receive = async (next: UiPagination) => {
    fixture.componentRef.setInput('pagination', next);
    await fixture.whenStable();
  };
  return { pageChange, nav, button, receive };
}

describe('PaginatorComponent with an opaque cursor', () => {
  it('renders the Optimus paginator with only previous and next', async () => {
    const { nav } = await renderPaginator({
      kind: 'cursor',
      previousCursor: null,
      nextCursor: 'eyJpZCI6NDJ9',
    });

    expect(nav.querySelector('p-paginator')).not.toBeNull();
    expect(
      within(nav)
        .getAllByRole('button')
        .map((button) => button.getAttribute('aria-label')),
    ).toEqual(['Anterior', 'Siguiente']);
  });

  it('emits the next cursor', async () => {
    const { pageChange, button } = await renderPaginator({
      kind: 'cursor',
      previousCursor: null,
      nextCursor: 'eyJpZCI6NDJ9',
    });

    button('Siguiente').click();

    expect(pageChange).toHaveBeenCalledWith({
      kind: 'cursor',
      cursor: 'eyJpZCI6NDJ9',
      direction: 'next',
    });
  });

  it('disables previous on the first page', async () => {
    const { button } = await renderPaginator({
      kind: 'cursor',
      previousCursor: null,
      nextCursor: 'b',
    });

    expect(button('Anterior').disabled).toBe(true);
    expect(button('Siguiente').disabled).toBe(false);
  });

  it('emits the previous cursor and disables next without a next cursor', async () => {
    const { pageChange, button } = await renderPaginator({
      kind: 'cursor',
      previousCursor: 'a',
      nextCursor: null,
    });

    button('Anterior').click();

    expect(pageChange).toHaveBeenCalledWith({ kind: 'cursor', cursor: 'a', direction: 'previous' });
    expect(button('Siguiente').disabled).toBe(true);
  });

  it('follows the cursors the Api sends after each move, forward and back', async () => {
    const { pageChange, button, receive } = await renderPaginator({
      kind: 'cursor',
      previousCursor: null,
      nextCursor: 'p2',
    });

    button('Siguiente').click();
    await receive({ kind: 'cursor', previousCursor: 'p1', nextCursor: 'p3' });
    expect(button('Anterior').disabled).toBe(false);
    expect(button('Siguiente').disabled).toBe(false);

    button('Siguiente').click();
    await receive({ kind: 'cursor', previousCursor: 'p2', nextCursor: null });
    expect(button('Siguiente').disabled).toBe(true);

    button('Anterior').click();
    await receive({ kind: 'cursor', previousCursor: 'p1', nextCursor: 'p3' });
    expect(button('Anterior').disabled).toBe(false);
    expect(button('Siguiente').disabled).toBe(false);

    button('Anterior').click();
    expect(pageChange).toHaveBeenLastCalledWith({
      kind: 'cursor',
      cursor: 'p1',
      direction: 'previous',
    });
    await receive({ kind: 'cursor', previousCursor: null, nextCursor: 'p2' });
    expect(button('Anterior').disabled).toBe(true);
    expect(pageChange).toHaveBeenCalledTimes(4);
  });
});

describe('PaginatorComponent with page numbers', () => {
  it('marks the current page and emits the chosen page', async () => {
    const { pageChange, button } = await renderPaginator({
      kind: 'pages',
      page: 2,
      pageSize: 10,
      total: 35,
    });

    expect(button('Página 2').getAttribute('aria-current')).toBe('page');
    button('Página 4').click();

    expect(pageChange).toHaveBeenCalledWith({ kind: 'pages', page: 4 });
  });

  it('moves one page with previous and next and disables next on the last page', async () => {
    const { pageChange, button } = await renderPaginator({
      kind: 'pages',
      page: 4,
      pageSize: 10,
      total: 35,
    });

    button('Anterior').click();

    expect(pageChange).toHaveBeenCalledWith({ kind: 'pages', page: 3 });
    expect(button('Siguiente').disabled).toBe(true);
  });

  it('jumps to the first and the last page', async () => {
    const { pageChange, button } = await renderPaginator({
      kind: 'pages',
      page: 10,
      pageSize: 10,
      total: 200,
    });

    button('Primera página').click();
    expect(pageChange).toHaveBeenLastCalledWith({ kind: 'pages', page: 1 });

    button('Última página').click();
    expect(pageChange).toHaveBeenLastCalledWith({ kind: 'pages', page: 20 });
  });

  it('shows a window of page links around the current page', async () => {
    const { nav } = await renderPaginator({ kind: 'pages', page: 10, pageSize: 10, total: 200 });

    const pages = within(nav)
      .getAllByRole('button', { name: /^Página/ })
      .map((button) => button.textContent?.trim());

    expect(pages).toEqual(['8', '9', '10', '11', '12']);
  });

  it('disables every move when there are no records', async () => {
    const { button } = await renderPaginator({ kind: 'pages', page: 1, pageSize: 10, total: 0 });

    expect(button('Anterior').disabled).toBe(true);
    expect(button('Siguiente').disabled).toBe(true);
  });
});
