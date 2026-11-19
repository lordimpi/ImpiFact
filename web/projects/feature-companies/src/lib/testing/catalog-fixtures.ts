import { HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';

/*
 * Catálogos de la DIAN para las pruebas del feature: un interceptor que responde `GET /api/v1/catalogs/{nombre}` con
 * unas pocas filas reales (mismos ids que la semilla del backend), así las pruebas no tienen que contestar cada
 * catálogo que pide el formulario de la empresa. Guarda las solicitudes para revisar la búsqueda y el cursor.
 */

type Row = Record<string, unknown> & { id: number; code: string; name: string };

const CATALOGS: Record<string, Row[]> = {
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
    { id: 117, code: 'R-99-PN', name: 'No aplica – Otros' },
  ],
  departments: [
    { id: 2, code: '05', name: 'Antioquia', country_id: 46 },
    { id: 4, code: '11', name: 'Bogotá', country_id: 46 },
  ],
  municipalities: [
    { id: 1, code: '05001', name: 'Medellín', department_id: 2 },
    { id: 149, code: '11001', name: 'Bogotá, D.C.', department_id: 4 },
  ],
  'unit-measures': [
    { id: 70, code: '94', name: 'unidad' },
    { id: 767, code: 'KGM', name: 'kilogramo' },
  ],
  taxes: [
    { id: 1, code: '01', name: 'IVA', description: 'Impuesto sobre la Ventas' },
    { id: 2, code: '02', name: 'IC', description: 'Impuesto al Consumo Departamental' },
    { id: 5, code: '05', name: 'ReteIVA', description: 'Retención sobre el IVA' },
    {
      id: 10,
      code: '22',
      name: 'INC Bolsas',
      description: 'Impuesto Nacional al Consumo de Bolsa',
    },
  ],
};

const PREFIX = '/api/v1/catalogs/';

/** Solicitudes de catálogos que recibió el interceptor, en orden. */
export const catalogRequests: HttpRequest<unknown>[] = [];

const plain = (text: string) =>
  text
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();

export const catalogFixtures: HttpInterceptorFn = (request, next) => {
  if (request.method !== 'GET' || !request.url.startsWith(PREFIX)) {
    return next(request);
  }
  catalogRequests.push(request);
  const name = request.url.slice(PREFIX.length);
  const q = plain(request.params.get('q') ?? '');
  const department = request.params.get('department_id');
  const items = (CATALOGS[name] ?? [])
    .filter((row) => !q || plain(`${row.code} ${row.name}`).includes(q))
    .filter((row) => department === null || String(row['department_id']) === department)
    .map((row) => ({ ...row, is_active: true }));
  return of(
    new HttpResponse({
      status: 200,
      body: { name, version: 'test', next_cursor: null, items },
    }),
  );
};
