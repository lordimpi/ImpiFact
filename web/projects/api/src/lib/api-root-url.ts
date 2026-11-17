/*
 * Punto de enganche para la URL base del API. Es código escrito a mano (lo generado vive en `../generated` y no se edita).
 *
 * El cliente generado arma cada URL con `ApiConfiguration.rootUrl` (vacío por defecto: rutas relativas al panel).
 * `core` lee la configuración en tiempo de ejecución al arrancar y fija aquí la URL base, sin recompilar el panel
 * para cada ambiente.
 */
import { EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';

import { ApiConfiguration } from '../generated/api-configuration';

/**
 * Fija `ApiConfiguration.rootUrl` mientras arranca la aplicación, antes de la primera llamada al API.
 *
 * `rootUrl` corre en un contexto de inyección, así que puede usar `inject()`; puede devolver una promesa (por ejemplo,
 * la lectura del archivo de configuración) y el arranque espera a que se resuelva. La URL no lleva barra final.
 */
export function provideApiRootUrl(rootUrl: () => string | Promise<string>): EnvironmentProviders {
  return provideAppInitializer(async () => {
    const configuration = inject(ApiConfiguration);
    configuration.rootUrl = await rootUrl();
  });
}
