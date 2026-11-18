import { fireEvent, screen, within } from '@testing-library/angular';
import {
  COMPANY_PATH,
  EMPTY_READINESS,
  ENVIRONMENTS,
  expectRequest,
  problem,
  renderCompaniesRoute,
  respond,
} from '../testing/companies-testing';

const URL = `${COMPANY_PATH}/environment`;

const choose = (label: RegExp, value: string) => {
  const select = screen.getByLabelText(label) as HTMLSelectElement;
  fireEvent.change(select, { target: { value } });
};

async function open(environments: object[] = ENVIRONMENTS) {
  const rendered = await renderCompaniesRoute('/empresas/company-1/ambientes');
  await screen.findByRole('heading', { level: 2, name: 'Ambientes' });
  await respond(rendered.http, `${COMPANY_PATH}/environments`, { environments });
  return rendered;
}

describe('EnvironmentsPage', () => {
  it('shows the current environment the API reports, with no change by default', async () => {
    await open([...ENVIRONMENTS.slice(1), { document_kind: 1, type_environment_id: 1 }]);

    for (const label of [
      'Factura electrónica y notas',
      'Nómina electrónica',
      'Documento equivalente (POS)',
      'Documento soporte',
      'Eventos RADIAN',
    ]) {
      expect((screen.getByLabelText(label) as HTMLSelectElement).value).toBe('');
    }
    expect(await screen.findAllByText('Ambiente actual: Habilitación (pruebas)')).toHaveLength(4);
    expect(screen.getAllByText('Ambiente actual: Producción')).toHaveLength(1);
    expect(screen.queryByText(/no informa el ambiente actual/)).toBeNull();
  });

  it('says when the current environment could not be read', async () => {
    const { http } = await renderCompaniesRoute('/empresas/company-1/ambientes');
    const error = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, `${COMPANY_PATH}/environments`)).flush(error.body, error.options);

    expect(await screen.findAllByText('Ambiente actual: no se pudo consultar')).toHaveLength(5);
  });

  it('does not call the API when nothing changes', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByText('Elija al menos un ambiente para cambiar.')).toBeTruthy();
    http.expectNone(URL);
  });

  it('sends only the families that change and shows the environments the API returns', async () => {
    const { http } = await open([]);
    choose(/^Nómina electrónica/, '2');

    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    const request = await expectRequest(http, URL);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ payroll_type_environment_id: 2 });
    request.flush({
      success: true,
      environments: [
        { document_kind: 1, type_environment_id: 2 },
        { document_kind: 2, type_environment_id: 2 },
      ],
    });

    expect((await screen.findAllByText('Ambiente actual: Habilitación (pruebas)')).length).toBe(2);
    expect((screen.getByLabelText(/^Nómina electrónica/) as HTMLSelectElement).value).toBe('');
    // El cambio de ambiente puede cambiar el estado del paso a producción.
    await respond(http, `${COMPANY_PATH}/setup-status`, EMPTY_READINESS);
  });

  it('asks for an explicit confirmation before moving a family to production', async () => {
    const { http } = await open();
    choose(/^Factura electrónica y notas/, '1');

    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    const dialog = await screen.findByRole('dialog', { name: 'Confirmar el paso a producción' });
    expect(
      within(dialog).getByText(
        /efecto fiscal real ante la DIAN.*Familias que pasan a producción: Factura electrónica y notas\./,
      ),
    ).toBeTruthy();
    const confirm = within(dialog).getByRole('button', { name: 'Pasar a producción' });
    expect((confirm as HTMLButtonElement).disabled).toBe(true);
    http.expectNone(URL);

    fireEvent.click(
      within(dialog).getByRole('checkbox', {
        name: 'Entiendo que los documentos tendrán efecto fiscal real.',
      }),
    );
    expect((confirm as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(confirm);

    const request = await expectRequest(http, URL);
    expect(request.request.body).toEqual({ type_environment_id: 1 });
    request.flush({ success: true, environments: [{ document_kind: 1, type_environment_id: 1 }] });
    expect(await screen.findByText('Ambiente actual: Producción')).toBeTruthy();
  });

  it('does not send anything when the confirmation is cancelled', async () => {
    const { http } = await open();
    choose(/^Factura electrónica y notas/, '1');
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    const dialog = await screen.findByRole('dialog', { name: 'Confirmar el paso a producción' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    http.expectNone(URL);
  });
});
