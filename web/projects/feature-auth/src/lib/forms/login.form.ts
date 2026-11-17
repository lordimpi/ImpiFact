import { WritableSignal, signal } from '@angular/core';
import { FieldTree, ValidationError, email, form, required } from '@angular/forms/signals';
import { AppError } from '@impifact/core';

/*
 * Formulario de inicio de sesión con Signal Forms (`@angular/forms/signals`, API pública estable desde Angular 22).
 * Solo valida forma (obligatorio y formato de correo); la autoridad es el Api.
 */
export interface LoginModel {
  email: string;
  password: string;
}

export type LoginForm = FieldTree<LoginModel>;

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createLoginForm(
  model: WritableSignal<LoginModel> = signal({ email: '', password: '' }),
): LoginForm {
  return form(model, (path) => {
    required(path.email);
    email(path.email);
    required(path.password);
  });
}

/** Errores de validación del cliente → clave de texto del feature (sin el prefijo `auth.`). */
const CLIENT_ERROR_KEYS: Record<keyof LoginModel, Partial<Record<string, string>>> = {
  email: { required: 'login.errors.emailRequired', email: 'login.errors.emailInvalid' },
  password: { required: 'login.errors.passwordRequired' },
};

/**
 * Mensaje que se muestra bajo un campo: el primer error, y solo después de tocarlo o de intentar enviar.
 * Los errores del Api (`kind: 'server'`) traen su texto; los del cliente se traducen con `translate`.
 */
export function fieldMessage(
  field: keyof LoginModel,
  state: { touched(): boolean; errors(): readonly ValidationError[] },
  translate: (key: string) => string,
): string | null {
  const error = state.touched() ? state.errors()[0] : undefined;
  if (!error) {
    return null;
  }
  const key = CLIENT_ERROR_KEYS[field][error.kind];
  return key ? translate(key) : (error.message ?? null);
}

/** Errores por campo del Api que se ponen junto a su campo (los nombres son los del JSON del Api). */
export function serverFieldErrors(loginForm: LoginForm, error: AppError) {
  const fields = { email: loginForm.email, password: loginForm.password } as const;
  return (Object.keys(fields) as (keyof typeof fields)[]).flatMap((name) => {
    const message = error.fieldErrors[name]?.[0];
    return message ? [{ kind: 'server', message, fieldTree: fields[name] }] : [];
  });
}

/** Texto del error general (fuera de los campos): uno propio para los códigos conocidos o el del Api. */
export function generalErrorKey(error: AppError): string | null {
  if (error.code === 'identity.invalid_credentials') {
    return 'login.errors.invalidCredentials';
  }
  if (error.status === 429) {
    return 'login.errors.tooManyAttempts';
  }
  return null;
}
