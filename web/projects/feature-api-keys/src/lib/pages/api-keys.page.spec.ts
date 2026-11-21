import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { expectRequest, problem, renderApiKeysRoute } from '../testing/api-keys-testing';

// jsdom has no matchMedia nor scrollIntoView; the Optimus date picker overlay uses them.
beforeAll(() => {
  window.matchMedia ??= ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  })) as typeof window.matchMedia;
  Element.prototype.scrollIntoView ??= () => undefined;
});

const SECRET = 'ifk_live_9f8e7d6c5b4a39281706f5e4d3c2b1a0';
const URL = '/api/v1/api-keys';

const created = {
  id: 'k-1',
  company_id: 'company-1',
  name: 'Tienda en línea',
  key: SECRET,
  key_prefix: 'ifk_live_9f8e',
  scopes: ['documents:read', 'documents:write'],
  expires_at: null,
};

/** Llave existente como la lista el Api (nunca trae la llave completa ni su hash). */
const ERP_KEY = {
  id: 'k-0',
  name: 'ERP contable',
  key_prefix: 'ifk_live_0a1b',
  scopes: ['documents:read'],
  created_at: '2026-10-01T15:00:00Z',
  expires_at: '2027-01-31T23:59:59-05:00',
  last_used_at: '2026-11-16T15:00:00Z',
  is_revoked: false,
  revoked_at: null,
};

const isList = (request: { method: string; url: string }) =>
  request.method === 'GET' && request.url === URL;
const isCreate = (request: { method: string; url: string }) =>
  request.method === 'POST' && request.url === URL;

/** Contesta la lectura de la lista con `items` y el cursor siguiente. */
async function respondList(
  http: HttpTestingController,
  items: object[],
  nextCursor: string | null = null,
): Promise<TestRequest> {
  const request = await expectRequest(http, isList);
  request.flush({ items, next_cursor: nextCursor });
  return request;
}

async function open(items: object[] = [], options = {}) {
  const rendered = await renderApiKeysRoute('/llaves-api', options);
  await respondList(rendered.http, items);
  return rendered;
}

