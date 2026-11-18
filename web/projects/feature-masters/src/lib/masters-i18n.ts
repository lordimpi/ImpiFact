import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/*
 * Textos del feature con un *scope* (ámbito) de Transloco: viven en `src/i18n/es-CO.json` de esta librería, la
 * aplicación los publica en `/i18n/masters/es-CO.json` (ver `angular.json`) y se piden solo al entrar al feature.
 * En las plantillas se usan con el prefijo `masters.` (`'masters.customers.list.title' | transloco`).
 */
export const MASTERS_I18N_SCOPE = 'masters';

/** Va en los `providers` de cada página o componente del feature. */
export const provideMastersI18n = () => provideTranslocoScope(MASTERS_I18N_SCOPE);

/** Carga los textos del feature en el idioma activo; nunca falla (sin red quedan las claves). */
export async function loadMastersTexts(transloco: TranslocoService): Promise<void> {
  try {
    await firstValueFrom(transloco.load(`${MASTERS_I18N_SCOPE}/${transloco.getActiveLang()}`));
  } catch {
    // Sin los textos la página se muestra igual; el error de red ya lo avisa core.
  }
}

/** *Resolver* de las rutas del feature: la página se abre con sus textos ya cargados. */
export const mastersTextsResolver: ResolveFn<boolean> = async () => {
  await loadMastersTexts(inject(TranslocoService));
  return true;
};

/** Traduce una clave del feature (sin el prefijo `masters.`). */
export function mastersText(
  transloco: TranslocoService,
  key: string,
  params?: Record<string, unknown>,
): string {
  return transloco.translate(`${MASTERS_I18N_SCOPE}.${key}`, params);
}
