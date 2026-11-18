/*
 * Etiqueta de los 5 flujos críticos del panel (docs/Arquitectura frontend.md, secciones 12 y 17): login y selección
 * de empresa, alta de empresa hasta producción, emisión de factura con descarga del PDF, nota crédito y cliente nuevo
 * usado en una factura. `npm run e2e:critical` corre solo estas pruebas (`playwright test --grep @critical`).
 */
export const CRITICAL = { tag: '@critical' };
