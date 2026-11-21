import { TestBed } from '@angular/core/testing';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import {
  COMPANY_PATH,
  EMPTY_READINESS,
  expectRequest,
  problem,
  renderCompaniesRoute,
  respond,
} from '../testing/companies-testing';
import { pickOption, stubOptimusDom, typeDate } from '../testing/optimus-testing';

const URL = `${COMPANY_PATH}/resolution`;
const LIST_URL = `${COMPANY_PATH}/resolutions`;
const RANGES_URL = `${COMPANY_PATH}/numbering-ranges`;
const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });
/** The input of a ui-date-picker or the ui-select, by the label of its field. */
const combobox = (label: RegExp) => screen.getByRole('combobox', { name: label });

beforeAll(stubOptimusDom);

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
  // The first test of the file pays the cold render of the route and of the Optimus controls (about 3 s alone,
  // more while the other files of the project run in parallel).
  it(
    'lists the saved resolutions with the next number, what remains and the days left',
    { timeout: 10_000 },
    async () => {
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
          'Cambiar próximo número',
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
          'Cambiar próximo número',
        ],
      ]);
      expect(screen.queryByText(/no permite listar/)).toBeNull();
    },
  );

  it('changes the next number of a saved resolution from a dialog and reads the list again', async () => {
    const { http } = await open([SETP_STATUS]);
    const table = await screen.findByRole('table', { name: 'Resoluciones guardadas' });

    fireEvent.click(within(table).getByRole('button', { name: 'Cambiar próximo número' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cambiar el próximo número' });
    expect(
      within(dialog).getByText(
        /si la empresa viene de otro sistema de facturación o ya usó números de este rango/,
      ),
    ).toBeTruthy();
    const input = within(dialog).getByLabelText(/^Próximo número/) as HTMLInputElement;
    expect(input.value).toBe('990000012');
    expect(input.placeholder).toBe('990000012');
    fireEvent.input(input, { target: { value: '990000101' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar próximo número' }));

    const request = await expectRequest(http, `${LIST_URL}/res-1/next-number`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ next_number: 990000101 });
    request.flush({
      success: true,
      message: 'Próximo número actualizado.',
      resolution_id: 'res-1',
      next_number: 990000101,
    });
    await respond(http, LIST_URL, { resolutions: [{ ...SETP_STATUS, next_number: 990000101 }] });
    await respond(http, `${COMPANY_PATH}/setup-status`, EMPTY_READINESS);

    expect(await within(table).findByText('990000101')).toBeTruthy();
    expect(screen.queryByRole('dialog', { name: 'Cambiar el próximo número' })).toBeNull();
    await waitFor(() =>
      expect(
        TestBed.inject(ToastStore)
          .toasts()
          .map((toast) => toast.text),
      ).toContain('Próximo número actualizado.'),
    );
  });

  it('shows the API reason next to the next number field and keeps the dialog open', async () => {
    const { http } = await open([SETP_STATUS]);
    const table = await screen.findByRole('table', { name: 'Resoluciones guardadas' });
    fireEvent.click(within(table).getByRole('button', { name: 'Cambiar próximo número' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cambiar el próximo número' });
    const input = within(dialog).getByLabelText(/^Próximo número/) as HTMLInputElement;
    fireEvent.input(input, { target: { value: '990000001' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar próximo número' }));

    const reason =
      'El próximo número no puede ser menor que 990000012, el próximo número actual: bajarlo podría repetir un número ya enviado a la DIAN.';
    const error = problem(422, 'validation.failed', 'La solicitud tiene datos inválidos.', {
      next_number: [reason],
    });
    (await expectRequest(http, `${LIST_URL}/res-1/next-number`)).flush(error.body, error.options);

    expect(await within(dialog).findByText(reason)).toBeTruthy();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(screen.getByRole('dialog', { name: 'Cambiar el próximo número' })).toBeTruthy();
  });

  it('checks the next number before calling the API', async () => {
    const { http } = await open([SETP_STATUS]);
    const table = await screen.findByRole('table', { name: 'Resoluciones guardadas' });
    fireEvent.click(within(table).getByRole('button', { name: 'Cambiar próximo número' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cambiar el próximo número' });
    fireEvent.input(within(dialog).getByLabelText(/^Próximo número/), { target: { value: '' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar próximo número' }));

    expect(await within(dialog).findByText('Este campo es obligatorio.')).toBeTruthy();
    http.expectNone(`${LIST_URL}/res-1/next-number`);
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
    // The date pickers show the dates of the example as dd/mm/aaaa.
    await waitFor(() =>
      expect((combobox(/^Fecha de la resolución/) as HTMLInputElement).value).toBe('19/01/2019'),
    );
    expect((combobox(/^Vigente desde/) as HTMLInputElement).value).toBe('19/01/2019');
    expect((combobox(/^Vigente hasta/) as HTMLInputElement).value).toBe('19/01/2030');
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

  it('takes the document type from its list and the dates typed in the date pickers', async () => {
    const { http } = await open();
    const documentType = combobox(/^Tipo de documento/);
    expect(documentType.textContent?.trim()).toBe('Factura de venta');
    expect((combobox(/^Vigente desde/) as HTMLInputElement).placeholder).toBe('dd/mm/aaaa');

    await pickOption(documentType, 'Nota crédito');
    type(/^Prefijo/, 'NC');
    type(/^Número de resolución/, '18760000002');
    type(/^Desde el número/, '1');
    type(/^Hasta el número/, '1000');
    typeDate(combobox(/^Fecha de la resolución/), '19/01/2019');
    typeDate(combobox(/^Vigente desde/), '20/01/2019');
    typeDate(combobox(/^Vigente hasta/), '20/01/2030');
    fireEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }));

    const request = await expectRequest(http, URL);
    expect(request.request.body).toMatchObject({
      type_document_id: 4,
      prefix: 'NC',
      resolution: '18760000002',
      resolution_date: '2019-01-19',
      from: 1,
      to: 1000,
      date_from: '2019-01-20',
      date_to: '2030-01-20',
    });
  });

  it('shows an example in every text field', async () => {
    await open();

    expect(field(/^Prefijo/).placeholder).toBe('SETP');
    expect(field(/^Número de resolución/).placeholder).toBe('18760000001');
    expect(field(/^Clave técnica/).placeholder).toBe('fc8eac422eba16e22ffd8c6f94b3f40a6e38162c');
    expect(field(/^Desde el número/).placeholder).toBe('990000000');
    expect(field(/^Hasta el número/).placeholder).toBe('995000000');
    expect(field(/^Identificador del software \(opcional\)/).placeholder).toBe(
      'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    );
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

  it('shows the DIAN answer without ranges as a neutral state, not as an error', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    (await expectRequest(http, (req) => req.url === RANGES_URL)).flush({
      success: false,
      numbering_ranges: [],
      operation_code: '301',
      operation_description:
        'No fue encontrado ningún rango de numeración para el NIT 900123456 y el software indicado.',
    });

    const message = await screen.findByText(
      'Respuesta de la DIAN: No fue encontrado ningún rango de numeración para el NIT 900123456 y el software indicado.',
    );
    expect(message.classList).not.toContain('companies-message--error');
    expect(message.classList).toContain('companies-message--info');
  });

  it('explains when the DIAN is not answering', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    const error = problem(503, 'dian.unavailable', 'La DIAN no responde.');
    (await expectRequest(http, (req) => req.url === RANGES_URL)).flush(error.body, error.options);

    const message = await screen.findByText(
      'La DIAN no responde en este momento. Intente de nuevo en unos minutos.',
    );
    expect(message.classList).toContain('companies-message--error');
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
