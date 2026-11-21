import { provideRouter } from '@angular/router';
import { render, screen, within } from '@testing-library/angular';
import { UiBreadcrumbItem } from './breadcrumb';
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

  it('places the actions in their own slot, not in the heading', async () => {
    await render(
      `<ui-page-title>
        Clientes
        <button uiPageTitleActions type="button">Nuevo cliente</button>
      </ui-page-title>`,
      { imports: [PageTitleComponent] },
    );

    const heading = screen.getByRole('heading', { level: 1 });
    expect(heading.textContent?.trim()).toBe('Clientes');
    const actions = screen.getByRole('button', { name: 'Nuevo cliente' });
    expect(actions.closest('.ui-page-title__actions')).toBeTruthy();
  });

  describe('breadcrumb', () => {
    const trail: UiBreadcrumbItem[] = [
      { label: 'Productos y servicios', link: '/productos' },
      { label: 'Nuevo producto' },
    ];

    async function renderTrail(breadcrumb: UiBreadcrumbItem[]) {
      await render(`<ui-page-title [breadcrumb]="breadcrumb">Nuevo producto</ui-page-title>`, {
        imports: [PageTitleComponent],
        componentProperties: { breadcrumb },
        providers: [provideRouter([])],
      });
    }

    it('renders the Optimus breadcrumb in a named navigation above the heading', async () => {
      await renderTrail(trail);

      const nav = screen.getByRole('navigation', { name: 'Ruta de navegación' });
      expect(nav.closest('p-breadcrumb')).not.toBeNull();
      expect(
        nav.compareDocumentPosition(screen.getByRole('heading', { level: 1 })) &
          Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
    });

    it('links every step but the last one, which is the current page', async () => {
      await renderTrail(trail);

      const nav = screen.getByRole('navigation', { name: 'Ruta de navegación' });
      const links = within(nav).getAllByRole('link');
      expect(links.map((link) => link.textContent?.trim())).toEqual(['Productos y servicios']);
      expect(links[0]?.getAttribute('href')).toBe('/productos');
      const current = within(nav).getByText('Nuevo producto');
      expect(current.closest('a')).toBeNull();
      expect(current.getAttribute('aria-current')).toBe('page');
    });

    it('separates the steps with a separator hidden from screen readers', async () => {
      await renderTrail(trail);

      const nav = screen.getByRole('navigation', { name: 'Ruta de navegación' });
      const separators = nav.querySelectorAll('.p-breadcrumb-separator');
      expect(separators).toHaveLength(1);
      expect(separators[0]?.getAttribute('aria-hidden')).toBe('true');
    });

    it('renders no navigation without a breadcrumb', async () => {
      await renderTrail([]);

      expect(screen.queryByRole('navigation')).toBeNull();
    });
  });
});
