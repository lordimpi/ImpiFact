import { WritableSignal, signal } from '@angular/core';
import { FieldTree, form, maxLength, required } from '@angular/forms/signals';
import { CreateApiKeyBody } from '@impifact/api';

/*
 * Formulario de una llave nueva con Signal Forms. Solo valida forma (nombre obligatorio y su largo); qué alcances
 * puede dar la credencial lo decide el Api (403 `api_key.scope_not_allowed`).
 */
export interface ApiKeyModel {
  name: string;
  /** Fecha `YYYY-MM-DD` del `ui-date-picker`; vacía si no vence. */
  expires_at: string;
}

export type ApiKeyForm = FieldTree<ApiKeyModel>;

export const NAME_MAX_LENGTH = 100;

/** Alcances que una llave de empresa puede llevar, en el orden en que se muestran; nunca `platform:*`. */
export const COMPANY_SCOPES: readonly string[] = [
  'documents:read',
  'documents:write',
  'config:write',
];

/** Desfase de Colombia (sin horario de verano): la llave vence al final del día elegido en hora de Colombia. */
const COLOMBIA_END_OF_DAY = 'T23:59:59-05:00';

export function emptyApiKey(): ApiKeyModel {
  return { name: '', expires_at: '' };
}

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createApiKeyForm(
  model: WritableSignal<ApiKeyModel> = signal(emptyApiKey()),
): ApiKeyForm {
  return form(model, (path) => {
    required(path.name);
    maxLength(path.name, NAME_MAX_LENGTH);
  });
}

/** Cuerpo de `createApiKey` con los alcances elegidos. */
export function apiKeyBody(model: ApiKeyModel, scopes: readonly string[]): CreateApiKeyBody {
  return {
    name: model.name.trim(),
    scopes: [...scopes],
    expires_at: model.expires_at ? `${model.expires_at}${COLOMBIA_END_OF_DAY}` : null,
  };
}
