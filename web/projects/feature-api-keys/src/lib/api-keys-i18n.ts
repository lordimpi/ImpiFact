import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/*
 * Textos del feature con un *scope* (ámbito) de Transloco: viven en `src/i18n/es-CO.json` de esta librería, la
 * aplicación los publica en `/i18n/apiKeys/es-CO.json` (ver `angular.json`) y se piden solo al entrar al feature.
 * En las plantillas llevan el prefijo `apiKeys.`.
 */
export const API_KEYS_I18N_SCOPE = 'apiKeys';

export const provideApiKeysI18n = () => provideTranslocoScope(API_KEYS_I18N_SCOPE);

/** *Resolver* de las rutas del feature: la página se abre con sus textos ya cargados; nunca falla. */
export const apiKeysTextsResolver: ResolveFn<boolean> = async () => {
  const transloco = inject(TranslocoService);
  try {
    await firstValueFrom(transloco.load(`${API_KEYS_I18N_SCOPE}/${transloco.getActiveLang()}`));
  } catch {
    // Sin los textos la página se muestra igual; el error de red ya lo avisa core.
  }
  return true;
};

/** Traduce una clave del feature (sin el prefijo `apiKeys.`). */
export function apiKeysText(
  transloco: TranslocoService,
  key: string,
  params?: Record<string, unknown>,
): string {
  return transloco.translate(`${API_KEYS_I18N_SCOPE}.${key}`, params);
}
