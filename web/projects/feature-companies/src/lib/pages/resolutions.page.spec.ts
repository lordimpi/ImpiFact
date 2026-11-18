import { fireEvent, screen, within } from '@testing-library/angular';
import {
  COMPANY_PATH,
  EMPTY_READINESS,
  expectRequest,
  problem,
  renderCompaniesRoute,
  respond,
} from '../testing/companies-testing';

const URL = `${COMPANY_PATH}/resolution`;
const LIST_URL = `${COMPANY_PATH}/resolutions`;
const RANGES_URL = `${COMPANY_PATH}/numbering-ranges`;
const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });

async function open(resolutions: object[] = []) {
  const rendered = await renderCompaniesRoute('/empresas/company-1/resoluciones');
  await screen.findByRole('heading', { level: 2, name: 'Resoluciones de numeración' });
  await respond(rendered.http, LIST_URL, { resolutions });
  return rendered;
}

const SETP = {
  type_document_id: 1,
  prefix: 'SETP',
  resolution: '18760000001',
  resolution_date: '2019-01-19',
  technical_key: 'fc8eac422eba16e22ffd8c6f94b3f40a6e38162c',
  from: 990000000,
  to: 995000000,
  date_from: '2019-01-19',
  date_to: '2030-01-19',
};

/** La resolución SETP como la lista el Api, con el próximo número y lo que queda. */
const SETP_STATUS = {
  id: 'res-1',
  kind: 'invoice',
  type_document_id: 1,
  type_environment_id: 2,
  prefix: 'SETP',
  resolution: '18760000001',
  resolution_date: '2019-01-19',
  from: 990000000,
  to: 995000000,
  date_from: '2019-01-19',
  date_to: '2030-01-19',
  is_active: true,
  has_technical_key: true,
  next_number: 990000012,
  remaining: 4999989,
  days_left: 1158,
};

const rowCells = (table: HTMLElement) =>
  within(table)
    .getAllByRole('row')
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent?.trim()),
    );

describe('ResolutionsPage', () => {
  it('lists the saved resolutions with the next number, what remains and the days left', async () => {
    await open([
      SETP_STATUS,
      {
        ...SETP_STATUS,
        id: 'res-2',
        kind: 'credit_note',
        type_document_id: 4,
        prefix: 'NC',
        is_active: false,
        days_left: null,
      },
    ]);

    const table = await screen.findByRole('table', { name: 'Resoluciones guardadas' });
    expect(rowCells(table)).toEqual([
      [
        'Factura de venta',
        'SETP',
        '18760000001',
        '990000000 – 995000000',
        '2019-01-19 – 2030-01-19',
        '990000012',
        '4999989',
        '1158',
        'Activa',
      ],
      [
        'Nota crédito',
        'NC',
        '18760000001',
        '990000000 – 995000000',
        '2019-01-19 – 2030-01-19',
        '990000012',
        '4999989',
        'Sin vencimiento',
        'Inactiva',
      ],
    ]);
    expect(screen.queryByText(/no permite listar/)).toBeNull();
  });

  it('says when the company has no resolutions yet', async () => {
    await open();

    expect(await screen.findByText('La empresa aún no tiene resoluciones guardadas.')).toBeTruthy();
  });

  it('explains when the resolutions could not be read and retries', async () => {
    const { http } = await renderCompaniesRoute('/empresas/company-1/resoluciones');
    const error = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, LIST_URL)).flush(error.body, error.options);

    expect(await screen.findByText('No se pudieron consultar las resoluciones.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await respond(http, LIST_URL, { resolutions: [SETP_STATUS] });
    expect(await screen.findByRole('table', { name: 'Resoluciones guardadas' })).toBeTruthy();
  });

  it('fills the habilitation example, saves it and reads the list again', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Usar el ejemplo de habilitación (SETP)' }));
    expect(field(/^Prefijo/).value).toBe('SETP');
    fireEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }));

    const request = await expectRequest(http, URL);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(SETP);
    request.flush({ success: true, resolution: { id: 'res-1', ...SETP } });
    await respond(http, LIST_URL, { resolutions: [SETP_STATUS] });
    await respond(http, `${COMPANY_PATH}/setup-status`, EMPTY_READINESS);

    const list = await screen.findByRole('table', { name: 'Resoluciones guardadas' });
    expect(within(list).getByText('SETP')).toBeTruthy();
    expect(within(list).getByText('990000012')).toBeTruthy();
  });

  it('checks the form before calling the API', async () => {
    const { http } = await open();
    type(/^Desde el número/, '100');
    type(/^Hasta el número/, '10');

    fireEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }));

    expect(
      await screen.findByText('El número final debe ser mayor o igual al inicial.'),
    ).toBeTruthy();
    expect(screen.getAllByText('Este campo es obligatorio.').length).toBeGreaterThan(0);
    http.expectNone(URL);
  });

  it('looks up the ranges in the DIAN and uses one in the form', async () => {
    const { http } = await open();
    type(/^Identificador del software \(opcional\)/, 'soft-123');

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    const request = await expectRequest(http, (req) => req.url === RANGES_URL);
    expect(request.request.params.get('software_id')).toBe('soft-123');
    request.flush({
      success: true,
      numbering_ranges: [
        {
          resolution_number: '18764000000001',
          resolution_date: '2026-01-10',
          prefix: 'FE',
          from: 1,
          to: 5000,
          valid_date_from: '2026-01-10',
          valid_date_to: '2028-01-10',
          technical_key: 'abc123',
        },
      ],
    });

    const table = await screen.findByRole('table', { name: 'Rangos autorizados en la DIAN' });
    expect(within(table).getByText('18764000000001')).toBeTruthy();
    fireEvent.click(within(table).getByRole('button', { name: 'Usar este rango' }));

    expect(field(/^Prefijo/).value).toBe('FE');
    expect(field(/^Número de resolución/).value).toBe('18764000000001');
    expect(field(/^Clave técnica/).value).toBe('abc123');
    expect(field(/^Hasta el número/).value).toBe('5000');
  });

  it('explains the DIAN answer when it has no ranges', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    (await expectRequest(http, (req) => req.url === RANGES_URL)).flush({
      success: false,
      numbering_ranges: [],
      operation_code: '301',
      operation_description: 'No se encontraron rangos de numeración.',
    });

    expect(
      await screen.findByText('Respuesta de la DIAN: No se encontraron rangos de numeración.'),
    ).toBeTruthy();
  });

  it('explains when the DIAN is not answering', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    const error = problem(503, 'dian.unavailable', 'La DIAN no responde.');
    (await expectRequest(http, (req) => req.url === RANGES_URL)).flush(error.body, error.options);

    expect(
      await screen.findByText(
        'La DIAN no responde en este momento. Intente de nuevo en unos minutos.',
      ),
    ).toBeTruthy();
  });

  it('explains when the company has no certificate to query the DIAN', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    const error = problem(404, 'certificate.not_found', 'No hay certificado.');
    (await expectRequest(http, (req) => req.url === RANGES_URL)).flush(error.body, error.options);

    expect(
      await screen.findByText(
        'La empresa no tiene un certificado activo. Cárguelo en la sección Certificado.',
      ),
    ).toBeTruthy();
  });
});
