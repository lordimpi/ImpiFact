/*
 * Catálogos de la DIAN (Dirección de Impuestos y Aduanas Nacionales) que usa el formulario de la empresa. El Api
 * todavía no tiene endpoints de catálogos, así que van aquí como listas fijas con los mismos ids de las tablas
 * sembradas en el backend (`backend/src/ImpiFact.Infrastructure/Seed/*.json`), igual que en feature-masters (un
 * feature no importa a otro). El servidor valida cada id; un id que no esté en la lista se muestra como «Otro (id N)».
 * Cuando exista `GET /api/v1/catalogs/{nombre}`, solo cambia este archivo: los formularios leen `CatalogOption`.
 */

export interface CatalogOption {
  /** Id del catálogo en el Api. */
  id: number;
  /** Nombre visible (ya en español). */
  name: string;
}

/** Tipo de identificación NIT (Número de Identificación Tributaria), el de casi todas las empresas. */
export const NIT_IDENTIFICATION_TYPE = 6;

export const IDENTIFICATION_TYPES: readonly CatalogOption[] = [
  { id: 6, name: 'NIT' },
  { id: 3, name: 'Cédula de ciudadanía' },
  { id: 5, name: 'Cédula de extranjería' },
  { id: 7, name: 'Pasaporte' },
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

/**
 * Opciones de un `<select>` con el valor guardado aunque no esté en la lista, para no borrar un id que llegó de
 * otra parte.
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
