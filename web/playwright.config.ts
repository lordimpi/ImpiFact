import { defineConfig, devices } from '@playwright/test';

// Configuración de Playwright para las pruebas E2E (de extremo a extremo) del panel.
// Levanta `ng serve` si no hay un servidor corriendo en el puerto 4200 (o en `E2E_PORT`, para correr dos copias del
// workspace a la vez sin que una use el servidor de la otra).
const port = process.env['E2E_PORT'] ?? '4200';
const baseURL = `http://localhost:${port}`;
const isCi = !!process.env['CI'];

export default defineConfig({
  testDir: './e2e',
  // Las capturas del manual de uso tienen su propia configuración (`npm run manual:screenshots`).
  testIgnore: '**/manual/**',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  reporter: isCi ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL,
    locale: 'es-CO',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `npm start -- --port ${port}`,
    url: baseURL,
    reuseExistingServer: !isCi,
    timeout: 180_000,
  },
});
