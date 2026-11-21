import { Locator, Page, expect, test } from '@playwright/test';
import path from 'node:path';
import { USER } from '../support/mock-api';
import { COMPANY_ID, OTHER_COMPANY_ID, mockManualApi } from './manual-api';
import { pickOption } from '../support/select';

/*
 * Capturas del manual de uso (docs/manual/Manual de uso.md). Cada prueba recorre una parte del panel contra la Api
 * simulada de `manual-api.ts` y guarda las imágenes en docs/manual/img con nombres fijos, en el orden del manual.
 * Se corre con `npm run manual:screenshots` (no entra en la suite E2E).
 *
 * Para que dos corridas den las mismas imágenes: ventana fija, reloj que arranca en una fecha fija (`clock.install`; el tiempo sigue
 * corriendo), sin animaciones ni cursor, y cada captura espera a que la red quede quieta y carguen las fuentes. La
 * barra lateral es `position: fixed`: las capturas son de la ventana visible (nunca `fullPage`), desplazando la
 * página hasta la parte que se quiere mostrar.
 */

const IMG = path.join(__dirname, '..', '..', '..', 'docs', 'manual', 'img');
/** Jueves 19 de noviembre de 2026, 10:30 a. m. en Colombia. */
const NOW = new Date('2026-11-19T10:30:00-05:00');
const PHONE = { width: 390, height: 844 };
const asPlatformAdmin = { companies: [], roles: ['platform_admin'], scopes: ['platform:*'] };

const mainNav = (page: Page) => page.getByRole('navigation', { name: 'Navegación principal' });

/** Espera a que la página quede quieta: sin pedidos pendientes, con las fuentes cargadas y sin cargadores. */
async function settle(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(async () => {
    await document.fonts.ready;
    // Las transiciones (por ejemplo, el color de una casilla recién marcada) terminan antes de la captura; las
    // animaciones sin fin (cargadores) no se esperan.
    const finite = document
      .getAnimations()
      .filter((animation) => animation.effect?.getTiming().iterations !== Infinity);
    await Promise.all(finite.map((animation) => animation.finished.catch(() => undefined)));
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  });
}

/** Captura de la ventana visible; por defecto, desde arriba de la página. */
async function shot(page: Page, name: string, options: { from?: Locator; offset?: number } = {}) {
  // El puntero queda donde fue el último clic: se aparta a una esquina para que no resalte nada.
  const size = page.viewportSize()!;
  await page.mouse.move(size.width - 1, size.height - 1);
  // Sin foco en campos: el anillo de foco de un campo recién llenado no siempre se dibuja igual.
  await page.evaluate(() => {
    const active = document.activeElement;
    if (
      active instanceof HTMLInputElement ||
      active instanceof HTMLSelectElement ||
      active instanceof HTMLTextAreaElement
    ) {
      active.blur();
    }
  });
  await settle(page);
  if (options.from) {
    const box = await options.from.boundingBox();
    const offset = options.offset ?? 96;
    await page.evaluate(
      (y) => window.scrollTo({ top: y, behavior: 'instant' }),
      box ? Math.max(0, box.y + (await page.evaluate(() => window.scrollY)) - offset) : 0,
    );
  } else {
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }));
  }
  await settle(page);
  await page.screenshot({
    path: path.join(IMG, `${name}.png`),
    animations: 'disabled',
    caret: 'hide',
  });
}

/** Cierra los avisos flotantes de confirmación para que no tapen la captura siguiente. */
async function dismissToasts(page: Page) {
  const close = page
    .getByRole('region', { name: 'Avisos' })
    .getByRole('button', { name: 'Cerrar' });
  // Un aviso puede cerrarse solo entre la cuenta y el clic: el clic tiene un tiempo corto y no falla.
  while ((await close.count()) > 0) {
    await close
      .first()
      .click({ timeout: 2_000 })
      .catch(() => undefined);
  }
}

async function fillLogin(page: Page) {
  await page.getByLabel('Correo electrónico').fill(USER.email);
  await page.getByLabel('Contraseña').fill(USER.password);
}

