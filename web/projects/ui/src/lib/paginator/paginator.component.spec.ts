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
  await render(PaginatorComponent, { inputs: { pagination, labels }, on: { pageChange } });
  return { pageChange, nav: screen.getByRole('navigation', { name: 'Paginación' }) };
}

describe('PaginatorComponent with an opaque cursor', () => {
  it('emits the next cursor and shows no page numbers', async () => {
    const { pageChange, nav } = await renderPaginator({
      kind: 'cursor',
      previousCursor: null,
      nextCursor: 'eyJpZCI6NDJ9',
    });

    within(nav).getByRole('button', { name: 'Siguiente' }).click();

    expect(pageChange).toHaveBeenCalledWith({
      kind: 'cursor',
      cursor: 'eyJpZCI6NDJ9',
      direction: 'next',
    });
    expect(within(nav).getAllByRole('button')).toHaveLength(2);
  });

  it('disables previous on the first page and emits the previous cursor otherwise', async () => {
    const first = await renderPaginator({ kind: 'cursor', previousCursor: null, nextCursor: 'b' });
    expect(
      (within(first.nav).getByRole('button', { name: 'Anterior' }) as HTMLButtonElement).disabled,
    ).toBe(true);
  });

  it('emits the previous cursor', async () => {
    const { pageChange, nav } = await renderPaginator({
      kind: 'cursor',
      previousCursor: 'a',
      nextCursor: null,
    });

    within(nav).getByRole('button', { name: 'Anterior' }).click();

    expect(pageChange).toHaveBeenCalledWith({ kind: 'cursor', cursor: 'a', direction: 'previous' });
    expect(
      (within(nav).getByRole('button', { name: 'Siguiente' }) as HTMLButtonElement).disabled,
    ).toBe(true);
  });
});

describe('PaginatorComponent with page numbers', () => {
  it('marks the current page and emits the chosen page', async () => {
    const { pageChange, nav } = await renderPaginator({
      kind: 'pages',
      page: 2,
      pageSize: 10,
      total: 35,
    });

    expect(within(nav).getByRole('button', { name: 'Página 2' }).getAttribute('aria-current')).toBe(
      'page',
    );
    within(nav).getByRole('button', { name: 'Página 4' }).click();

    expect(pageChange).toHaveBeenCalledWith({ kind: 'pages', page: 4 });
  });

  it('moves one page with previous and next and disables next on the last page', async () => {
    const { pageChange, nav } = await renderPaginator({
      kind: 'pages',
      page: 4,
      pageSize: 10,
      total: 35,
    });

    within(nav).getByRole('button', { name: 'Anterior' }).click();

    expect(pageChange).toHaveBeenCalledWith({ kind: 'pages', page: 3 });
    expect(
      (within(nav).getByRole('button', { name: 'Siguiente' }) as HTMLButtonElement).disabled,
    ).toBe(true);
  });

  it('shows a window around the current page plus the first and last pages', async () => {
    const { nav } = await renderPaginator({ kind: 'pages', page: 10, pageSize: 10, total: 200 });

    const pages = within(nav)
      .getAllByRole('button', { name: /^Página/ })
      .map((button) => button.textContent?.trim());

    expect(pages).toEqual(['1', '9', '10', '11', '20']);
  });

  it('shows a single disabled page when there are no records', async () => {
    const { nav } = await renderPaginator({ kind: 'pages', page: 1, pageSize: 10, total: 0 });

    expect(within(nav).getAllByRole('button', { name: /^Página/ })).toHaveLength(1);
    expect(
      (within(nav).getByRole('button', { name: 'Siguiente' }) as HTMLButtonElement).disabled,
    ).toBe(true);
  });
});
