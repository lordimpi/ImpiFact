/*
 * Catálogos de la DIAN (Dirección de Impuestos y Aduanas Nacionales) que usan los formularios del feature. El Api
 * todavía no tiene endpoints de catálogos, así que van aquí como listas fijas con los mismos ids de las tablas
 * sembradas en el backend (`backend/src/ImpiFact.Infrastructure/Seed/*.json`). El servidor valida cada id; si un
 * registro trae un id que no está en la lista, el formulario lo muestra como «Otro (id N)» para no perderlo.
 * Cuando existan los endpoints, estas listas se reemplazan por consultas al Api.
 */

export interface CatalogOption {
  /** Id del catálogo en el Api. */
  id: number;
  /** Nombre visible (ya en español). */
  name: string;
}

/** Tipo de identificación NIT (Número de Identificación Tributaria): el único que lleva dígito de verificación. */
export const NIT_IDENTIFICATION_TYPE = 6;

export const IDENTIFICATION_TYPES: readonly CatalogOption[] = [
  { id: 6, name: 'NIT' },
  { id: 3, name: 'Cédula de ciudadanía' },
  { id: 5, name: 'Cédula de extranjería' },
  { id: 4, name: 'Tarjeta de extranjería' },
  { id: 2, name: 'Tarjeta de identidad' },
  { id: 1, name: 'Registro civil' },
  { id: 7, name: 'Pasaporte' },
  { id: 8, name: 'Documento de identificación extranjero' },
  { id: 9, name: 'NIT de otro país' },
  { id: 10, name: 'NUIP (Número Único de Identificación Personal)' },
  { id: 11, name: 'PEP (Permiso Especial de Permanencia)' },
  { id: 12, name: 'PPT (Permiso por Protección Temporal)' },
];

export const ORGANIZATION_TYPES: readonly CatalogOption[] = [
  { id: 1, name: 'Persona jurídica y asimiladas' },
  { id: 2, name: 'Persona natural y asimiladas' },
];

export const REGIMES: readonly CatalogOption[] = [
  { id: 1, name: 'Responsable de IVA' },
  { id: 2, name: 'No responsable de IVA' },
];

export const LIABILITIES: readonly CatalogOption[] = [
  { id: 7, name: 'O-13 Gran contribuyente' },
  { id: 9, name: 'O-15 Autorretenedor' },
  { id: 14, name: 'O-23 Agente de retención en el impuesto sobre las ventas' },
  { id: 112, name: 'O-47 Régimen Simple de Tributación (SIMPLE)' },
  { id: 117, name: 'R-99-PN No aplica (otros)' },
];

/** Capitales de departamento; el catálogo completo tiene 1122 municipios y llegará con el endpoint. */
export const MUNICIPALITIES: readonly CatalogOption[] = [
  { id: 149, name: 'Bogotá, D.C.' },
  { id: 1, name: 'Medellín (Antioquia)' },
  { id: 1006, name: 'Cali (Valle del Cauca)' },
  { id: 126, name: 'Barranquilla (Atlántico)' },
  { id: 150, name: 'Cartagena de Indias (Bolívar)' },
  { id: 846, name: 'Bucaramanga (Santander)' },
  { id: 780, name: 'San José de Cúcuta (Norte de Santander)' },
  { id: 832, name: 'Pereira (Risaralda)' },
  { id: 319, name: 'Manizales (Caldas)' },
  { id: 820, name: 'Armenia (Quindío)' },
  { id: 959, name: 'Ibagué (Tolima)' },
  { id: 605, name: 'Neiva (Huila)' },
  { id: 687, name: 'Villavicencio (Meta)' },
  { id: 716, name: 'Pasto (Nariño)' },
  { id: 362, name: 'Popayán (Cauca)' },
  { id: 657, name: 'Santa Marta (Magdalena)' },
  { id: 404, name: 'Valledupar (Cesar)' },
  { id: 429, name: 'Montería (Córdoba)' },
  { id: 933, name: 'Sincelejo (Sucre)' },
  { id: 196, name: 'Tunja (Boyacá)' },
  { id: 642, name: 'Riohacha (La Guajira)' },
  { id: 575, name: 'Quibdó (Chocó)' },
  { id: 346, name: 'Florencia (Caquetá)' },
  { id: 1055, name: 'Yopal (Casanare)' },
  { id: 1048, name: 'Arauca (Arauca)' },
  { id: 1074, name: 'Mocoa (Putumayo)' },
  { id: 1087, name: 'San Andrés (San Andrés y Providencia)' },
  { id: 1089, name: 'Leticia (Amazonas)' },
  { id: 1100, name: 'Inírida (Guainía)' },
  { id: 1109, name: 'San José del Guaviare (Guaviare)' },
  { id: 1113, name: 'Mitú (Vaupés)' },
  { id: 1119, name: 'Puerto Carreño (Vichada)' },
];