/** Entra con la Api del manual y llega a `target` (por defecto, la misma ruta pedida). */
async function signIn(
  page: Page,
  returnUrl: string,
  options: Parameters<typeof mockManualApi>[1] = {},
  target: string | RegExp = returnUrl,
) {
  await page.clock.install({ time: NOW });
  const mocks = await mockManualApi(page, options);
  await page.goto(`/login?returnUrl=${encodeURIComponent(returnUrl)}`);
  await fillLogin(page);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page).toHaveURL(target);
  if ((page.viewportSize()?.width ?? 0) >= 1024) {
    await expect(mainNav(page)).toBeVisible();
  }
  return mocks;
}

test.describe('acceso y estructura', () => {
  test('inicio de sesión, recuperación y selección de empresa', async ({ page }) => {
    await page.clock.install({ time: NOW });
    await mockManualApi(page, { companies: [COMPANY_ID, OTHER_COMPANY_ID] });
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
    await fillLogin(page);
    await shot(page, '01-inicio-sesion');

    await page.getByRole('link', { name: '¿Olvidó su contraseña?' }).click();
    await expect(
      page.getByRole('heading', { level: 1, name: 'Recuperar la contraseña' }),
    ).toBeVisible();
    await page.getByLabel('Correo electrónico').fill(USER.email);
    await shot(page, '02-recuperar-contrasena');

    await page.getByRole('link', { name: 'Volver a iniciar sesión' }).click();
    await fillLogin(page);
    await page.getByRole('button', { name: 'Ingresar' }).click();
    await expect(page).toHaveURL('/empresas/seleccionar');
    await expect(
      page.getByRole('button', { name: 'Trabajar con Café La Montaña SAS' }),
    ).toBeVisible();
    await shot(page, '03-seleccionar-empresa');
  });

  test('estructura de la pantalla y menú de la cuenta', async ({ page }) => {
    await signIn(page, '/');
    await expect(page.getByRole('heading', { level: 1, name: 'Tablero' })).toBeVisible();
    await shot(page, '04-estructura-pantalla');

    await page
      .getByRole('banner')
      .getByRole('button', { name: /Mi cuenta/ })
      .click();
    await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
    await shot(page, '05-menu-cuenta');
  });

  test('menú en el celular', async ({ page }) => {
    await page.setViewportSize(PHONE);
    await signIn(page, '/');
    await expect(page.getByRole('heading', { level: 1, name: 'Tablero' })).toBeVisible();
    await page.getByRole('button', { name: 'Menú de navegación' }).click();
    await expect(mainNav(page)).toBeVisible();
    await shot(page, '06-celular-menu');
  });
});

