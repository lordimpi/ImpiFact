import { TestBed } from '@angular/core/testing';
import { getState } from '@ngrx/signals';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { CompanySetupStore } from '../state/company-setup.store';
import {
  CERTIFICATE,
  expectRequest,
  problem,
  renderCompaniesRoute,
} from '../testing/companies-testing';

const SECRET = 'clave-del-p12';
const CERTIFICATES_URL = '/api/v1/companies/900123456/certificates';

async function open(certificates: unknown[] = [CERTIFICATE]) {
  const rendered = await renderCompaniesRoute('/empresas/company-1/certificado');
  await screen.findByRole('heading', { level: 2, name: 'Certificado digital' });
  (await expectRequest(rendered.http, CERTIFICATES_URL)).flush({ success: true, certificates });
  return rendered;
}

const fileInput = () => screen.getByLabelText(/^Archivo del certificado/) as HTMLInputElement;
const passwordInput = () =>
  screen.getByLabelText(/^Contraseña del certificado/) as HTMLInputElement;

function chooseFile(name = 'acme.p12', content = 'PFX-BYTES') {
  const file = new File([content], name, { type: 'application/x-pkcs12' });
  fireEvent.change(fileInput(), { target: { files: [file] } });
}

describe('CertificatePage', () => {
  it('lists the certificates with their holder, expiry and days left', async () => {
    await open();

    const list = await screen.findByRole('list', { name: 'Certificados de la empresa' });
    const item = within(list).getByRole('listitem');
    expect(within(item).getByText('CN=ACME SAS, SERIALNUMBER=900123456')).toBeTruthy();
    expect(within(item).getByText('225 días restantes')).toBeTruthy();
    expect(within(item).getByText('Activo')).toBeTruthy();
  });

  it('explains when there is no certificate yet', async () => {
    await open([]);

    expect(await screen.findByText('La empresa todavía no tiene certificados.')).toBeTruthy();
  });

  it('uses a password field that the browser does not autocomplete', async () => {
    await open();

    expect(passwordInput().type).toBe('password');
    expect(passwordInput().getAttribute('autocomplete')).toBe('new-password');
    expect(fileInput().getAttribute('accept')).toBe('.p12,.pfx');
  });

  it('requires the file and the password before calling the API', async () => {
    const { http } = await open();

    fireEvent.click(screen.getByRole('button', { name: 'Cargar certificado' }));

    expect(await screen.findByText('Elija el archivo del certificado.')).toBeTruthy();
    expect(screen.getByText('Escriba la contraseña del certificado.')).toBeTruthy();
    http.expectNone({ method: 'PUT' });
  });

  it('rejects a file that is not .p12 or .pfx', async () => {
    const { http } = await open();
    chooseFile('acme.pem');
    fireEvent.input(passwordInput(), { target: { value: SECRET } });

    fireEvent.click(screen.getByRole('button', { name: 'Cargar certificado' }));

    expect(await screen.findByText('El archivo debe ser .p12 o .pfx.')).toBeTruthy();
    http.expectNone({ method: 'PUT' });
  });

  it('uploads the file in base64 with the password and forgets the password', async () => {
    const { http } = await open();
    chooseFile();
    fireEvent.input(passwordInput(), { target: { value: SECRET } });

    fireEvent.click(screen.getByRole('button', { name: 'Cargar certificado' }));
    const upload = await expectRequest(http, '/api/v1/companies/900123456/certificate');
    expect(upload.request.method).toBe('PUT');
    expect(upload.request.body).toEqual({ certificate: btoa('PFX-BYTES'), password: SECRET });
    upload.flush({
      success: true,
      certificate_days_left: 365,
      certificate: { ...CERTIFICATE, id: 'cert-2', certificate_days_left: 365 },
    });

    expect(await screen.findByText('El certificado se cargó. Vence en 365 días.')).toBeTruthy();
    expect(passwordInput().value).toBe('');
    expect(fileInput().value).toBe('');
    const state = JSON.stringify(getState(TestBed.inject(CompanySetupStore)));
    expect(state).not.toContain(SECRET);
    const items = within(screen.getByRole('list', { name: 'Certificados de la empresa' }))
      .getAllByRole('listitem')
      .map((item) => item.textContent);
    expect(items).toHaveLength(2);
    expect(items[0]).toContain('365 días restantes');
    expect(items[1]).toContain('Reemplazado');
  });

  it('shows the API error in the form and clears the password', async () => {
    const { http } = await open();
    chooseFile();
    fireEvent.input(passwordInput(), { target: { value: SECRET } });

    fireEvent.click(screen.getByRole('button', { name: 'Cargar certificado' }));
    const upload = await expectRequest(http, '/api/v1/companies/900123456/certificate');
    const error = problem(
      422,
      'certificate.invalid',
      'La contraseña o el archivo del certificado no son válidos.',
    );
    upload.flush(error.body, error.options);

    expect(
      await screen.findByText('La contraseña o el archivo del certificado no son válidos.'),
    ).toBeTruthy();
    await waitFor(() => expect(passwordInput().value).toBe(''));
  });

  it('offers to retry when the list cannot be read', async () => {
    const { http } = await renderCompaniesRoute('/empresas/company-1/certificado');
    const first = await expectRequest(http, CERTIFICATES_URL);
    first.flush({ code: 'boom' }, { status: 500, statusText: 'Error' });

    fireEvent.click(await screen.findByRole('button', { name: 'Reintentar' }));
    (await expectRequest(http, CERTIFICATES_URL)).flush({
      success: true,
      certificates: [CERTIFICATE],
    });

    expect(await screen.findByText('225 días restantes')).toBeTruthy();
  });
});
