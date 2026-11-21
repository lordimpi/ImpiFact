import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  Provider,
  inject,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import {
  UNSAVED_CHANGES_CONFIRM,
  coreInterceptors,
  provideCore,
  provideI18n,
} from '@impifact/core';
import { UI_TEXTS, UiConfirm, provideUiTheme } from '@impifact/ui';
import { TranslocoService } from '@jsverse/transloco';
import { routes } from './app.routes';

/**
 * Leaving a page with unsaved changes asks with the Optimus UI confirm dialog of ui (`UiConfirm`) instead of the
 * browser's `window.confirm`, core's default. The dialog loads the first time the guard asks, so it stays out of the
 * initial bundle. «Seguir editando» is the focused, safe choice; Escape also stays.
 */
export const unsavedChangesDialog: Provider = {
  provide: UNSAVED_CHANGES_CONFIRM,
  useFactory: () => {
    const confirm = inject(UiConfirm);
    const transloco = inject(TranslocoService);
    return (message: string) =>
      confirm.ask({
        header: transloco.translate('core.unsavedChangesTitle'),
        message,
        acceptLabel: transloco.translate('core.unsavedChangesLeave'),
        rejectLabel: transloco.translate('core.unsavedChangesStay'),
      });
  },
};

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
        return {
          fieldHelp: (label: string) => transloco.translate('ui.fieldHelp', { label }),
          get showPassword() {
            return transloco.translate('ui.showPassword');
          },
          get firstPage() {
            return transloco.translate('ui.paginator.first');
          },
          get lastPage() {
            return transloco.translate('ui.paginator.last');
          },
          get breadcrumb() {
            return transloco.translate('ui.breadcrumb');
          },
          // Spanish texts of the Optimus select, date picker and file upload (month names, empty lists…).
          get optimus() {
            return transloco.translateObject('ui.optimus');
          },
          fileTooLarge: (name: string, size: string, max: string) =>
            transloco.translate('ui.fileUpload.tooLarge', { name, size, max }),
          fileWrongType: (name: string, accept: string) =>
            transloco.translate('ui.fileUpload.wrongType', { name, accept }),
        };
      },
    },
    unsavedChangesDialog,
  ],
};
