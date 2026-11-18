import { fireEvent, render, screen } from '@testing-library/angular';
import { SearchSelectComponent, SearchSelectLabels } from './search-select.component';

const LABELS: SearchSelectLabels = {
  search: 'Buscar municipio',
  hint: 'Nombre o código DANE',
  loading: 'Buscando…',
  error: 'No se pudo cargar la lista.',
  empty: 'Ningún municipio coincide.',
  more: 'Cargar más municipios',
  retry: 'Reintentar',
};

async function setUp(
  inputs: Partial<{ status: 'loading' | 'ready' | 'error'; hasMore: boolean; empty: boolean }> = {},
) {
  const termChange = vi.fn();
  const loadMore = vi.fn();
  const retry = vi.fn();
  await render(
    `<ui-search-select [labels]="labels" term="" [status]="status" [hasMore]="hasMore" [empty]="empty"
        (termChange)="termChange($event)" (loadMore)="loadMore()" (retry)="retry()">
      <label for="m">Municipio</label><select id="m"><option>Bogotá</option></select>
    </ui-search-select>`,
    {
      imports: [SearchSelectComponent],
      componentProperties: {
        labels: LABELS,
        status: inputs.status ?? 'ready',
        hasMore: inputs.hasMore ?? false,
        empty: inputs.empty ?? false,
        termChange,
        loadMore,
        retry,
      },
    },
  );
  return { termChange, loadMore, retry };
}

describe('SearchSelectComponent', () => {
  it('projects the select and emits what the person types', async () => {
    const { termChange } = await setUp();
    expect(screen.getByLabelText('Municipio')).toBeTruthy();
    fireEvent.input(screen.getByLabelText('Buscar municipio'), { target: { value: 'bogo' } });
    expect(termChange).toHaveBeenCalledWith('bogo');
    expect(screen.queryByRole('button', { name: 'Cargar más municipios' })).toBeNull();
  });

  it('offers more rows while the server has them', async () => {
    const { loadMore } = await setUp({ hasMore: true });
    fireEvent.click(screen.getByRole('button', { name: 'Cargar más municipios' }));
    expect(loadMore).toHaveBeenCalled();
  });

  it('announces loading', async () => {
    await setUp({ status: 'loading' });
    expect(screen.getByText('Buscando…')).toBeTruthy();
  });

  it('shows an error with a retry', async () => {
    const { retry } = await setUp({ status: 'error' });
    expect(screen.getByText('No se pudo cargar la lista.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(retry).toHaveBeenCalled();
  });

  it('says when nothing matches', async () => {
    await setUp({ empty: true });
    expect(screen.getByText('Ningún municipio coincide.')).toBeTruthy();
  });
});