test.describe('administración de la plataforma', () => {
  test('crear una empresa, su llave y su primer dueño', async ({ page }) => {
    await signIn(page, '/plataforma/empresas', asPlatformAdmin);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Empresas de la plataforma' }),
    ).toBeVisible();
    await expect(page.getByText('Ferretería El Tornillo SAS')).toBeVisible();
    await shot(page, '10-plataforma-empresas');

    await page.getByRole('button', { name: 'Nueva empresa' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Nueva empresa' })).toBeVisible();
    await page.getByRole('textbox', { name: /^NIT/ }).pressSequentially('900345678');
    await expect(page.getByLabel('Dígito de verificación')).toHaveValue('9');
    await page.getByLabel('Razón social').fill('Tienda Naturista Vida Sana SAS');
    await pickOption(
      page,
      page.getByRole('combobox', { name: 'Tipo de organización' }),
      'Persona Jurídica y asimiladas',
    );
    await pickOption(page, page.getByRole('combobox', { name: 'Régimen' }), 'Responsable de IVA');
    await page.getByRole('checkbox', { name: 'R-99-PN No aplica – Otros' }).check();
    const department = page.getByRole('combobox', { name: 'Departamento', exact: true });
    await pickOption(page, department, 'Antioquia', 'antio');
    const municipality = page.getByRole('combobox', { name: 'Municipio', exact: true });
    await pickOption(page, municipality, 'Medellín', 'mede');
    await page.getByLabel('Dirección').fill('Carrera 70 # 44-12');
    await page.getByLabel('Teléfono').fill('6044123456');
    await page.getByLabel('Correo electrónico').fill('contabilidad@vidasana.example');
    await page.getByLabel('Matrícula mercantil').fill('21-778899-03');
    await shot(page, '11-plataforma-nueva-empresa');

    await page.getByRole('button', { name: 'Crear empresa' }).click();
    const keyDialog = page.getByRole('dialog', { name: 'Copie la llave de la empresa' });
    await expect(keyDialog).toBeVisible();
    await shot(page, '12-plataforma-llave-empresa');

    await keyDialog.getByRole('button', { name: 'Ya la copié' }).click();
    const userDialog = page.getByRole('dialog', {
      name: 'Crear usuario de Tienda Naturista Vida Sana SAS',
    });
    await expect(userDialog).toBeVisible();
    await userDialog.getByLabel('Correo electrónico').fill('gerencia@vidasana.example');
    await userDialog.getByLabel('Nombre').fill('Laura Gómez Arango');
    await userDialog.getByLabel('Contraseña').fill('Clave-temporal-2026');
    await shot(page, '13-plataforma-crear-dueno');
  });

  test('planes y asignación de un plan', async ({ page }) => {
    await signIn(page, '/plataforma/planes', asPlatformAdmin);
    await expect(page.getByRole('table', { name: 'Planes' }).getByText('Anual 5000')).toBeVisible();
    await shot(page, '14-plataforma-planes');

    await page.getByRole('button', { name: 'Nuevo plan' }).click();
    const planDialog = page.getByRole('dialog', { name: 'Nuevo plan' });
    await planDialog.getByLabel('Nombre').fill('Mensual 100');
    await planDialog.getByLabel('Facturas y notas').fill('100');
    await shot(page, '15-plataforma-nuevo-plan');
    await planDialog.getByRole('button', { name: 'Cancelar' }).click();

    await mainNav(page).getByRole('link', { name: 'Empresas', exact: true }).click();
    await page.getByRole('button', { name: 'Asignar plan a Ferretería El Tornillo SAS' }).click();
    const assign = page.getByRole('dialog', { name: 'Asignar plan a Ferretería El Tornillo SAS' });
    await pickOption(page, assign.getByRole('combobox', { name: 'Plan' }), 'Pyme 300');
    await shot(page, '16-plataforma-asignar-plan');
  });

  test('usuarios de una empresa', async ({ page }) => {
    await signIn(page, '/plataforma/empresas', asPlatformAdmin);
    await page.getByRole('link', { name: 'Usuarios de Café La Montaña SAS' }).click();
    await expect(page).toHaveURL('/plataforma/empresas/901458321/usuarios');
    await expect(
      page.getByRole('table', { name: 'Usuarios de la empresa' }).getByText('Diana Peña Ospina'),
    ).toBeVisible();
    await shot(page, '17-plataforma-usuarios-empresa');
  });
});

test.describe('configuración de la empresa', () => {
  async function openSection(page: Page, name: string) {
    await page
      .getByRole('navigation', { name: 'Secciones de la configuración' })
      .getByRole('link', { name })
      .click();
  }

  test('datos, ambientes, software, certificado, resoluciones y paso a producción', async ({
    page,
  }) => {
    test.slow();
    await signIn(page, '/empresa', {}, `/empresas/${COMPANY_ID}/datos`);
    await expect(page.getByLabel('Razón social')).toHaveValue('Café La Montaña SAS');
    await expect(page.getByRole('combobox', { name: 'Municipio', exact: true })).toHaveText(
      'Medellín',
    );
    await shot(page, '20-empresa-datos');

    await openSection(page, 'Ambientes');
    await expect(page.getByRole('heading', { level: 2, name: 'Ambientes' })).toBeVisible();
    await shot(page, '21-empresa-ambientes');

    await openSection(page, 'Software');
    await expect(page.getByRole('table', { name: 'Software registrado' })).toBeVisible();
    await shot(page, '22-empresa-software');

    await openSection(page, 'Certificado');
    await expect(page.getByText('224 días restantes')).toBeVisible();
    await shot(page, '23-empresa-certificado');

    await openSection(page, 'Resoluciones');
    await expect(
      page.getByRole('heading', { level: 2, name: 'Resoluciones de numeración' }),
    ).toBeVisible();
    await page.getByRole('button', { name: 'Usar el ejemplo de habilitación (SETP)' }).click();
    await expect(page.getByLabel('Prefijo')).toHaveValue('SETP');
    await shot(page, '24-empresa-resolucion-ejemplo', {
      from: page.getByRole('button', { name: 'Usar el ejemplo de habilitación (SETP)' }),
    });
    await page.getByRole('button', { name: 'Guardar resolución' }).click();
    await expect(page.getByRole('cell', { name: '18760000001' })).toBeVisible();
    await shot(page, '25-empresa-resoluciones');
    await dismissToasts(page);

    await openSection(page, 'Paso a producción');
    const steps = page.getByRole('list', { name: 'Pasos del paso a producción' });
    const step = (title: string) =>
      steps.getByRole('listitem').filter({ has: page.getByRole('heading', { name: title }) });
    await expect(
      step('Software y set de pruebas').getByText('Completo', { exact: true }),
    ).toBeVisible();
    await shot(page, '26-paso-produccion');

    await step('Set de pruebas de la DIAN')
      .getByRole('button', { name: 'Enviar factura de prueba' })
      .click();
    await expect(page.getByText('Factura SETP990000000: Aceptada')).toBeVisible({
      timeout: 15_000,
    });
    await dismissToasts(page);
    await shot(page, '27-paso-produccion-set-aceptado', {
      from: step('Set de pruebas de la DIAN'),
    });

    await page
      .getByRole('button', { name: 'Pasar a producción: Factura electrónica y notas' })
      .click();
    const dialog = page.getByRole('dialog', { name: 'Confirmar el paso a producción' });
    await expect(dialog).toBeVisible();
    await shot(page, '28-paso-produccion-confirmar', { from: step('Producción') });
  });

  test('usuarios de la empresa', async ({ page }) => {
    await signIn(page, '/');
    await mainNav(page).getByRole('link', { name: 'Usuarios' }).click();
    await expect(page).toHaveURL('/empresa/usuarios');
    await expect(
      page.getByRole('table', { name: 'Usuarios de la empresa' }).getByText('Diana Peña Ospina'),
    ).toBeVisible();
    await shot(page, '29-usuarios-empresa');

    await page.getByRole('button', { name: 'Nuevo usuario' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nuevo usuario' });
    await dialog.getByLabel('Correo electrónico').fill('julian.mejia@cafelamontana.example');
    await dialog.getByLabel('Nombre').fill('Julián Mejía Restrepo');
    await dialog.getByLabel('Teléfono').fill('3158765432');
    await dialog.getByLabel('Contraseña').fill('Clave-temporal-2026');
    // El rol viene en «Vendedor»: elegirlo de nuevo dejaría la lista con el anillo de foco. Se desplaza el diálogo
    // (no la página) hasta que el rol se vea entero.
    const role = dialog.getByRole('combobox', { name: 'Rol' });
    await expect(role).toHaveText('Vendedor');
    await role.scrollIntoViewIfNeeded();
    await shot(page, '29-usuarios-nuevo');
  });
});

test.describe('clientes y productos', () => {
  test('listas y formularios', async ({ page }) => {
    await signIn(page, '/clientes');
    await expect(page.getByText('Hotel Mirador del Valle SAS')).toBeVisible();
    await shot(page, '30-clientes');

    await page.getByRole('link', { name: 'Nuevo cliente' }).click();
    await expect(page).toHaveURL('/clientes/nuevo');
    await pickOption(page, page.getByRole('combobox', { name: 'Tipo de identificación' }), 'NIT');
    await page.getByRole('textbox', { name: 'Número de identificación' }).fill('900345678');
    await page.getByRole('button', { name: 'Buscar en la DIAN' }).click();
    await expect(page.getByRole('status')).toContainText('La DIAN tiene registrado a');
    await shot(page, '31-cliente-nuevo');

    await page.goto('/productos');
    await expect(page.getByText('Café en grano tostado 1 kg')).toBeVisible();
    await shot(page, '32-productos');

    await page.getByRole('link', { name: 'Nuevo producto' }).click();
    await expect(page).toHaveURL('/productos/nuevo');
    await page.getByRole('textbox', { name: 'Código', exact: true }).fill('CAF-DESC');
    await page
      .getByRole('textbox', { name: 'Nombre', exact: true })
      .fill('Café descafeinado 340 g');
    await page.getByLabel('Precio unitario sin impuestos').fill('27500');
    await shot(page, '33-producto-nuevo');
  });
});

test.describe('emisión', () => {
  test('factura con vista previa y envío a la DIAN', async ({ page }) => {
    test.slow();
    await signIn(page, '/documentos/nuevo');
    await expect(page.getByRole('heading', { level: 1, name: 'Emitir factura' })).toBeVisible();
    await page.getByLabel('Buscar cliente').fill('mirador');
    await page.getByRole('button', { name: /Elegir Hotel Mirador del Valle SAS/ }).click();
    await expect(page.getByText('Cliente elegido')).toBeVisible();
    await page.getByLabel('Agregar un producto').fill('grano');
    await page.getByRole('button', { name: /Agregar Café en grano tostado 1 kg/ }).click();
    await page.getByRole('group', { name: 'Línea 1' }).getByLabel('Cantidad').fill('20');
    await page.getByLabel('Agregar un producto').fill('catación');
    await page.getByRole('button', { name: /Agregar Servicio de catación para eventos/ }).click();
    await expect(page.getByTestId('payable')).toContainText('4.165.000,00');
    await shot(page, '40-factura-nueva');
    await shot(page, '41-factura-totales', {
      from: page.getByRole('group', { name: 'Línea 2' }),
    });

    await page.getByRole('button', { name: 'Vista previa' }).click();
    const preview = page.getByRole('dialog', { name: 'Vista previa del PDF' });
    await expect(preview.locator('iframe')).toHaveAttribute('src', /^blob:/);
    // Sin captura: el navegador de las pruebas no dibuja el PDF y el diálogo queda debajo de la barra lateral.
    await page.getByRole('button', { name: 'Cerrar la vista previa' }).click();

    await page.getByRole('button', { name: 'Emitir factura' }).click();
    await expect(page).toHaveURL('/documentos/d-1025');
    await expect(page.getByText('Consultando el estado en la DIAN…')).toBeVisible();
    await shot(page, '43-factura-emitida');
  });

  test('nota crédito', async ({ page }) => {
    await signIn(page, '/documentos/nota-credito?factura=d-1024');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Emitir nota crédito' }),
    ).toBeVisible();
    await expect(page.getByRole('group', { name: 'Línea 1' }).getByLabel('Código')).toHaveValue(
      'CAF-G1K',
    );
    await pickOption(
      page,
      page.getByRole('combobox', { name: /^Concepto de la corrección/ }),
      'Devolución parcial de los bienes y/o no aceptación parcial del servicio',
    );
    await expect(page.getByTestId('payable')).toContainText('4.165.000,00');
    await shot(page, '44-nota-credito');
  });

  test('nota débito', async ({ page }) => {
    await signIn(page, '/documentos/nota-debito');
    await expect(page.getByRole('heading', { level: 1, name: 'Emitir nota débito' })).toBeVisible();
    await page.getByLabel('Buscar factura').fill('1024');
    await page.getByRole('button', { name: /Elegir CLM1024/ }).click();
    const line = page.getByRole('group', { name: 'Línea 1' });
    await line.getByLabel('Código').fill('INT-MORA');
    await line.getByLabel('Descripción').fill('Intereses de mora de noviembre');
    await line.getByLabel('Precio unitario').fill('45000');
    await pickOption(
      page,
      page.getByRole('combobox', { name: /^Concepto de la corrección/ }),
      'Intereses',
    );
    await expect(page.getByTestId('payable')).toContainText('53.550,00');
    await shot(page, '45-nota-debito');
  });
});

test.describe('consulta de documentos', () => {
  test('lista, filtros, detalle y acciones', async ({ page }) => {
    await signIn(page, '/documentos');
    await expect(page.getByRole('link', { name: 'CLM1024', exact: true })).toBeVisible();
    await shot(page, '50-documentos-lista');

    await page.getByText('Más filtros').click();
    await expect(page.getByLabel('Enviado a la DIAN')).toBeVisible();
    await shot(page, '51-documentos-filtros');
    await page.getByText('Menos filtros').click();

    await page.getByRole('button', { name: 'Exportar' }).click();
    await expect(
      page.getByRole('menu', { name: 'Exportar' }).getByRole('menuitem', { name: 'PDF' }),
    ).toBeVisible();
    await shot(page, '56-documentos-exportar');
    await page.keyboard.press('Escape');
    await expect(page.getByRole('menu', { name: 'Exportar' })).toBeHidden();

    await page.getByLabel('Más acciones de CLM1024').click();
    await expect(page.getByRole('button', { name: 'Reenviar correo', exact: true })).toBeVisible();
    await shot(page, '52-documentos-mas-acciones', {
      from: page.getByRole('table', { name: 'Documentos emitidos' }),
    });
    await page.getByLabel('Más acciones de CLM1024').click();

    await page.getByRole('link', { name: 'CLM1024', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Factura CLM1024' })).toBeVisible();
    await shot(page, '53-documento-detalle');
    await shot(page, '54-documento-acciones', {
      from: page.getByRole('heading', { name: 'Acciones', exact: true }),
      offset: 420,
    });
    await page.getByRole('button', { name: 'Reenviar correo al cliente' }).click();
    await expect(page.getByText('El correo quedó en cola para enviarse.')).toBeVisible();
  });

  test('documento en el celular', async ({ page }) => {
    await page.setViewportSize(PHONE);
    await signIn(page, '/documentos/d-1024');
    await expect(page.getByRole('heading', { level: 1, name: 'Factura CLM1024' })).toBeVisible();
    await shot(page, '55-celular-documento');
  });
});

test.describe('tablero, avisos, llaves y plan', () => {
  test('tablero y avisos', async ({ page }) => {
    await signIn(page, '/');
    await expect(page.getByText('Quedan 75 números.')).toBeVisible();
    await shot(page, '61-tablero-plan-certificado', {
      from: page.getByRole('heading', { name: 'Plan', exact: true }),
    });

    await page.getByRole('button', { name: 'Avisos, 3 sin leer' }).click();
    await expect(
      page.getByRole('region', { name: 'Avisos' }).getByText('La DIAN rechazó un documento'),
    ).toBeVisible();
    await shot(page, '62-avisos-campana');
    await page.keyboard.press('Escape');

    await mainNav(page).getByRole('link', { name: 'Avisos' }).click();
    await expect(page.getByRole('table', { name: 'Avisos' })).toBeVisible();
    await shot(page, '63-avisos');
  });

  test('llaves de API y plan', async ({ page }) => {
    await signIn(page, '/llaves-api');
    await expect(page.getByRole('table', { name: 'Llaves de la empresa' })).toBeVisible();
    await page.getByLabel(/Nombre de la llave/).fill('Punto de venta');
    await page.getByRole('checkbox', { name: /Consultar documentos/ }).check();
    await page.getByRole('checkbox', { name: /Emitir documentos/ }).check();
    await shot(page, '64-llaves-api');

    await page.getByRole('button', { name: 'Crear llave' }).click();
    await expect(page.getByRole('dialog', { name: 'Copie su llave ahora' })).toBeVisible();
    await shot(page, '65-llave-creada');
    await page.getByRole('button', { name: 'Ya la copié' }).click();

    await mainNav(page).getByRole('link', { name: 'Mi plan' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Mi plan' })).toBeVisible();
    await expect(page.getByText('Pyme 300')).toBeVisible();
    await shot(page, '66-mi-plan');
  });
});

test.describe('solución de problemas', () => {
  test('documento rechazado', async ({ page }) => {
    await signIn(page, '/documentos/d-1022');
    await expect(page.getByRole('heading', { level: 1, name: 'Factura CLM1022' })).toBeVisible();
    await shot(page, '70-documento-rechazado', {
      from: page.getByRole('heading', { name: 'Respuesta de la DIAN' }),
      offset: 360,
    });
  });

  test('cierre de la sesión por inactividad', async ({ page }) => {
    await page.clock.install({ time: NOW });
    await mockManualApi(page);
    await page.goto('/login');
    await fillLogin(page);
    await page.getByRole('button', { name: 'Ingresar' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Tablero' })).toBeVisible();

    await page.clock.fastForward('25:01');
    const dialog = page.getByRole('dialog', { name: 'Su sesión se cerrará por inactividad' });
    await expect(dialog).toBeVisible();
    await shot(page, '71-aviso-inactividad');

    await page.clock.fastForward('05:01');
    await expect(page).toHaveURL('/login?reason=inactivity');
    await expect(page.getByRole('status')).toContainText('Su sesión se cerró por inactividad');
    await shot(page, '72-sesion-cerrada');
  });
});
