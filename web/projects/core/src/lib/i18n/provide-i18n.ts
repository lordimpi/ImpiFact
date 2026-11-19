import { HttpClient } from '@angular/common/http';
import { EnvironmentProviders, Injectable, inject, isDevMode } from '@angular/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { Observable } from 'rxjs';

/** Idioma base del panel: español de Colombia. */
export const DEFAULT_LANG = 'es-CO';

/**
 * Carga los textos en tiempo de ejecución desde `/i18n/<idioma>.json`, publicado por la aplicación.
 * Pasa por HttpClient, así queda sujeto a los mismos interceptores que el resto de llamadas.
 */
@Injectable({ providedIn: 'root' })
export class HttpTranslationLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    return this.http.get<Translation>(`i18n/${lang}.json`);
  }
}

/**
 * i18n (internacionalización) del panel con Transloco. Todo texto visible sale de estas traducciones.
 * Requiere `provideHttpClient()` en la aplicación.
 */
export function provideI18n(): EnvironmentProviders[] {
  return provideTransloco({
    config: {
      availableLangs: [DEFAULT_LANG],
      defaultLang: DEFAULT_LANG,
      fallbackLang: DEFAULT_LANG,
      reRenderOnLangChange: false,
      prodMode: !isDevMode(),
      missingHandler: { logMissingKey: true, useFallbackTranslation: false, allowEmpty: false },
    },
    loader: HttpTranslationLoader,
  });
}
