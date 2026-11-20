import { expect, test } from '@playwright/test';
import { signInAt } from './support/sign-in';

/*
 * El panel servido por la imagen de `Dockerfile.web` (nginx): encabezados de seguridad, caché, rutas del panel y la
 * política de seguridad de contenido (CSP) con la aplicación corriendo en el navegador. `ng serve` no manda CSP, así
 * que con `npm run e2e` estas pruebas se omiten; contra el contenedor (puerto 8180 del perfil «app» del compose):
 *   E2E_PORT=8180 npx playwright test e2e/served-panel.spec.ts
 */

interface CspWindow {
  cspViolations: string[];
}

test.beforeEach(async ({ request }) => {
  const response = await request.get('/');
  test.skip(
    !response.headers()['content-security-policy'],
    'Only against the web image (Dockerfile.web): ng serve sends no CSP.',
  );
});

test('sends the security headers and caches only the files with a hash in their name', async ({
  request,
}) => {
  // Una ruta del panel sirve index.html, que no se guarda en ninguna caché: lleva el nonce de esta respuesta.
  const route = await request.get('/documentos/nuevo');
  expect(route.status()).toBe(200);
  const html = await route.text();
  expect(html).toContain('<app-root');
  expect(route.headers()).toMatchObject({
    'cache-control': 'no-store',
    'referrer-policy': 'no-referrer',
    'x-content-type-options': 'nosniff',
    'x-frame-options': 'DENY',
  });
  expect(route.headers()['etag']).toBeUndefined();
  expect(route.headers()['last-modified']).toBeUndefined();
  const csp = route.headers()['content-security-policy'] ?? '';
  expect(csp).toContain("default-src 'self'");
  expect(csp).toContain("frame-ancestors 'none'");
  expect(csp).toContain("object-src 'none'");
  expect(csp).not.toContain('unsafe-inline');

  // El nonce del encabezado es el del HTML (script-src y style-src), sin el marcador del build, y cambia en cada
  // respuesta.
  const nonce = /script-src 'self' 'nonce-([A-Za-z0-9+/=]+)'/.exec(csp)?.[1];
  expect(nonce).toMatch(/^[A-Za-z0-9+/=]{22,}$/);
  expect(csp).toContain(`style-src 'self' 'nonce-${nonce}'`);
  expect(html).not.toContain('IMPIFACT_CSP_NONCE');
  expect(html).toMatch(new RegExp(`<app-root[^>]*ngcspnonce="${nonce}"`, 'i'));
  for (const [tag] of html.matchAll(/<(?:script|style)\b[^>]*>/gi)) {
    expect(tag).toContain(`nonce="${nonce}"`);
  }
  const again = await request.get('/');
  expect(again.headers()['content-security-policy']).not.toContain(nonce);

  const main = /<script src="(main-[A-Za-z0-9_-]{8}\.js)"/.exec(html)?.[1];
  expect(main).toBeDefined();
  const script = await request.get(`/${main}`);
  expect(script.headers()['cache-control']).toBe('public, max-age=31536000, immutable');
  expect(script.headers()['x-content-type-options']).toBe('nosniff');

  const texts = await request.get('/i18n/es-CO.json');
  expect(texts.headers()['cache-control']).toBe('no-cache');

  const config = await request.get('/config.json');
  expect(config.headers()['cache-control']).toBe('no-store');
  expect(await config.json()).toEqual({
    apiBaseUrl: expect.any(String),
    inactivity: { warnMinutes: expect.any(Number), logoutMinutes: expect.any(Number) },
    features: {},
  });

  // Un archivo que no existe y una ruta del Api no devuelven la página del panel.
  expect((await request.get('/chunk-NOTFOUND.js')).status()).toBe(404);
  expect((await request.get('/api/v1/me/companies')).status()).toBe(404);
});

test('runs under the CSP: styles, sign-in and the PDF preview frame', async ({ page }) => {
  await page.addInitScript(() => {
    const target = window as unknown as CspWindow;
    target.cspViolations = [];
    document.addEventListener('securitypolicyviolation', (event) =>
      target.cspViolations.push(`${event.violatedDirective} ${event.blockedURI}`),
    );
  });

  await signInAt(page, '/');

  // El script en línea de index.html (permitido por el nonce) activa la hoja de estilos que Angular carga sin bloquear.
  await expect(page.locator('link[rel="stylesheet"][href^="styles-"]')).toHaveAttribute(
    'media',
    'all',
  );

  // Como la vista previa del PDF (PdfPreviewComponent): un iframe con una URL blob: creada por la propia página. El
  // contenido es texto porque Chromium sin ventana no tiene visor de PDF (descarga el archivo y el iframe no termina de
  // cargar); a la política solo le importa que el iframe venga de blob: (frame-src).
  const loaded = await page.evaluate(
    () =>
      new Promise<boolean>((resolve) => {
        const frame = document.createElement('iframe');
        frame.addEventListener('load', () => resolve(true));
        frame.src = URL.createObjectURL(new Blob(['vista previa'], { type: 'text/plain' }));
        document.body.append(frame);
        setTimeout(() => resolve(false), 5_000);
      }),
  );
  expect(loaded).toBe(true);

  expect(await page.evaluate(() => (window as unknown as CspWindow).cspViolations)).toEqual([]);
});
