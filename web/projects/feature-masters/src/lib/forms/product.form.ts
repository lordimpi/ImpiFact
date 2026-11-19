import { WritableSignal, signal } from '@angular/core';
import { FieldTree, form, hidden, maxLength, pattern, required } from '@angular/forms/signals';
import { CreateProductCommand, ProductResponse } from '@impifact/api';
import { DEFAULT_UNIT_MEASURE, PER_UNIT_TAX } from '../models/catalogs';
import { asText, catalogId, optionalText } from './form-messages';

/*
 * Formulario del producto con Signal Forms. Los nombres del modelo son los del JSON del Api. Solo valida forma
 * (obligatorio, longitud y que los importes sean números con punto decimal); qué impuesto lleva tarifa o valor por
 * unidad lo valida el Api y su error se muestra junto al campo.
 */
export interface ProductModel {
  code: string;
  name: string;
  description: string;
  unit_measure_id: string;
  unit_price: string;
  tax_id: string;
  tax_percent: string;
  tax_per_unit_amount: string;
  is_active: boolean;
}

export const PRODUCT_FIELDS: readonly (keyof ProductModel)[] = [
  'code',
  'name',
  'description',
  'unit_measure_id',
  'unit_price',
  'tax_id',
  'tax_percent',
  'tax_per_unit_amount',
  'is_active',
];

export type ProductForm = FieldTree<ProductModel>;

/** Importe sin separadores de miles y con punto decimal (hasta 6 decimales, como acepta el Api). */
const AMOUNT = /^\d+(\.\d{1,6})?$/;
/** Tarifa en porcentaje (hasta 4 decimales, como acepta el Api). */
const PERCENT = /^\d+(\.\d{1,4})?$/;

export function emptyProduct(): ProductModel {
  return {
    code: '',
    name: '',
    description: '',
    unit_measure_id: String(DEFAULT_UNIT_MEASURE),
    unit_price: '',
    tax_id: '',
    tax_percent: '',
    tax_per_unit_amount: '',
    is_active: true,
  };
}

/** Si el impuesto elegido lleva tarifa: todos menos el que se cobra solo por unidad. */
export const hasPercent = (taxId: string) => taxId !== '' && taxId !== String(PER_UNIT_TAX);
/** Si el impuesto elegido admite valor por unidad: cualquiera; el Api dice cuál lo exige. */
export const hasPerUnit = (taxId: string) => taxId !== '';

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createProductForm(
  model: WritableSignal<ProductModel> = signal(emptyProduct()),
): ProductForm {
  return form(model, (path) => {
    required(path.code);
    maxLength(path.code, 100);
    required(path.name);
    maxLength(path.name, 500);
    maxLength(path.description, 1000);
    required(path.unit_measure_id);
    required(path.unit_price);
    pattern(path.unit_price, AMOUNT);
    hidden(path.tax_percent, { when: ({ valueOf }) => !hasPercent(valueOf(path.tax_id)) });
    pattern(path.tax_percent, PERCENT);
    hidden(path.tax_per_unit_amount, { when: ({ valueOf }) => !hasPerUnit(valueOf(path.tax_id)) });
    pattern(path.tax_per_unit_amount, AMOUNT);
  });
}

/** Cuerpo del alta (los importes van como texto para no perder decimales; el Api los acepta así). */
export function productCommand(model: ProductModel): CreateProductCommand {
  return {
    code: optionalText(model.code),
    name: optionalText(model.name),
    description: optionalText(model.description),
    unit_measure_id: catalogId(model.unit_measure_id),
    unit_price: optionalText(model.unit_price),
    tax_id: catalogId(model.tax_id),
    tax_percent: hasPercent(model.tax_id) ? optionalText(model.tax_percent) : null,
    tax_per_unit_amount: hasPerUnit(model.tax_id) ? optionalText(model.tax_per_unit_amount) : null,
  };
}

export function productModel(product: ProductResponse): ProductModel {
  return {
    code: asText(product.code),
    name: asText(product.name),
    description: asText(product.description),
    unit_measure_id: asText(product.unit_measure_id),
    unit_price: asText(product.unit_price),
    tax_id: asText(product.tax_id),
    tax_percent: asText(product.tax_percent),
    tax_per_unit_amount: asText(product.tax_per_unit_amount),
    is_active: product.is_active ?? true,
  };
}
