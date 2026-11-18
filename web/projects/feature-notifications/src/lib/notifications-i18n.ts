import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/*
 * Textos del feature con un *scope* (ámbito) de Transloco: viven en `src/i18n/es-CO.json` de esta librería, la
 * aplicación los publica en `/i18n/notifications/es-CO.json` (ver `angular.json`) y se piden solo al entrar al
 * feature o al abrir la lista de la campana. En las plantillas llevan el prefijo `notifications.`.
 */
export const NOTIFICATIONS_I18N_SCOPE = 'notifications';

/** Va en los `providers` de cada página o componente del feature. */
export const provideNotificationsI18n = () => provideTranslocoScope(NOTIFICATIONS_I18N_SCOPE);

/** Carga los textos del feature en el idioma activo; nunca falla (sin red quedan las claves). */
export async function loadNotificationsTexts(transloco: TranslocoService): Promise<void> {
  try {
    await firstValueFrom(
      transloco.load(`${NOTIFICATIONS_I18N_SCOPE}/${transloco.getActiveLang()}`),
    );
  } catch {
    // Sin los textos la página se muestra igual; el error de red ya lo avisa core.
  }
}

/** *Resolver* de las rutas del feature: la página se abre con sus textos ya cargados. */
export const notificationsTextsResolver: ResolveFn<boolean> = async () => {
  await loadNotificationsTexts(inject(TranslocoService));
  return true;
};

/** Traduce una clave del feature (sin el prefijo `notifications.`). */
export function notificationsText(
  transloco: TranslocoService,
  key: string,
  params?: Record<string, unknown>,
): string {
  return transloco.translate(`${NOTIFICATIONS_I18N_SCOPE}.${key}`, params);
}

const DATE_TIME = new Intl.DateTimeFormat('es-CO', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'America/Bogota',
});

/** Fecha y hora de un aviso en la hora de Colombia; vacía si no llegó. */
export function formatNoticeDate(value: string | null | undefined): string {
  if (!value) {
    return '';
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : DATE_TIME.format(date);
}