/** Unidades más usadas; el catálogo completo tiene 1093 y llegará con el endpoint. */
export const UNIT_MEASURES: readonly CatalogOption[] = [
  { id: 70, name: 'Unidad (94)' },
  { id: 886, name: 'Número de unidades internacionales (NIU)' },
  { id: 730, name: 'Hora (HUR)' },
  { id: 606, name: 'Día (DAY)' },
  { id: 767, name: 'Kilogramo (KGM)' },
  { id: 692, name: 'Gramo (GRM)' },
  { id: 821, name: 'Litro (LTR)' },
  { id: 686, name: 'Galón (GLL)' },
  { id: 865, name: 'Metro (MTR)' },
  { id: 863, name: 'Metro cuadrado (MTK)' },
  { id: 864, name: 'Metro cúbico (MTQ)' },
  { id: 797, name: 'Kilovatio hora (KWH)' },
  { id: 381, name: 'Caja (BX)' },
  { id: 938, name: 'Par (PR)' },
  { id: 993, name: 'Conjunto (SET)' },
  { id: 1093, name: 'Mutuamente definido (ZZ)' },
];

/** Unidad por defecto de un producto nuevo. */
export const DEFAULT_UNIT_MEASURE = 70;

/** Impuesto que se cobra solo por unidad (INC Bolsas): lleva valor por unidad y no tarifa. */
export const PER_UNIT_TAX = 10;
/** Impuesto que admite tarifa o valor por unidad (IC, impuesto al consumo departamental). */
export const PERCENT_OR_PER_UNIT_TAX = 2;

/** Impuestos que se cargan a un producto (los de retención no van en el producto). */
export const TAXES: readonly CatalogOption[] = [
  { id: 1, name: 'IVA (impuesto al valor agregado)' },
  { id: 4, name: 'INC (impuesto nacional al consumo)' },
  { id: 2, name: 'IC (impuesto al consumo departamental)' },
  { id: 3, name: 'ICA (impuesto de industria y comercio)' },
  { id: 10, name: 'INC Bolsas' },
  { id: 11, name: 'INCarbono' },
  { id: 12, name: 'INCombustibles' },
  { id: 13, name: 'Sobretasa combustibles' },
  { id: 9, name: 'Timbre' },
  { id: 8, name: 'Fondo de horticultura' },
  { id: 16, name: 'IC porcentual' },
  { id: 17, name: 'IC datos' },
  { id: 19, name: 'ICL (impuesto al consumo de licores)' },
  { id: 20, name: 'INPP (impuesto nacional a productos plásticos)' },
  { id: 21, name: 'IBUA (impuesto a bebidas ultraprocesadas azucaradas)' },
  { id: 22, name: 'ICUI (impuesto a comestibles ultraprocesados)' },
  { id: 23, name: 'ADV (ad valorem)' },
  { id: 15, name: 'No aplica' },
];

/**
 * Opciones de un `<select>` con el valor guardado aunque no esté en la lista: así editar un registro no borra un id
 * que llegó de otra parte (por ejemplo, de la emisión por el Api).
 */
export function optionsWith(
  options: readonly CatalogOption[],
  current: string,
  otherName: (id: number) => string,
): readonly CatalogOption[] {
  const id = Number(current);
  if (current === '' || !Number.isInteger(id) || options.some((option) => option.id === id)) {
    return options;
  }
  return [...options, { id, name: otherName(id) }];
}
