import { FieldTree, ValidationError } from '@angular/forms/signals';
import { AppError } from '@impifact/core';

/*
 * Mensajes de los campos de los formularios del feature. El cliente solo valida forma (obligatorio, longitud,
 * formato); las reglas fiscales las aplica el Api y sus errores por campo (`kind: 'server'`) llegan con su texto.
 */

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** Errores del cliente → clave de texto del feature (sin el prefijo `companies.`). */
const CLIENT_ERROR_KEYS: Partial<Record<string, string>> = {
  required: 'common.errors.required',
  maxLength: 'common.errors.maxLength',
  minLength: 'common.errors.minLength',
  email: 'common.errors.email',
  pattern: 'common.errors.pattern',
};

/**
 * Mensaje bajo un campo: el primer error, solo después de tocarlo o de intentar enviar. `formatKey` reemplaza el
 * texto del error de formato cuando el campo tiene uno propio (por ejemplo, los números).
 */
export function fieldMessage(
  state: { touched(): boolean; errors(): readonly ValidationError[] },
  translate: Translate,
  formatKey?: string,
): string | null {
  const error = state.touched() ? state.errors()[0] : undefined;
  if (!error) {
    return null;
  }
  if (error.kind === 'pattern' && formatKey) {
    return translate(formatKey);
  }
  const key = CLIENT_ERROR_KEYS[error.kind];
  if (!key) {
    return error.message ?? null;
  }
  const { maxLength: max, minLength: min } = error as { maxLength?: number; minLength?: number };
  if (min !== undefined) {
    return translate(key, { min });
  }
  return translate(key, max === undefined ? undefined : { max });
}

/**
 * Errores por campo del Api que se ponen junto a su campo. Los nombres del modelo del formulario son los del JSON
 * del Api, así cada error cae en su campo sin traducir nombres.
 */
export function serverFieldErrors<Model extends object>(
  tree: FieldTree<Model>,
  names: readonly (keyof Model & string)[],
  error: AppError,
) {
  const fields = tree as unknown as Record<string, FieldTree<unknown>>;
  return names.flatMap((name) => {
    const message = error.fieldErrors[name]?.[0];
    const fieldTree = fields[name];
    return message && fieldTree ? [{ kind: 'server', message, fieldTree }] : [];
  });
}

/** Valor de un `<select>` de catálogo para el Api: vacío es `null`. */
export function catalogId(value: string): number | null {
  return value === '' ? null : Number(value);
}

/** Texto para el Api: vacío o solo espacios es `null`. */
export function optionalText(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

/** Valor del Api para un campo de texto del formulario. */
export function asText(value: unknown): string {
  return value === null || value === undefined ? '' : String(value);
}
