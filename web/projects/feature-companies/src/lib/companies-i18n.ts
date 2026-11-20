import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { UiTableLabels } from '@impifact/ui';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/*
 * Textos del feature con un *scope* (ámbito) de Transloco: viven en `src/i18n/es-CO.json` de esta librería, la
 * aplicación los publica en `/i18n/companies/es-CO.json` (ver `angular.json`) y se piden solo al entrar al feature.
 * En las plantillas se usan con el prefijo `companies.` (`'companies.data.title' | transloco`).
 */
export const COMPANIES_I18N_SCOPE = 'companies';

/** Va en los `providers` de cada página o componente del feature. */
export const provideCompaniesI18n = () => provideTranslocoScope(COMPANIES_I18N_SCOPE);

/** Carga los textos del feature en el idioma activo; nunca falla (sin red quedan las claves). */
export async function loadCompaniesTexts(transloco: TranslocoService): Promise<void> {
  try {
    await firstValueFrom(transloco.load(`${COMPANIES_I18N_SCOPE}/${transloco.getActiveLang()}`));
  } catch {
    // Sin los textos la página se muestra igual; el error de red ya lo avisa core.
  }
}

/** *Resolver* de las rutas del feature: la página se abre con sus textos ya cargados. */
export const companiesTextsResolver: ResolveFn<boolean> = async () => {
  await loadCompaniesTexts(inject(TranslocoService));
  return true;
};

/** Traduce una clave del feature (sin el prefijo `companies.`). */
export function companiesText(
  transloco: TranslocoService,
  key: string,
  params?: Record<string, unknown>,
): string {
  return transloco.translate(`${COMPANIES_I18N_SCOPE}.${key}`, params);
}

/**
 * Texts of the feature's small tables (`ui-table`): they list rows already loaded by the page, so the loading,
 * empty and error states never show, but the table needs them.
 */
export function companiesTableLabels(transloco: TranslocoService): UiTableLabels {
  return {
    loading: companiesText(transloco, 'common.loading'),
    emptyTitle: companiesText(transloco, 'resolutions.savedEmpty'),
    errorTitle: companiesText(transloco, 'resolutions.loadError'),
    retry: companiesText(transloco, 'common.retry'),
    paginator: {
      navigation: transloco.translate('ui.paginator.navigation'),
      previous: transloco.translate('ui.paginator.previous'),
      next: transloco.translate('ui.paginator.next'),
      page: transloco.translate('ui.paginator.page'),
    },
  };
}
