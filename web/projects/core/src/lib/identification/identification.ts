/*
 * Identificación de empresas y clientes: tipos de documento (catálogo `identification-types` de la semilla del
 * backend), solo dígitos y el dígito de verificación (DV) del NIT. El DV es un dígito de control, no un cálculo
 * fiscal; el Api lo vuelve a validar.
 */

/** Tipo de identificación NIT (código DIAN 31) en la semilla del backend. */
export const NIT_IDENTIFICATION_TYPE_ID = 6;

/**
 * Tipos de documento colombianos que solo llevan dígitos (por su id en la semilla): tarjeta de identidad (12),
 * cédula de ciudadanía (13), cédula de extranjería (22), NIT (31) y NUIP (91). El pasaporte, el NIT de otro país,
 * el PEP, el PPT y los documentos extranjeros pueden llevar letras.
 */
export const NUMERIC_IDENTIFICATION_TYPE_IDS: ReadonlySet<number> = new Set([2, 3, 5, 6, 10]);

/** Pesos de la DIAN para el DV, desde el último dígito del NIT. */
const CHECK_DIGIT_WEIGHTS = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

const NIT_DIGITS = /^\d{1,15}$/;

/** El texto sin nada que no sea un dígito (por ejemplo «900.123.456-8» queda «9001234568»). */
export function digitsOnly(text: string): string {
  return text.replace(/\D+/g, '');
}

/** Si el tipo de identificación (su id como texto) es NIT. */
export function isNitType(typeId: string): boolean {
  return typeId === String(NIT_IDENTIFICATION_TYPE_ID);
}

/** Si el número de ese tipo de identificación (su id como texto) solo admite dígitos. */
export function isNumericIdentificationType(typeId: string): boolean {
  return typeId !== '' && NUMERIC_IDENTIFICATION_TYPE_IDS.has(Number(typeId));
}

/**
 * DV del NIT con el módulo 11 de la DIAN: cada dígito, desde el último, por su peso; si el residuo de la suma entre
 * 11 es 0 o 1, ese es el DV, y si no, 11 menos el residuo. `null` si el NIT no tiene de 1 a 15 dígitos.
 */
export function nitCheckDigit(nit: string): string | null {
  if (!NIT_DIGITS.test(nit)) {
    return null;
  }
  let sum = 0;
  for (let position = 0; position < nit.length; position++) {
    sum += Number(nit[nit.length - 1 - position]) * CHECK_DIGIT_WEIGHTS[position]!;
  }
  const remainder = sum % 11;
  return String(remainder < 2 ? remainder : 11 - remainder);
}
