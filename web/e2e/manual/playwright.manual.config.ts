import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';

// Configuración aparte para generar las capturas del manual de uso (docs/manual/img) con `npm run manual:screenshots`.
// No forma parte de la suite E2E: `playwright.config.ts` ignora esta carpeta. Usa la misma Api simulada y el mismo
// servidor (`ng serve`, o el que ya corra en `E2E_PORT`), con una ventana fija de 1366 × 768, tema claro, sin
// animaciones y en español de Colombia, para que dos corridas produzcan las mismas imágenes.
const port = process.env['E2E_PORT'] ?? '4200';
const baseURL = `http://localhost:${port}`;

export default defineConfig({
  testDir: '.',
  testMatch: 'manual.spec.ts',
  fullyParallel: true,
  // Pocas a la vez: con la máquina cargada, `ng serve` responde lento y las capturas no ganan nada con más.
  workers: 2,
  retries: 0,
  reporter: 'list',
  outputDir: path.join(__dirname, '..', '..', 'test-results', 'manual'),
  use: {
    ...devices['Desktop Chrome'],
    baseURL,
    locale: 'es-CO',
    timezoneId: 'America/Bogota',
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1,
    colorScheme: 'light',
    reducedMotion: 'reduce',
    trace: 'off',
    // Los campos de fecha del navegador toman el formato del idioma de Chromium, no el de `locale`.
    launchOptions: { args: ['--lang=es-CO'] },
  },
  webServer: {
    command: `npm start -- --port ${port}`,
    cwd: path.join(__dirname, '..', '..'),
    url: baseURL,
    reuseExistingServer: !process.env['CI'],
    timeout: 180_000,
  },
});
