import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import {
  COMPANY_PATH,
  EMPTY_READINESS,
  ENVIRONMENTS,
  INVOICE_SOFTWARE,
  expectRequest,
  problem,
  renderCompaniesRoute,
  respond,
} from '../testing/companies-testing';

const URL = `${COMPANY_PATH}/software`;
const isSave = (request: { method: string; url: string }) =>
  request.method === 'PUT' && request.url === URL;
const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });
const save = () => fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
const cells = (table: HTMLElement) =>
  within(table)
    .getAllByRole('cell')
    .map((cell) => cell.textContent?.trim());

async function open(software: object[] = []) {
  const rendered = await renderCompaniesRoute('/empresas/company-1/software');
  await screen.findByRole('heading', { level: 2, name: 'Software' });
  await respond(rendered.http, URL, { software });
  await respond(rendered.http, `${COMPANY_PATH}/environments`, { environments: ENVIRONMENTS });
  return rendered;
}

describe('SoftwarePage', () => {
  it('writes the PIN in a password field', async () => {
    await open();

    expect(field(/^PIN del software/).type).toBe('password');
    expect(await screen.findByText('La empresa aún no tiene software registrado.')).toBeTruthy();
  });

  it('shows the saved software without its PIN and keeps the PIN when it is left empty', async () => {
    const { http } = await open([INVOICE_SOFTWARE]);

    const table = await screen.findByRole('table', { name: 'Software registrado' });
    expect(cells(table)).toEqual([
      'Factura electrónica y notas',
      'Habilitación (pruebas)',
      'soft-123',
      'set-abc',
      'PIN configurado',
    ]);
    await waitFor(() => expect(field(/^Identificador del software/).value).toBe('soft-123'));
    expect(field(/^Identificador del set de pruebas/).value).toBe('set-abc');
    expect(field(/^PIN del software/).value).toBe('');
    expect(field(/^PIN del software/).getAttribute('aria-required')).not.toBe('true');
    expect(screen.getByText(/Déjelo vacío para conservar el PIN guardado/)).toBeTruthy();

    type(/^Identificador del set de pruebas/, 'set-new');
    save();

    const request = await expectRequest(http, isSave);
    expect(request.request.body).toEqual({ test_set_id: 'set-new' });
    request.flush({ success: true, software: [{ ...INVOICE_SOFTWARE, test_set_id: 'set-new' }] });
    await respond(http, URL, { software: [{ ...INVOICE_SOFTWARE, test_set_id: 'set-new' }] });
    await respond(http, `${COMPANY_PATH}/setup-status`, EMPTY_READINESS);
  });

  it('asks for the PIN again when the identifier changes', async () => {
    const { http } = await open([INVOICE_SOFTWARE]);
    await waitFor(() => expect(field(/^Identificador del software/).value).toBe('soft-123'));

    type(/^Identificador del software/, 'soft-999');
    save();

    expect(await screen.findByText('Este campo es obligatorio.')).toBeTruthy();
    http.expectNone(isSave);
  });

  it('checks the PIN format before calling the API', async () => {
    const { http } = await open();
    type(/^Identificador del software/, 'soft-123');
    type(/^PIN del software/, '12');

    save();

    expect(await screen.findByText('El PIN tiene 5 dígitos.')).toBeTruthy();
    http.expectNone(isSave);
  });

  it('saves the software, forgets the PIN and lists what the API reads back', async () => {
    const { http } = await open();
    type(/^Identificador del software/, 'soft-123');
    type(/^PIN del software/, '12345');
    type(/^Identificador del set de pruebas/, 'set-abc');

    save();
    const request = await expectRequest(http, isSave);
    expect(request.request.body).toEqual({ id: 'soft-123', pin: '12345', test_set_id: 'set-abc' });
    // La respuesta del guardado no trae `has_pin`; la lectura siguiente sí.
    const saved = { ...INVOICE_SOFTWARE } as Partial<typeof INVOICE_SOFTWARE>;
    delete saved.has_pin;
    request.flush({ success: true, software: [saved] });
    await respond(http, URL, { software: [INVOICE_SOFTWARE] });

    const table = await screen.findByRole('table', { name: 'Software registrado' });
    expect(cells(table)).toContain('PIN configurado');
    expect(field(/^PIN del software/).value).toBe('');
    expect(field(/^Identificador del software/).value).toBe('soft-123');
  });

  it('shows the server field errors next to their fields', async () => {
    const { http } = await open();
    type(/^Identificador del software/, 'soft-123');
    type(/^PIN del software/, '12345');

    save();
    const error = problem(422, 'validation.failed', 'Los datos no son válidos.', {
      id: ['El campo id no debe ser mayor que 64 caracteres.'],
    });
    (await expectRequest(http, isSave)).flush(error.body, error.options);

    expect(
      await screen.findByText('El campo id no debe ser mayor que 64 caracteres.'),
    ).toBeTruthy();
    expect(field(/^PIN del software/).value).toBe('');
  });

  it('explains when the software could not be read', async () => {
    const { http } = await renderCompaniesRoute('/empresas/company-1/software');
    const error = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, (request) => request.method === 'GET' && request.url === URL)).flush(
      error.body,
      error.options,
    );

    expect(await screen.findByText('No se pudo consultar el software guardado.')).toBeTruthy();
  });
});
