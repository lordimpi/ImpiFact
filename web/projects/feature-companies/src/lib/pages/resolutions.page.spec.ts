import { fireEvent, screen, within } from '@testing-library/angular';
import { expectRequest, problem, renderCompaniesRoute } from '../testing/companies-testing';

const URL = '/api/v1/companies/900123456/resolution';
const RANGES_URL = '/api/v1/companies/900123456/numbering-ranges';
const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });

async function open() {
  const rendered = await renderCompaniesRoute('/empresas/company-1/resoluciones');
  await screen.findByRole('heading', { level: 2, name: 'Resoluciones de numeración' });
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

describe('ResolutionsPage', () => {
  it('fills the habilitation example and saves it', async () => {
    const { http } = await open();
    expect(screen.getByText(/El Api no permite listar las resoluciones guardadas/)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Usar el ejemplo de habilitación (SETP)' }));
    expect(field(/^Prefijo/).value).toBe('SETP');
    fireEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }));

    const request = await expectRequest(http, URL);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(SETP);
    request.flush({ success: true, resolution: { id: 'res-1', ...SETP } });

    const list = await screen.findByRole('table', {
      name: 'Resoluciones guardadas en esta sesión',
    });
    expect(within(list).getByText('SETP')).toBeTruthy();
    expect(within(list).getByText('990000000 – 995000000')).toBeTruthy();
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
