import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { expectRequest, problem, renderApiKeysRoute } from '../testing/api-keys-testing';

const SECRET = 'ifk_live_9f8e7d6c5b4a39281706f5e4d3c2b1a0';

const created = {
  id: 'k-1',
  company_id: 'company-1',
  name: 'Tienda en línea',
  key: SECRET,
  key_prefix: 'ifk_live_9f8e',
  scopes: ['documents:read', 'documents:write'],
  expires_at: null,
};

async function fillAndCreate() {
  fireEvent.input(await screen.findByLabelText(/Nombre de la llave/), {
    target: { value: 'Tienda en línea' },
  });
  fireEvent.click(screen.getByRole('checkbox', { name: /Consultar documentos/ }));
  fireEvent.click(screen.getByRole('checkbox', { name: /Emitir documentos/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Crear llave' }));
}

describe('ApiKeysPage', () => {
  it('offers only the scopes the session has and explains the missing list', async () => {
    await renderApiKeysRoute('/llaves-api', { scopes: ['documents:read', 'documents:write'] });

    expect(await screen.findByRole('heading', { level: 1, name: 'Llaves de API' })).toBeTruthy();
    expect(screen.getByRole('checkbox', { name: /Consultar documentos/ })).toBeTruthy();
    expect(screen.getByRole('checkbox', { name: /Emitir documentos/ })).toBeTruthy();
    expect(screen.queryByRole('checkbox', { name: /configuración/ })).toBeNull();
    expect(screen.getByText(/El Api todavía no lista las llaves/)).toBeTruthy();
  });

  it('creates a key, shows it once and forgets it when the dialog closes', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');
    await fillAndCreate();

    const request = await expectRequest(http, '/api/v1/api-keys');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Tienda en línea',
      scopes: ['documents:read', 'documents:write'],
      expires_at: null,
    });
    request.flush(created, { status: 201, statusText: 'Created' });

    const dialog = await screen.findByRole('dialog', { name: 'Copie su llave ahora' });
    expect(within(dialog).getByText(SECRET)).toBeTruthy();
    expect(within(dialog).getByRole('button', { name: 'Copiar llave' })).toBeTruthy();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Ya la copié' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(document.body.textContent).not.toContain(SECRET);

    const table = screen.getByRole('table', { name: 'Llaves creadas en esta sesión' });
    expect(within(table).getByText('Tienda en línea')).toBeTruthy();
    expect(within(table).getByText('ifk_live_9f8e…')).toBeTruthy();
  });

  it('sends the expiry date at the end of that day in Colombia', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');
    fireEvent.input(await screen.findByLabelText(/Vence el/), { target: { value: '2027-01-31' } });
    await fillAndCreate();

    const request = await expectRequest(http, '/api/v1/api-keys');
    expect(request.request.body.expires_at).toBe('2027-01-31T23:59:59-05:00');
  });

  it('asks for a name and at least one scope before calling the API', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');

    fireEvent.click(await screen.findByRole('button', { name: 'Crear llave' }));

    expect(await screen.findByText('Escriba un nombre para la llave.')).toBeTruthy();
    expect(screen.getByText('Elija al menos un permiso.')).toBeTruthy();
    http.expectNone('/api/v1/api-keys');
  });

  it('shows the API error when a scope is not allowed', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');
    await fillAndCreate();

    const { body, options } = problem(
      403,
      'api_key.scope_not_allowed',
      'La credencial no puede dar ese alcance.',
    );
    (await expectRequest(http, '/api/v1/api-keys')).flush(body, options);

    expect(await screen.findByText('La credencial no puede dar ese alcance.')).toBeTruthy();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('revokes a key after confirming', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');
    await fillAndCreate();
    (await expectRequest(http, '/api/v1/api-keys')).flush(created, {
      status: 201,
      statusText: 'Created',
    });
    fireEvent.click(await screen.findByRole('button', { name: 'Ya la copié' }));

    fireEvent.click(
      await screen.findByRole('button', { name: 'Revocar la llave Tienda en línea' }),
    );
    const confirm = await screen.findByRole('dialog', { name: 'Revocar la llave' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Revocar' }));

    const revoke = await expectRequest(http, '/api/v1/api-keys/k-1');
    expect(revoke.request.method).toBe('DELETE');
    revoke.flush(null, { status: 204, statusText: 'No Content' });

    expect(await screen.findByText('Revocada')).toBeTruthy();
  });
});
