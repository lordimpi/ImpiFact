import {
  EnvironmentInjector,
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
  runInInjectionContext,
} from '@angular/core';

/** Opciones del tema de ui. */
export interface UiThemeOptions {
  /** Nonce de la CSP (política de seguridad de contenido) para las etiquetas <style> que inyecta Optimus UI. */
  nonce?: string;
}

/**
 * Configura Optimus UI con el preset de ImpiFact. Se registra una vez en la configuración de la aplicación.
 *
 * El preset y el motor de estilos de Optimus UI se cargan con un `import()` durante el arranque (en paralelo con
 * `config.json`), no en el bundle inicial: la aplicación espera a que el tema quede aplicado antes de pintar la
 * primera página, así ningún componente se ve sin estilos. Ver `optimus-theme.ts`.
 */
export function provideUiTheme(options: UiThemeOptions = {}): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      const injector = inject(EnvironmentInjector);
      return import('./optimus-theme').then(({ applyUiTheme }) =>
        runInInjectionContext(injector, () => applyUiTheme(options)),
      );
    }),
  ]);
}
