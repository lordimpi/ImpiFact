import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/*
 * Textos del feature con un *scope* (ámbito) de Transloco: viven en `src/i18n/es-CO.json` de esta librería, la
 * aplicación los publica en `/i18n/platform/es-CO.json` (ver `angular.json`) y se piden solo al entrar al feature.
 * En las plantillas llevan el prefijo `platform.`.
 */
export const PLATFORM_I18N_SCOPE = 'platform';

export const providePlatformI18n = () => provideTranslocoScope(PLATFORM_I18N_SCOPE);

/** *Resolver* de las rutas del feature: la página se abre con sus textos ya cargados; nunca falla. */
export const platformTextsResolver: ResolveFn<boolean> = async () => {
  const transloco = inject(TranslocoService);
  try {
    await firstValueFrom(transloco.load(`${PLATFORM_I18N_SCOPE}/${transloco.getActiveLang()}`));
  } catch {
    // Sin los textos la página se muestra igual; el error de red ya lo avisa core.
  }
  return true;
};

/** Traduce una clave del feature (sin el prefijo `platform.`). */
export function platformText(
  transloco: TranslocoService,
  key: string,
  params?: Record<string, unknown>,
): string {
  return transloco.translate(`${PLATFORM_I18N_SCOPE}.${key}`, params);
}
