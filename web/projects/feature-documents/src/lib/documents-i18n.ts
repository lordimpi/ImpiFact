import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/*
 * Textos del feature con un *scope* (ámbito) de Transloco: viven en `src/i18n/es-CO.json` de esta librería, la
 * aplicación los publica en `/i18n/documents/es-CO.json` (ver `angular.json`) y se piden solo al entrar al feature.
 * En las plantillas se usan con el prefijo `documents.` (`'documents.list.title' | transloco`).
 */
export const DOCUMENTS_I18N_SCOPE = 'documents';

/** Va en los `providers` de cada página o componente del feature. */
export const provideDocumentsI18n = () => provideTranslocoScope(DOCUMENTS_I18N_SCOPE);

/**
 * Carga los textos del feature y los comunes de la aplicación (`ui.*`, que las páginas leen con `translate` para
 * armar los textos de la tabla) en el idioma activo; nunca falla (sin red quedan las claves).
 */
export async function loadDocumentsTexts(transloco: TranslocoService): Promise<void> {
  const lang = transloco.getActiveLang();
  try {
    await Promise.all([
      firstValueFrom(transloco.load(lang)),
      firstValueFrom(transloco.load(`${DOCUMENTS_I18N_SCOPE}/${lang}`)),
    ]);
  } catch {
    // Sin los textos la página se muestra igual; el error de red ya lo avisa core.
  }
}

/** *Resolver* de las rutas del feature: la página se abre con sus textos ya cargados. */
export const documentsTextsResolver: ResolveFn<boolean> = async () => {
  await loadDocumentsTexts(inject(TranslocoService));
  return true;
};
