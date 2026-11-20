import { render, screen } from '@testing-library/angular';
import { PageTitleComponent } from './page-title.component';

describe('PageTitleComponent', () => {
  it('renders the projected text as the level 1 heading', async () => {
    await render('<ui-page-title>Documentos</ui-page-title>', { imports: [PageTitleComponent] });

    expect(screen.getByRole('heading', { level: 1, name: 'Documentos' })).toBeTruthy();
  });

  it('shows the subtitle outside the heading', async () => {
    await render('<ui-page-title subtitle="Facturas y notas emitidas">Documentos</ui-page-title>', {
      imports: [PageTitleComponent],
    });

    expect(screen.getByRole('heading', { level: 1, name: 'Documentos' })).toBeTruthy();
    expect(screen.getByText('Facturas y notas emitidas').tagName).toBe('P');
  });

  it('places the actions and the breadcrumb in their own slots, not in the heading', async () => {
    await render(
      `<ui-page-title>
        <nav uiPageTitleBreadcrumb aria-label="Ruta">Inicio</nav>
        Clientes
        <button uiPageTitleActions type="button">Nuevo cliente</button>
      </ui-page-title>`,
      { imports: [PageTitleComponent] },
    );

    const heading = screen.getByRole('heading', { level: 1 });
    expect(heading.textContent?.trim()).toBe('Clientes');
    const actions = screen.getByRole('button', { name: 'Nuevo cliente' });
    expect(actions.closest('.ui-page-title__actions')).toBeTruthy();
    expect(
      screen.getByRole('navigation', { name: 'Ruta' }).closest('.ui-page-title__breadcrumb'),
    ).toBeTruthy();
  });
});
