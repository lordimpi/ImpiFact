/*
 * Ids fijos de los catálogos de la DIAN (Dirección de Impuestos y Aduanas Nacionales) que el feature necesita para
 * decidir qué campos mostrar. Las listas completas salen del Api (`CatalogsStore` y `catalogSearch` de core); aquí
 * solo quedan los ids de la semilla del backend (`backend/src/ImpiFact.Infrastructure/Seed/*.json`).
 */

/** Tipo de identificación NIT (Número de Identificación Tributaria): el único que lleva dígito de verificación. */
export const NIT_IDENTIFICATION_TYPE = 6;

/** Unidad por defecto de un producto nuevo («unidad», código 94). */
export const DEFAULT_UNIT_MEASURE = 70;

/** Impuesto que se cobra solo por unidad (INC Bolsas): lleva valor por unidad y no tarifa. */
export const PER_UNIT_TAX = 10;
/** Impuesto que admite tarifa o valor por unidad (IC, impuesto al consumo departamental). */
export const PERCENT_OR_PER_UNIT_TAX = 2;

/** Retenciones (ReteIVA, ReteRenta y ReteICA): no se cargan a un producto, así que no salen en su lista. */
export const WITHHOLDING_TAXES: readonly number[] = [5, 6, 7];
