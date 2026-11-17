import { defineConfig, devices } from '@playwright/test';

// Configuración de Playwright para las pruebas E2E (de extremo a extremo) del panel.
// Levanta `ng serve` si no hay un servidor corriendo en el puerto 4200.
const baseURL = 'http://localhost:4200';
const isCi = !!process.env['CI'];

export default defineConfig({
  testDir: './e2e',
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
    command: 'npm start',
    url: baseURL,
    reuseExistingServer: !isCi,
    timeout: 180_000,
  },
});
