import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/*
 * Textos del feature con un *scope* (ámbito) de Transloco: viven en `src/i18n/es-CO.json` de esta librería, la
 * aplicación los publica en `/i18n/dashboard/es-CO.json` (ver `angular.json`) y se piden solo al abrir el tablero.
 * En las plantillas llevan el prefijo `dashboard.`.
 */
export const DASHBOARD_I18N_SCOPE = 'dashboard';

export const provideDashboardI18n = () => provideTranslocoScope(DASHBOARD_I18N_SCOPE);

/** *Resolver* de la ruta: el tablero se abre con sus textos ya cargados; nunca falla. */
export const dashboardTextsResolver: ResolveFn<boolean> = async () => {
  const transloco = inject(TranslocoService);
  try {
    await firstValueFrom(transloco.load(`${DASHBOARD_I18N_SCOPE}/${transloco.getActiveLang()}`));
  } catch {
    // Sin los textos la página se muestra igual; el error de red ya lo avisa core.
  }
  return true;
};

/** Traduce una clave del feature (sin el prefijo `dashboard.`). */
export function dashboardText(
  transloco: TranslocoService,
  key: string,
  params?: Record<string, unknown>,
): string {
  return transloco.translate(`${DASHBOARD_I18N_SCOPE}.${key}`, params);
}
