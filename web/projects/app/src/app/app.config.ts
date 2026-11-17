import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { coreInterceptors, provideCore, provideI18n } from '@impifact/core';
import { provideUiTheme } from '@impifact/ui';
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
  ],
};
