import { WritableSignal, signal } from '@angular/core';
import {
  FieldTree,
  ValidationError,
  email,
  form,
  maxLength,
  minLength,
  required,
  validate,
} from '@angular/forms/signals';
import { AppError } from '@impifact/core';

/*
 * Formularios de la recuperación de contraseña con Signal Forms: pedir el enlace (`/recuperar`) y fijar la contraseña
 * nueva (`/restablecer`). Como en el login, el cliente solo valida forma (obligatorio, formato, largo de 10 a 256 de
 * la política del Api y que las dos contraseñas coincidan); la autoridad es el Api.
 */
export const PASSWORD_MIN_LENGTH = 10;
export const PASSWORD_MAX_LENGTH = 256;

export interface RecoveryModel {
  email: string;
}

export interface ResetModel {
  newPassword: string;
  confirmation: string;
}

export type RecoveryForm = FieldTree<RecoveryModel>;
export type ResetForm = FieldTree<ResetModel>;

/** Debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createRecoveryForm(
  model: WritableSignal<RecoveryModel> = signal({ email: '' }),
): RecoveryForm {
  return form(model, (path) => {
    required(path.email);
    email(path.email);
  });
}

/** Debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createResetForm(
  model: WritableSignal<ResetModel> = signal({ newPassword: '', confirmation: '' }),
): ResetForm {
  return form(model, (path) => {
    required(path.newPassword);
    minLength(path.newPassword, PASSWORD_MIN_LENGTH);
    maxLength(path.newPassword, PASSWORD_MAX_LENGTH);
    required(path.confirmation);
    validate(path.confirmation, ({ value, valueOf }) =>
      value() && value() !== valueOf(path.newPassword) ? { kind: 'mismatch' } : undefined,
    );
  });
}

/** Errores de validación del cliente → clave de texto del feature (sin el prefijo `auth.`). */
const CLIENT_ERROR_KEYS: Record<string, Partial<Record<string, string>>> = {
  email: { required: 'login.errors.emailRequired', email: 'login.errors.emailInvalid' },
  newPassword: {
    required: 'reset.errors.passwordRequired',
    minLength: 'reset.errors.passwordTooShort',
    maxLength: 'reset.errors.passwordTooLong',
  },
  confirmation: {
    required: 'reset.errors.confirmationRequired',
    mismatch: 'reset.errors.mismatch',
  },
};

/**
 * Mensaje bajo un campo: el primer error, solo después de tocarlo o de intentar enviar. Los del Api traen su texto;
 * los del cliente se traducen con `translate` (los parámetros, como el largo mínimo, van en el texto).
 */
export function resetFieldMessage(
  field: 'email' | keyof ResetModel,
  state: { touched(): boolean; errors(): readonly ValidationError[] },
  translate: (key: string) => string,
): string | null {
  const error = state.touched() ? state.errors()[0] : undefined;
  if (!error) {
    return null;
  }
  const key = CLIENT_ERROR_KEYS[field]?.[error.kind];
  return key ? translate(key) : (error.message ?? null);
}

/** Error del Api en un campo del JSON (`email`, `new_password`) → error de servidor para ese campo del formulario. */
export function serverFieldError(error: AppError, apiName: string, fieldTree: FieldTree<unknown>) {
  const message = error.fieldErrors[apiName]?.[0];
  return message ? [{ kind: 'server', message, fieldTree }] : [];
}

/** El enlace ya no sirve: token desconocido, vencido, usado o reemplazado (422), o ganó otra confirmación (409). */
export function isInvalidLink(error: AppError): boolean {
  return error.code === 'identity.invalid_reset_token' || error.status === 409;
}
