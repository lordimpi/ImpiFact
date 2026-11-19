import { Page } from '@playwright/test';

/*
 * Catálogos de la DIAN simulados (`GET /api/v1/catalogs/{nombre}`) con algunas filas reales de la semilla del
 * backend. Como el Api, buscan con `q` sin tildes en el código o el nombre, filtran los municipios por
 * `department_id`, paginan por cursor los dos catálogos largos (municipios y unidades de medida) y responden 304 a un `If-None-Match` con la versión vigente.
 */

type Row = Record<string, unknown> & { id: number; code: string; name: string };

export const CATALOG_ROWS: Record<string, Row[]> = {
  'identification-types': [
    { id: 3, code: '13', name: 'Cédula de ciudadanía' },
    { id: 6, code: '31', name: 'NIT' },
    { id: 7, code: '41', name: 'Pasaporte' },
  ],
  'organization-types': [
    { id: 1, code: '1', name: 'Persona Jurídica y asimiladas' },
    { id: 2, code: '2', name: 'Persona Natural y asimiladas' },
  ],
  regimes: [
    { id: 1, code: '48', name: 'Responsable de IVA' },
    { id: 2, code: '49', name: 'No Responsable de IVA' },
  ],
  liabilities: [
    { id: 7, code: 'O-13', name: 'Gran contribuyente' },
    { id: 9, code: 'O-15', name: 'Autorretenedor' },
    { id: 117, code: 'R-99-PN', name: 'No aplica – Otros' },
  ],
  departments: [
    { id: 2, code: '05', name: 'Antioquia', country_id: 46 },
    { id: 4, code: '11', name: 'Bogotá', country_id: 46 },
    { id: 11, code: '19', name: 'Cauca', country_id: 46 },
    { id: 31, code: '76', name: 'Valle del Cauca', country_id: 46 },
  ],
  municipalities: [
    { id: 1, code: '05001', name: 'Medellín', department_id: 2 },
    { id: 2, code: '05002', name: 'Abejorral', department_id: 2 },
    { id: 149, code: '11001', name: 'Bogotá, D.C.', department_id: 4 },
    { id: 362, code: '19001', name: 'Popayán', department_id: 11 },
    { id: 363, code: '19022', name: 'Almaguer', department_id: 11 },
    { id: 364, code: '19050', name: 'Argelia', department_id: 11 },
    { id: 1006, code: '76001', name: 'Cali', department_id: 31 },
  ],
  'unit-measures': [
    { id: 70, code: '94', name: 'unidad' },
    { id: 767, code: 'KGM', name: 'kilogramo' },
    { id: 821, code: 'LTR', name: 'litro' },
  ],
  taxes: [
    { id: 1, code: '01', name: 'IVA', description: 'Impuesto sobre la Ventas' },
    { id: 4, code: '04', name: 'INC', description: 'Impuesto Nacional al Consumo' },
    { id: 5, code: '05', name: 'ReteIVA', description: 'Retención sobre el IVA' },
    {
      id: 10,
      code: '22',
      name: 'INC Bolsas',
      description: 'Impuesto Nacional al Consumo de Bolsa',
    },
  ],
  'credit-note-discrepancy-responses': [
    {
      id: 1,
      code: '1',
      name: 'Devolución parcial de los bienes y/o no aceptación parcial del servicio',
    },
    { id: 2, code: '2', name: 'Anulación de factura electrónica' },
    { id: 3, code: '3', name: 'Rebaja o descuento parcial o total' },
  ],
  'debit-note-discrepancy-responses': [
    { id: 1, code: '1', name: 'Intereses' },
    { id: 2, code: '2', name: 'Gastos por cobrar' },
  ],
};

/** Catálogos que el Api pagina por cursor. */
const PAGED = new Set(['municipalities', 'unit-measures']);
/** Filas por página de los catálogos largos en la simulación (pocas, para probar la página siguiente). */
export const MOCK_CATALOG_PAGE_SIZE = 2;
const VERSION = 'e2e-v1';

const plain = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

export async function mockCatalogs(page: Page): Promise<void> {
  await page.route(/\/api\/v1\/catalogs\/[a-z-]+(\?|$)/, (route) => {
    const url = new URL(route.request().url());
    const name = url.pathname.split('/').pop() ?? '';
    const rows = CATALOG_ROWS[name];
    if (!rows) {
      return route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ status: 404, code: 'catalog.not_found', message: 'No existe.' }),
      });
    }
    if (route.request().headers()['if-none-match'] === `"${VERSION}"`) {
      return route.fulfill({ status: 304 });
    }
    const q = plain(url.searchParams.get('q') ?? '');
    const department = url.searchParams.get('department_id');
    const matching = rows
      .filter((row) => !q || plain(`${row.code} ${row.name}`).includes(q))
      .filter((row) => department === null || String(row['department_id']) === department)
      .map((row) => ({ ...row, is_active: true }));
    let items = matching;
    let next: string | null = null;
    if (PAGED.has(name)) {
      const start = Number(url.searchParams.get('cursor') ?? '0');
      items = matching.slice(start, start + MOCK_CATALOG_PAGE_SIZE);
      next =
        start + MOCK_CATALOG_PAGE_SIZE < matching.length
          ? String(start + MOCK_CATALOG_PAGE_SIZE)
          : null;
    }
    return route.fulfill({
      headers: { ETag: `"${VERSION}"`, 'Cache-Control': 'public, max-age=3600' },
      json: { name, version: VERSION, next_cursor: next, items },
    });
  });
}
