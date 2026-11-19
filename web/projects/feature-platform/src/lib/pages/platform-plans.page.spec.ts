import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { expectRequest, problem, renderPlatformRoute } from '../testing/platform-testing';

const isList = (request: { url: string; method: string }) =>
  request.method === 'GET' && request.url === '/api/v1/plans';

export const PLANS = [
  {
    id: 1,
    name: 'Pyme 300',
    period: 1,
    qty_docs_invoice: 300,
    qty_docs_payroll: 0,
    qty_docs_radian: 0,
    qty_docs_ds: 10,
    state: true,
    observations: null,
  },
  {
    id: 2,
    name: 'Anual 5000',
    period: '2',
    qty_docs_invoice: '5000',
    qty_docs_payroll: '0',
    qty_docs_radian: '0',
    qty_docs_ds: '0',
    state: false,
    observations: 'Solo para clientes antiguos',
  },
];

describe('PlatformPlansPage', () => {
  it('lists the plans of the platform', async () => {
    const { http } = await renderPlatformRoute('/plataforma/planes');

    (await expectRequest(http, isList)).flush({ success: true, data: PLANS });

    const table = await screen.findByRole('table', { name: 'Planes' });
    expect(await within(table).findByText('Pyme 300')).toBeTruthy();
    expect(within(table).getByText('Mensual')).toBeTruthy();
    expect(within(table).getByText('Anual')).toBeTruthy();
    expect(within(table).getByText('Inactivo')).toBeTruthy();
    expect(
      within(table)
        .getByRole('link', { name: 'Ver las empresas del plan Pyme 300' })
        .getAttribute('href'),
    ).toBe('/plataforma/planes/1');
  });

  it('creates a plan without asking for an id: the API assigns it', async () => {
    const { http } = await renderPlatformRoute('/plataforma/planes');
    (await expectRequest(http, isList)).flush({ success: true, data: PLANS });

    fireEvent.click(await screen.findByRole('button', { name: 'Nuevo plan' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo plan' });
    expect(within(dialog).queryByLabelText(/Id del plan/)).toBeNull();
    fireEvent.input(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Micro 50' } });
    // Signal Forms lee el evento `input` del <select>; el navegador manda también `change`.
    const period = within(dialog).getByLabelText(/Periodo/);
    fireEvent.change(period, { target: { value: '3' } });
    fireEvent.input(period, { target: { value: '3' } });
    fireEvent.input(within(dialog).getByLabelText('Facturas y notas'), {
      target: { value: '50' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    const save = await expectRequest(
      http,
      (request) => request.method === 'POST' && request.url === '/api/v1/plans',
    );
    expect(save.request.body).toEqual({
      name: 'Micro 50',
      period: 3,
      qty_docs_invoice: 50,
      qty_docs_payroll: null,
      qty_docs_radian: null,
      qty_docs_ds: null,
      state: true,
      observations: null,
    });
    save.flush(
      { success: true, plan: { id: 3, name: 'Micro 50' } },
      { status: 201, statusText: 'Created' },
    );

    await expectRequest(http, isList);
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('explains every plan field with a placeholder and a tooltip', async () => {
    const { http } = await renderPlatformRoute('/plataforma/planes');
    (await expectRequest(http, isList)).flush({ success: true, data: PLANS });

    fireEvent.click(await screen.findByRole('button', { name: 'Nuevo plan' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo plan' });

    const name = within(dialog).getByLabelText(/^Nombre/) as HTMLInputElement;
    const invoices = within(dialog).getByLabelText('Facturas y notas') as HTMLInputElement;
    expect(name.placeholder).toBe('Por ejemplo, Mensual 100');
    expect(invoices.placeholder).toBe('Por ejemplo, 100');
    const help = document.getElementById(
      (invoices.getAttribute('aria-describedby') ?? '').split(' ')[0]!,
    );
    expect(help?.getAttribute('role')).toBe('tooltip');
    expect(help?.textContent).toContain('en cada periodo del plan');
    expect(help?.textContent).toContain('0 = no puede emitir facturas ni notas');
    expect(
      within(dialog).getByText(/0 = la empresa no puede emitir documentos de esa familia/),
    ).toBeTruthy();
    expect(within(dialog).getAllByRole('tooltip', { hidden: true }).length).toBe(9);
  });

  it('edits a plan and shows a repeated name next to the field', async () => {
    const { http } = await renderPlatformRoute('/plataforma/planes');
    (await expectRequest(http, isList)).flush({ success: true, data: PLANS });

    fireEvent.click(await screen.findByRole('button', { name: 'Editar el plan Anual 5000' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar el plan Anual 5000' });
    const id = within(dialog).getByLabelText(/Id del plan/) as HTMLInputElement;
    expect(id.value).toBe('2');
    expect(id.readOnly).toBe(true);
    expect((within(dialog).getByLabelText(/Periodo/) as HTMLSelectElement).value).toBe('2');
    expect((within(dialog).getByLabelText('Plan activo') as HTMLInputElement).checked).toBe(false);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    const save = await expectRequest(http, '/api/v1/plans/2');
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toMatchObject({ id: 2, name: 'Anual 5000', state: false });
    const { body, options } = problem(422, 'validation.failed', 'Datos no válidos.', {
      name: ['El nombre ya existe.'],
    });
    save.flush(body, options);

    expect(await within(dialog).findByText('El nombre ya existe.')).toBeTruthy();
  });

  it('keeps an administrator with an active company out', async () => {
    const { http } = await renderPlatformRoute('/plataforma/planes', { scopes: [] });

    expect(await screen.findByText('stub')).toBeTruthy();
    http.expectNone('/api/v1/plans');
  });
});
