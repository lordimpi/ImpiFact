import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { expectRequest, problem, renderPlatformRoute } from '../testing/platform-testing';

const PLANS = [
  { id: 1, name: 'Pyme 300', period: 1, state: true },
  { id: 2, name: 'Anual 5000', period: 2, state: true },
];

const ACME = {
  id: 'c-1',
  name: 'Acme SAS',
  nit: '900123456',
  dv: '7',
  email: 'facturas@acme.test',
  is_active: true,
  plan_id: 1,
  plan_name: 'Pyme 300',
};
const BETA = {
  id: 'c-2',
  name: 'Beta Ltda',
  nit: '800111222',
  dv: '3',
  email: 'beta@beta.test',
  is_active: false,
  plan_id: null,
  plan_name: null,
};

const LIST_URL = '/api/v1/platform/companies';
const isList = (request: { method: string; url: string }) =>
  request.method === 'GET' && request.url === LIST_URL;

async function respondList(
  http: HttpTestingController,
  items: object[],
  nextCursor: string | null = null,
): Promise<TestRequest> {
  const request = await expectRequest(http, isList);
  request.flush({ items, next_cursor: nextCursor });
  return request;
}

async function open(companies: object[] = [ACME, BETA], nextCursor: string | null = null) {
  const result = await renderPlatformRoute('/plataforma/empresas');
  (await expectRequest(result.http, '/api/v1/plans')).flush({ success: true, data: PLANS });
  await respondList(result.http, companies, nextCursor);
  await screen.findByRole('table', { name: 'Empresas de la plataforma' });
  return result;
}

const rowsOf = () =>
  within(screen.getByRole('table', { name: 'Empresas de la plataforma' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent?.trim()),
    );

function choosePlan(dialog: HTMLElement, plan: string) {
  // Signal Forms lee el evento `input` del <select>; el navegador manda también `change`.
  const select = within(dialog).getByLabelText(/^Plan/);
  fireEvent.change(select, { target: { value: plan } });
  fireEvent.input(select, { target: { value: plan } });
}

describe('PlatformCompaniesPage', () => {
  it('lists the companies of the platform with their current plan', async () => {
    const { http } = await renderPlatformRoute('/plataforma/empresas');
    (await expectRequest(http, '/api/v1/plans')).flush({ success: true, data: PLANS });
    const request = await respondList(http, [ACME, BETA]);
    expect(request.request.params.get('limit')).toBe('20');
    expect(request.request.params.has('search')).toBe(false);

    await waitFor(() =>
      expect(rowsOf().map((row) => row.slice(0, 5))).toEqual([
        ['Acme SAS', '900123456-7', 'facturas@acme.test', 'Pyme 300', 'Activa'],
        ['Beta Ltda', '800111222-3', 'beta@beta.test', 'Sin plan', 'Inactiva'],
      ]),
    );
    expect(screen.queryByText(/todavía no lista las empresas/)).toBeNull();
    expect(screen.queryByLabelText(/NIT de la empresa/)).toBeNull();
  });

  it('searches on the server after a pause in typing', async () => {
    const { http } = await open();

    fireEvent.input(screen.getByRole('searchbox', { name: 'Buscar empresa' }), {
      target: { value: 'bet' },
    });
    fireEvent.input(screen.getByRole('searchbox', { name: 'Buscar empresa' }), {
      target: { value: 'beta' },
    });

    const search = await respondList(http, [BETA]);
    expect(search.request.params.get('search')).toBe('beta');
    await waitFor(() => expect(rowsOf()).toHaveLength(1));
    http.expectNone(isList);
  });

  it('loads more companies with the cursor', async () => {
    const { http } = await open([ACME], 'cursor-2');

    fireEvent.click(screen.getByRole('button', { name: 'Cargar más empresas' }));

    const more = await respondList(http, [BETA]);
    expect(more.request.params.get('cursor')).toBe('cursor-2');
    await waitFor(() => expect(rowsOf()).toHaveLength(2));
    expect(screen.queryByRole('button', { name: 'Cargar más empresas' })).toBeNull();
  });

  it('says when the platform has no companies', async () => {
    await open([]);

    expect(await screen.findByText('La plataforma aún no tiene empresas')).toBeTruthy();
  });

  it('explains when the companies could not be read and retries', async () => {
    const { http } = await renderPlatformRoute('/plataforma/empresas');
    (await expectRequest(http, '/api/v1/plans')).flush({ success: true, data: PLANS });
    const { body, options } = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, isList)).flush(body, options);

    expect(await screen.findByText('No se pudieron consultar las empresas')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await respondList(http, [ACME]);
    await waitFor(() => expect(rowsOf()).toHaveLength(1));
  });

  it('assigns a plan to the company picked from the list', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Asignar plan a Beta Ltda' }));
    const dialog = await screen.findByRole('dialog', { name: 'Asignar plan a Beta Ltda' });
    choosePlan(dialog, '2');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar plan' }));

    const request = await expectRequest(http, '/api/v1/companies/800111222/plan');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ plan_id: 2 });
    request.flush({
      success: true,
      plan: { id: 2, name: 'Anual 5000' },
      start_plan_date: '2026-11-17T20:00:00Z',
    });

    expect(
      await screen.findByText(/Se asignó el plan Anual 5000 a Beta Ltda \(NIT 800111222\)/),
    ).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(rowsOf()[1]?.[3]).toBe('Anual 5000');
  });

  it('asks for the plan before calling the API', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Asignar plan a Acme SAS' }));
    const dialog = await screen.findByRole('dialog', { name: 'Asignar plan a Acme SAS' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar plan' }));

    expect(await within(dialog).findByText('Este campo es obligatorio.')).toBeTruthy();
    http.expectNone((request) => request.url.includes('/plan'));
  });

  it('explains a company that no longer exists', async () => {
    const { http } = await open();
    fireEvent.click(screen.getByRole('button', { name: 'Asignar plan a Acme SAS' }));
    const dialog = await screen.findByRole('dialog', { name: 'Asignar plan a Acme SAS' });
    choosePlan(dialog, '2');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar plan' }));

    const { body, options } = problem(404, 'company.not_found', 'Not found');
    (await expectRequest(http, '/api/v1/companies/900123456/plan')).flush(body, options);

    expect(await within(dialog).findByText('La empresa ya no existe.')).toBeTruthy();
  });
});
