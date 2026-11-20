import { fireEvent, render, screen, within } from '@testing-library/angular';
import { UiPagination } from '../paginator/pagination';
import { TableCellDirective } from './table-cell.directive';
import { TableComponent } from './table.component';
import { UiFilters, UiSort, UiTableColumn, UiTableLabels } from './table.models';

interface Invoice {
  id: number;
  number: string;
  customer: string;
  status: string;
}

const columns: UiTableColumn[] = [
  { key: 'number', header: 'Número', sortable: true },
  { key: 'customer', header: 'Cliente', filterable: true, filterLabel: 'Filtrar por cliente' },
  { key: 'status', header: 'Estado', sortable: true, filterable: true },
];

// Filas en un orden que no es alfabético: la tabla debe mostrarlas tal cual llegan del servidor.
const rows: Invoice[] = [
  { id: 3, number: 'SETP-3', customer: 'Zapatos SAS', status: 'Rechazado' },
  { id: 1, number: 'SETP-1', customer: 'Alfa Ltda', status: 'Aceptado' },
  { id: 2, number: 'SETP-2', customer: 'Mercado SA', status: 'En cola' },
];

const labels: UiTableLabels = {
  loading: 'Cargando documentos',
  emptyTitle: 'No hay documentos',
  emptyDescription: 'Cambie los filtros',
  errorTitle: 'No se pudieron cargar los documentos',
  retry: 'Reintentar',
  paginator: {
    navigation: 'Paginación',
    previous: 'Anterior',
    next: 'Siguiente',
    page: 'Página {page}',
  },
};

interface Options {
  rows?: Invoice[];
  loading?: boolean;
  error?: boolean;
  sort?: UiSort | null;
  filters?: UiFilters;
  pagination?: UiPagination | null;
}

async function renderTable(options: Options = {}) {
  const sortChange = vi.fn();
  const filterChange = vi.fn();
  const pageChange = vi.fn();
  const retry = vi.fn();
  const view = await render(TableComponent<Invoice>, {
    inputs: { caption: 'Documentos emitidos', columns, labels, rows, ...options },
    on: { sortChange, filterChange, pageChange, retry },
  });
  const table = screen.getByRole('table', { name: 'Documentos emitidos' });
  return { view, table, sortChange, filterChange, pageChange, retry };
}

function bodyRows(table: HTMLElement): string[][] {
  const [, body] = within(table).getAllByRole('rowgroup');
  return within(body!)
    .queryAllByRole('row')
    .map((row) =>
      within(row)
        .queryAllByRole('cell')
        .map((cell) => cell.textContent?.trim() ?? ''),
    );
}

