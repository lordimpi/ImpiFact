/**
 * Spanish (es-CO) texts of the newer showcase sections, merged into Transloco when the page opens
 * (`TranslocoService.setTranslation`, flat keys). The showcase is development only (the route does not exist in the
 * production build), so these keys stay next to it instead of growing the common es-CO.json the whole panel downloads.
 * They follow the same `devUi.*` naming, so moving them into public/i18n/es-CO.json later is a copy and paste.
 */
export const SHOWCASE_TEXTS: Readonly<Record<string, string>> = {
  'devUi.breadcrumb.home': 'Inicio',
  'devUi.breadcrumb.current': 'Vitrina',
  'devUi.pageAction': 'Nueva factura',
  'devUi.buttons.variants': 'Variantes',
  'devUi.buttons.sizes': 'Tamaños',
  'devUi.buttons.small': 'Pequeño',
  'devUi.buttons.normal': 'Normal',
  'devUi.buttons.large': 'Grande',
  'devUi.stats.title': 'Indicadores',
  'devUi.stats.issued': 'Facturas emitidas',
  'devUi.stats.issuedValue': '1.284',
  'devUi.stats.issuedTrend': '+12 % frente a octubre',
  'devUi.stats.sales': 'Ventas del mes',
  'devUi.stats.salesValue': '$ 48.920.000',
  'devUi.stats.period': 'Noviembre de 2026',
  'devUi.stats.rejected': 'Rechazadas',
  'devUi.stats.rejectedValue': '3',
  'devUi.stats.rejectedTrend': '+2 frente a octubre',
  'devUi.stats.customers': 'Clientes activos',
  'devUi.stats.customersValue': '312',
  'devUi.stats.customersHint': 'Con al menos una factura este año',
  'devUi.badges.title': 'Insignias',
  'devUi.badges.neutral': 'Borrador',
  'devUi.badges.info': 'En cola',
  'devUi.badges.success': 'Aceptado',
  'devUi.badges.warning': 'Pendiente',
  'devUi.badges.danger': 'Rechazado',
  'devUi.tabs.title': 'Pestañas',
  'devUi.tabs.label': 'Secciones de la empresa',
  'devUi.tabs.data': 'Datos',
  'devUi.tabs.resolutions': 'Resoluciones',
  'devUi.tabs.certificate': 'Certificado',
  'devUi.tabs.users': 'Usuarios',
  'devUi.cards.title': 'Tarjetas',
  'devUi.cards.resolution': 'Resolución de facturación',
  'devUi.cards.resolutionSubtitle': 'Vigente hasta el 31 de diciembre de 2026',
  'devUi.cards.resolutionBody':
    'Prefijo SETP, del 990000000 al 995000000. Quedan 4.998.716 números.',
  'devUi.cards.edit': 'Editar',
  'devUi.cards.updated': 'Actualizada hace 2 días',
  'devUi.cards.latest': 'Últimos documentos',
  'devUi.cards.latestCaption': 'Últimos documentos emitidos',
  'devUi.cards.viewAll': 'Ver todos',
  'devUi.cards.plain':
    'Una tarjeta sin encabezado, con relleno compacto, para agrupar contenido suelto.',
  'devUi.fields.nitHelp':
    'Número de identificación tributaria de la empresa, tal como aparece en el RUT, sin el dígito de verificación.',
  'devUi.fields.documentType': 'Tipo de documento',
  'devUi.fields.documentTypes.cc': 'Cédula de ciudadanía',
  'devUi.fields.documentTypes.nit': 'NIT',
  'devUi.fields.documentTypes.ce': 'Cédula de extranjería',
  'devUi.fields.municipality': 'Municipio',
  'devUi.fields.municipalityPlaceholder': 'Escriba para buscar…',
  'devUi.fields.municipalityLoading': 'Buscando municipios',
  'devUi.fields.municipalityError': 'No se pudieron cargar los municipios',
  'devUi.fields.municipalityEmpty': 'Ningún municipio coincide',
  'devUi.fields.municipalityIdle': 'Seleccione primero el departamento.',
  'devUi.fields.department': 'Departamento',
  'devUi.fields.departmentPlaceholder': 'Seleccione el departamento',
  'devUi.fields.regime': 'Régimen',
  'devUi.fields.regimePlaceholder': 'Seleccione el régimen',
  'devUi.fields.regimeError': 'Seleccione el régimen de la empresa.',
  'devUi.fields.expiresAt': 'Vence el',
  'devUi.fields.expiresAtHint':
    'Del 20 de noviembre de 2026 al 31 de diciembre de 2027; vacía si no vence.',
  'devUi.fields.certificate': 'Certificado digital',
  'devUi.fields.certificateHint': 'Archivo .p12 o .pfx de máximo 2 MB.',
  'devUi.checkboxes.title': 'Casillas',
  'devUi.checkboxes.sendEmail': 'Enviar la factura al correo del cliente',
  'devUi.checkboxes.long':
    'Entiendo que los documentos tendrán efecto fiscal real ante la DIAN y que no se pueden anular, solo corregir con una nota crédito.',
  'devUi.checkboxes.longHint': 'Una etiqueta larga pasa a la línea de abajo bajo su propio texto.',
  'devUi.checkboxes.disabled': 'Casilla deshabilitada',
  'devUi.checkboxes.disabledChecked': 'Casilla deshabilitada y marcada',
  'devUi.meter.title': 'Consumo del plan',
  'devUi.meter.label': 'Documentos del plan',
  'devUi.meter.value': '820 de 1.000 documentos',
  'devUi.meter.highValue': '960 de 1.000 documentos',
  'devUi.toasts.title': 'Avisos',
  'devUi.toasts.show': 'Mostrar avisos',
  'devUi.toasts.success': 'La factura SETP990000004 se envió a la DIAN.',
  'devUi.toasts.error': 'No se pudo enviar la factura. Intente de nuevo.',
};

/** Departments of the select example (sample data); a municipality belongs to `Math.floor(id / 1000)`. */
export const SAMPLE_DEPARTMENTS = [
  { id: 11, name: 'Bogotá, D.C.' },
  { id: 5, name: 'Antioquia' },
  { id: 76, name: 'Valle del Cauca' },
  { id: 8, name: 'Atlántico' },
  { id: 13, name: 'Bolívar' },
  { id: 68, name: 'Santander' },
] as const;

/** Regimes of the select example (sample data). */
export const SAMPLE_REGIMES = [
  { id: 48, name: 'Responsable de IVA' },
  { id: 49, name: 'No responsable de IVA' },
] as const;

/** Municipalities of the combobox example (sample data, like the table rows). */
export const SAMPLE_MUNICIPALITIES = [
  { id: 11001, name: 'Bogotá, D.C.' },
  { id: 5001, name: 'Medellín' },
  { id: 76001, name: 'Cali' },
  { id: 8001, name: 'Barranquilla' },
  { id: 13001, name: 'Cartagena de Indias' },
  { id: 68001, name: 'Bucaramanga' },
] as const;
