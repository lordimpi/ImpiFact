import { fireEvent, screen, within } from '@testing-library/angular';
import { expectRequest, problem, renderCompaniesRoute } from '../testing/companies-testing';

const URL = '/api/v1/companies/900123456/software';
const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });
const save = () => fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

async function open() {
  const rendered = await renderCompaniesRoute('/empresas/company-1/software');
  await screen.findByRole('heading', { level: 2, name: 'Software' });
  return rendered;
}

describe('SoftwarePage', () => {
  it('writes the PIN in a password field', async () => {
    await open();

    expect(field(/^PIN del software/).type).toBe('password');
    expect(screen.getByText('Aún no ha guardado el software en esta sesión.')).toBeTruthy();
  });

  it('checks the PIN format before calling the API', async () => {
    const { http } = await open();
    type(/^Identificador del software/, 'soft-123');
    type(/^PIN del software/, '12');

    save();

    expect(await screen.findByText('El PIN tiene 5 dígitos.')).toBeTruthy();
    http.expectNone(URL);
  });

  it('saves the software, forgets the PIN and lists what the API returns', async () => {
    const { http } = await open();
    type(/^Identificador del software/, 'soft-123');
    type(/^PIN del software/, '12345');
    type(/^Identificador del set de pruebas/, 'set-abc');

    save();
    const request = await expectRequest(http, URL);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ id: 'soft-123', pin: '12345', test_set_id: 'set-abc' });
    request.flush({
      success: true,
      software: [
        {
          document_kind: 1,
          type_environment_id: 2,
          identifier: 'soft-123',
          url: 'https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc',
          test_set_id: 'set-abc',
        },
      ],
    });

    const table = await screen.findByRole('table', { name: 'Software registrado' });
    const cells = within(table)
      .getAllByRole('cell')
      .map((cell) => cell.textContent?.trim());
    expect(cells).toEqual([
      'Factura electrónica y notas',
      'Habilitación (pruebas)',
      'soft-123',
      'set-abc',
    ]);
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
    (await expectRequest(http, URL)).flush(error.body, error.options);

    expect(
      await screen.findByText('El campo id no debe ser mayor que 64 caracteres.'),
    ).toBeTruthy();
    expect(field(/^PIN del software/).value).toBe('');
  });
});
