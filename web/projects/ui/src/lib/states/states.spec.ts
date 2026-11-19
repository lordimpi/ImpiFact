import { render, screen } from '@testing-library/angular';
import { EmptyStateComponent } from './empty-state.component';
import { ErrorStateComponent } from './error-state.component';
import { LoadingComponent } from './loading.component';

describe('EmptyStateComponent', () => {
  it('shows the title, the description and the projected action', async () => {
    await render(
      `<ui-empty-state title="Sin documentos" description="Emita su primera factura">
        <button type="button">Nueva factura</button>
      </ui-empty-state>`,
      { imports: [EmptyStateComponent] },
    );

    expect(screen.getByText('Sin documentos')).toBeTruthy();
    expect(screen.getByText('Emita su primera factura')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Nueva factura' })).toBeTruthy();
  });

  it('omits the description when none is given', async () => {
    const { container } = await render(EmptyStateComponent, { inputs: { title: 'Sin datos' } });

    expect(container.querySelectorAll('p')).toHaveLength(1);
  });
});

describe('LoadingComponent', () => {
  it('announces the loading label as a polite status', async () => {
    await render(LoadingComponent, { inputs: { label: 'Cargando documentos' } });

    const status = screen.getByRole('status');
    expect(status.textContent).toContain('Cargando documentos');
    expect(status.getAttribute('aria-busy')).toBe('true');
  });

  it('draws the requested number of skeleton lines', async () => {
    const { container } = await render(LoadingComponent, {
      inputs: { label: 'Cargando', lines: 4 },
    });

    expect(container.querySelectorAll('p-skeleton')).toHaveLength(4);
  });
});

describe('ErrorStateComponent', () => {
  it('announces the error as an alert', async () => {
    await render(ErrorStateComponent, {
      inputs: { title: 'No se pudo cargar', description: 'Revise su conexión' },
    });

    const alert = screen.getByRole('alert');
    expect(alert.textContent).toContain('No se pudo cargar');
    expect(alert.textContent).toContain('Revise su conexión');
  });

  it('emits retry from the retry button', async () => {
    const retry = vi.fn();
    await render(ErrorStateComponent, {
      inputs: { title: 'No se pudo cargar', retryLabel: 'Reintentar' },
      on: { retry },
    });

    screen.getByRole('button', { name: 'Reintentar' }).click();

    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('has no retry button without a retry label', async () => {
    await render(ErrorStateComponent, { inputs: { title: 'No se pudo cargar' } });

    expect(screen.queryByRole('button')).toBeNull();
  });
});