describe('TableComponent', () => {
  it('renders the caption, the headers and the rows in the order received', async () => {
    const { table } = await renderTable();

    expect(within(table).getByRole('columnheader', { name: /Número/ })).toBeTruthy();
    expect(within(table).getByRole('columnheader', { name: /Cliente/ })).toBeTruthy();
    expect(bodyRows(table).map((cells) => cells[0])).toEqual(['SETP-3', 'SETP-1', 'SETP-2']);
  });

  it('emits an ascending sort for an unsorted column without reordering rows', async () => {
    const { table, sortChange } = await renderTable();

    within(table)
      .getByRole('button', { name: /Número/ })
      .click();

    expect(sortChange).toHaveBeenCalledWith({ key: 'number', direction: 'asc' });
    expect(bodyRows(table).map((cells) => cells[0])).toEqual(['SETP-3', 'SETP-1', 'SETP-2']);
  });

  it('shows the current sort and emits the opposite direction', async () => {
    const { table, sortChange } = await renderTable({ sort: { key: 'number', direction: 'asc' } });

    const header = within(table).getByRole('columnheader', { name: /Número/ });
    expect(header.getAttribute('aria-sort')).toBe('ascending');
    expect(
      within(table)
        .getByRole('columnheader', { name: /Estado/ })
        .getAttribute('aria-sort'),
    ).toBe('none');
    within(header).getByRole('button').click();

    expect(sortChange).toHaveBeenCalledWith({ key: 'number', direction: 'desc' });
  });

  it('has no sort button on a column that is not sortable', async () => {
    const { table } = await renderTable();

    const header = within(table).getByRole('columnheader', { name: /Cliente/ });
    expect(within(header).queryByRole('button')).toBeNull();
    expect(header.getAttribute('aria-sort')).toBeNull();
  });

  it('emits all filters when one changes without filtering rows locally', async () => {
    const { table, filterChange } = await renderTable({ filters: { status: 'Aceptado' } });

    const customer = within(table).getByRole('textbox', { name: 'Filtrar por cliente' });
    fireEvent.change(customer, { target: { value: 'Alfa' } });

    expect(filterChange).toHaveBeenCalledWith({ status: 'Aceptado', customer: 'Alfa' });
    expect(bodyRows(table)).toHaveLength(3);
  });

  it('removes a filter that is cleared and labels filters by their header by default', async () => {
    const { table, filterChange } = await renderTable({ filters: { status: 'Aceptado' } });

    const status = within(table).getByRole('textbox', { name: 'Estado' });
    expect((status as HTMLInputElement).value).toBe('Aceptado');
    fireEvent.change(status, { target: { value: '  ' } });

    expect(filterChange).toHaveBeenCalledWith({});
  });

  it('shows skeleton rows and announces loading instead of data', async () => {
    const { table, view } = await renderTable({ loading: true });

    expect(table.getAttribute('aria-busy')).toBe('true');
    expect(screen.getByRole('status').textContent).toContain('Cargando documentos');
    expect(view.container.querySelectorAll('tbody p-skeleton').length).toBe(5 * columns.length);
    expect(within(table).queryByText('SETP-1')).toBeNull();
  });

  it('shows the empty state when there are no rows', async () => {
    const { table } = await renderTable({ rows: [] });

    expect(within(table).getByText('No hay documentos')).toBeTruthy();
    expect(within(table).getByText('Cambie los filtros')).toBeTruthy();
  });

  it('shows the error state with a retry action instead of rows', async () => {
    const { table, retry } = await renderTable({ error: true });

    expect(within(table).getByRole('alert').textContent).toContain(
      'No se pudieron cargar los documentos',
    );
    expect(within(table).queryByText('SETP-1')).toBeNull();
    within(table).getByRole('button', { name: 'Reintentar' }).click();

    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('forwards page changes from its paginator', async () => {
    const { pageChange } = await renderTable({
      pagination: { kind: 'cursor', previousCursor: null, nextCursor: 'c2' },
    });

    screen.getByRole('button', { name: 'Siguiente' }).click();

    expect(pageChange).toHaveBeenCalledWith({ kind: 'cursor', cursor: 'c2', direction: 'next' });
  });

  it('has no paginator without pagination', async () => {
    await renderTable();

    expect(screen.queryByRole('navigation')).toBeNull();
  });

  it('renders a custom cell template for a column', async () => {
    await render(
      `<ui-table caption="Documentos" [columns]="columns" [rows]="rows" [labels]="labels">
        <ng-template uiTableCell="status" let-row>
          <strong>{{ row.status }}!</strong>
        </ng-template>
      </ui-table>`,
      {
        imports: [TableComponent, TableCellDirective],
        componentProperties: { columns, rows, labels },
      },
    );

    expect(screen.getByText('Aceptado!').tagName).toBe('STRONG');
  });

  it('can drop its own frame to sit flush inside a card, and stripe its rows', async () => {
    const { view } = await renderTable();
    const scroll = () => view.container.querySelector('.ui-table__scroll');
    expect(scroll()?.classList).not.toContain('ui-table__scroll--flush');
    expect(view.container.querySelector('table')?.classList).not.toContain('ui-table--striped');

    view.fixture.componentRef.setInput('flush', true);
    view.fixture.componentRef.setInput('striped', true);
    view.detectChanges();

    expect(scroll()?.classList).toContain('ui-table__scroll--flush');
    expect(view.container.querySelector('table')?.classList).toContain('ui-table--striped');
  });
  describe('stacked mode on narrow screens', () => {
    it('is on by default and can be turned off per table', async () => {
      const { view } = await renderTable();
      const host = view.fixture.nativeElement as HTMLElement;
      expect(host.classList).toContain('ui-table--stack');

      view.fixture.componentRef.setInput('stackOnNarrow', false);
      view.detectChanges();

      expect(host.classList).not.toContain('ui-table--stack');
    });

    it('labels every data cell with its column header so a stacked row reads as label and value', async () => {
      const { table } = await renderTable();

      const [, body] = within(table).getAllByRole('rowgroup');
      const firstRow = within(body!).getAllByRole('row')[0]!;
      const labelsOfCells = within(firstRow)
        .getAllByRole('cell')
        .map((cell) => cell.getAttribute('data-label'));
      expect(labelsOfCells).toEqual(['Número', 'Cliente', 'Estado']);
    });

    it('labels the filter fields with their column header too', async () => {
      const { table } = await renderTable();

      const customer = within(table).getByRole('textbox', { name: 'Filtrar por cliente' });
      expect(customer.closest('td')?.getAttribute('data-label')).toBe('Cliente');
    });

    it('does not label the cell of the empty state', async () => {
      const { table } = await renderTable({ rows: [] });

      const cell = within(table).getByText('No hay documentos').closest('td');
      expect(cell?.hasAttribute('data-label')).toBe(false);
    });

    it('keeps explicit table roles, since a stacked table changes the display of its elements', async () => {
      const { view } = await renderTable();
      const root = view.container as HTMLElement;

      expect(root.querySelector('table')?.getAttribute('role')).toBe('table');
      expect(root.querySelector('table')?.getAttribute('aria-labelledby')).toBe(
        root.querySelector('caption')?.id,
      );
      for (const group of Array.from(root.querySelectorAll('thead, tbody'))) {
        expect(group.getAttribute('role')).toBe('rowgroup');
      }
      for (const row of Array.from(root.querySelectorAll('tr'))) {
        expect(row.getAttribute('role')).toBe('row');
      }
      for (const header of Array.from(root.querySelectorAll('th'))) {
        expect(header.getAttribute('role')).toBe('columnheader');
      }
      for (const cell of Array.from(root.querySelectorAll('td'))) {
        expect(cell.getAttribute('role')).toBe('cell');
      }
    });
  });
});
