import { NAV_GROUPS, activeNavPath } from './nav-items';

const ITEMS = NAV_GROUPS.flatMap((group) => group.items);

describe('activeNavPath', () => {
  it('marks only the home entry on the root path', () => {
    expect(activeNavPath(ITEMS, '/')).toBe('/');
  });

  it('marks the most specific entry of a nested path', () => {
    expect(activeNavPath(ITEMS, '/documentos/nuevo')).toBe('/documentos/nuevo');
    expect(activeNavPath(ITEMS, '/empresa/produccion')).toBe('/empresa/produccion');
    expect(activeNavPath(ITEMS, '/empresa/usuarios')).toBe('/empresa/usuarios');
  });

  it('marks the parent entry of a page without its own entry', () => {
    expect(activeNavPath(ITEMS, '/documentos/doc-1')).toBe('/documentos');
    expect(activeNavPath(ITEMS, '/clientes/nuevo')).toBe('/clientes');
  });

  it('does not take a path that only shares a prefix', () => {
    expect(activeNavPath(ITEMS, '/empresas/seleccionar')).toBeNull();
  });

  it('only marks entries that are shown', () => {
    const visible = ITEMS.filter((item) => item.path !== '/documentos/nuevo');
    expect(activeNavPath(visible, '/documentos/nuevo')).toBe('/documentos');
  });
});