async function fillAndCreate() {
  fireEvent.input(await screen.findByLabelText(/Nombre de la llave/), {
    target: { value: 'Tienda en línea' },
  });
  fireEvent.click(screen.getByRole('checkbox', { name: /Consultar documentos/ }));
  fireEvent.click(screen.getByRole('checkbox', { name: /Emitir documentos/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Crear llave' }));
}

const rowsOf = (table: HTMLElement) =>
  within(table)
    .getAllByRole('row')
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent?.trim()),
    );

describe('ApiKeysPage', () => {
  it('offers only the scopes the session has', async () => {
    await open([], { scopes: ['documents:read', 'documents:write'] });

    expect(await screen.findByRole('heading', { level: 1, name: 'Llaves de API' })).toBeTruthy();
    expect(screen.getByRole('checkbox', { name: /Consultar documentos/ })).toBeTruthy();
    expect(screen.getByRole('checkbox', { name: /Emitir documentos/ })).toBeTruthy();
    expect(screen.queryByRole('checkbox', { name: /configuración/ })).toBeNull();
  });

  it('lists the keys of the company from the API, without the session notice', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');
    const request = await respondList(http, [ERP_KEY]);
    expect(request.request.params.get('limit')).toBe('20');
    expect(request.request.params.has('cursor')).toBe(false);

    const table = await screen.findByRole('table', { name: 'Llaves de la empresa' });
    await waitFor(() => expect(within(table).getByText('ERP contable')).toBeTruthy());
    expect(rowsOf(table)[0]?.slice(0, 5)).toEqual([
      'ERP contable',
      'ifk_live_0a1b…',
      'Consultar documentos y descargar sus archivos',
      '31/01/2027',
      '16/11/2026',
    ]);
    expect(within(table).getByText('Activa')).toBeTruthy();
    expect(screen.queryByText(/solo aparecen las que cree en esta sesión/)).toBeNull();
  });

  it('pages the list on the server with the cursor', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');
    await respondList(http, [ERP_KEY], 'cursor-2');

    fireEvent.click(await screen.findByRole('button', { name: 'Siguiente' }));

    const next = await expectRequest(http, isList);
    expect(next.request.params.get('cursor')).toBe('cursor-2');
    next.flush({ items: [{ ...ERP_KEY, id: 'k-9', name: 'Tienda vieja' }], next_cursor: null });
    expect(await screen.findByText('Tienda vieja')).toBeTruthy();
    expect(screen.queryByText('ERP contable')).toBeNull();
  });

  it('says when the company has no keys yet', async () => {
    await open();

    expect(await screen.findByText('La empresa aún no tiene llaves de API')).toBeTruthy();
  });

  it('explains when the list could not be read and retries', async () => {
    const { http } = await renderApiKeysRoute('/llaves-api');
    const { body, options } = problem(503, 'unavailable', 'No disponible.');
    (await expectRequest(http, isList)).flush(body, options);

    expect(await screen.findByText('No se pudieron consultar las llaves')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await respondList(http, [ERP_KEY]);
    expect(await screen.findByText('ERP contable')).toBeTruthy();
  });

  it('creates a key, shows it once, forgets it and reads the list again', async () => {
    const { http } = await open();
    await fillAndCreate();

    const request = await expectRequest(http, isCreate);
    expect(request.request.body).toEqual({
      name: 'Tienda en línea',
      scopes: ['documents:read', 'documents:write'],
      expires_at: null,
    });
    request.flush(created, { status: 201, statusText: 'Created' });

    const dialog = await screen.findByRole('dialog', { name: 'Copie su llave ahora' });
    expect(within(dialog).getByText(SECRET)).toBeTruthy();
    expect(within(dialog).getByRole('button', { name: 'Copiar llave' })).toBeTruthy();

    // La lista nunca trae la llave completa.
    await respondList(http, [
      {
        id: 'k-1',
        name: 'Tienda en línea',
        key_prefix: 'ifk_live_9f8e',
        scopes: created.scopes,
        created_at: '2026-11-17T15:00:00Z',
        expires_at: null,
        last_used_at: null,
        is_revoked: false,
        revoked_at: null,
      },
      ERP_KEY,
    ]);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Ya la copié' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(document.body.textContent).not.toContain(SECRET);

    const table = screen.getByRole('table', { name: 'Llaves de la empresa' });
    expect(within(table).getByText('Tienda en línea')).toBeTruthy();
    expect(within(table).getByText('ifk_live_9f8e…')).toBeTruthy();
  });

  it('sends the expiry date at the end of that day in Colombia', async () => {
    const { http } = await open();
    // The ui-date-picker takes the date typed as dd/mm/aaaa and gives the form the ISO date.
    const expiresAt = (await screen.findByRole('combobox', {
      name: /Vence el/,
    })) as HTMLInputElement;
    expect(expiresAt.placeholder).toBe('dd/mm/aaaa');
    fireEvent.keyDown(expiresAt, { key: '7' });
    fireEvent.input(expiresAt, { target: { value: '31/01/2027' } });
    await fillAndCreate();

    const request = await expectRequest(http, isCreate);
    expect(request.request.body.expires_at).toBe('2027-01-31T23:59:59-05:00');
  });

  it('shows an example in the name of the key', async () => {
    await open();

    expect(
      ((await screen.findByLabelText(/Nombre de la llave/)) as HTMLInputElement).placeholder,
    ).toBe('Tienda en línea');
  });

  it('asks for a name and at least one scope before calling the API', async () => {
    const { http } = await open();

    fireEvent.click(await screen.findByRole('button', { name: 'Crear llave' }));

    expect(await screen.findByText('Escriba un nombre para la llave.')).toBeTruthy();
    expect(screen.getByText('Elija al menos un permiso.')).toBeTruthy();
    http.expectNone(isCreate);
  });

  it('shows the API error when a scope is not allowed', async () => {
    const { http } = await open();
    await fillAndCreate();

    const { body, options } = problem(
      403,
      'api_key.scope_not_allowed',
      'La credencial no puede dar ese alcance.',
    );
    (await expectRequest(http, isCreate)).flush(body, options);

    expect(await screen.findByText('La credencial no puede dar ese alcance.')).toBeTruthy();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('revokes a listed key after confirming', async () => {
    const { http } = await open([ERP_KEY]);

    fireEvent.click(await screen.findByRole('button', { name: 'Revocar la llave ERP contable' }));
    const confirm = await screen.findByRole('dialog', { name: 'Revocar la llave' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Revocar' }));

    const revoke = await expectRequest(http, `${URL}/k-0`);
    expect(revoke.request.method).toBe('DELETE');
    revoke.flush(null, { status: 204, statusText: 'No Content' });

    expect(await screen.findByText('Revocada')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Revocar la llave ERP contable' })).toBeNull();
  });
});
