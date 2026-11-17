import {
  APP_BOOTSTRAP_LISTENER,
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
} from '@angular/core';
import { provideApiRootUrl } from '@impifact/api';
import { RuntimeConfigService } from './config/runtime-config';
import { InactivityMonitor } from './inactivity/inactivity-monitor';
import { CORE_ROUTE_PATHS, CoreRoutePaths, DEFAULT_CORE_ROUTE_PATHS } from './session/route-paths';
import { SessionStore } from './session/session.store';

export interface CoreOptions {
  /** Rutas de login, inicio y selección de empresa, si difieren de la sección 7 de la arquitectura. */
  routePaths?: Partial<CoreRoutePaths>;
}

/**
 * Arranque de core. Va en `app.config.ts` junto a `provideHttpClient(withFetch(), withInterceptors(coreInterceptors))`
 * y `provideI18n()`:
 *
 * 1. Mientras arranca la aplicación lee `config.json` y fija la URL del Api con `provideApiRootUrl`.
 * 2. Ya arrancada (cuando la URL está puesta), recupera la sesión con una renovación; sin cookie queda anónima,
 *    sin errores. Los guards esperan esa recuperación.
 * 3. Enciende el cierre por inactividad, que se activa solo cuando hay sesión.
 */
export function provideCore(options: CoreOptions = {}): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideApiRootUrl(() =>
      inject(RuntimeConfigService)
        .load()
        .then((config) => config.apiBaseUrl),
    ),
    { provide: CORE_ROUTE_PATHS, useValue: { ...DEFAULT_CORE_ROUTE_PATHS, ...options.routePaths } },
    {
      provide: APP_BOOTSTRAP_LISTENER,
      multi: true,
      useFactory: () => {
        const session = inject(SessionStore);
        return () => void session.restore();
      },
    },
    provideEnvironmentInitializer(() => inject(InactivityMonitor)),
  ]);
}
