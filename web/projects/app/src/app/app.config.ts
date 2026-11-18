import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { coreInterceptors, provideCore, provideI18n } from '@impifact/core';
import { UI_TEXTS, provideUiTheme } from '@impifact/ui';
import { TranslocoService } from '@jsverse/transloco';
import { routes } from './app.routes';

// Angular 22 no usa Zone.js por defecto (zoneless): la detección de cambios la disparan los signals.
// provideCore lee config.json (URL del Api), recupera la sesión y enciende el cierre por inactividad.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors(coreInterceptors)),
    provideRouter(routes, withComponentInputBinding()),
    provideI18n(),
    provideCore(),
    provideUiTheme(),
    // Textos propios de los componentes de ui (por ejemplo, el botón de ayuda de cada campo).
    {
      provide: UI_TEXTS,
      useFactory: () => {
        const transloco = inject(TranslocoService);
        return { fieldHelp: (label: string) => transloco.translate('ui.fieldHelp', { label }) };
      },
    },
  ],
};
