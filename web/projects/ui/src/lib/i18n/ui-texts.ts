import { InjectionToken } from '@angular/core';

/**
 * Textos propios de los componentes de ui que no llegan por sus entradas, ya traducidos. La aplicación los da con
 * Transloco (`app.config.ts`); sin proveedor queda un texto de respaldo para las pruebas aisladas.
 */
export interface UiTexts {
  /** Nombre accesible del botón de ayuda de un campo, a partir de la etiqueta del campo. */
  fieldHelp(label: string): string;
  /** Nombre accesible del botón que muestra u oculta una contraseña (es un conmutador: `aria-pressed`). */
  showPassword: string;
  /** Accessible name of the paginator button that jumps to the first page. */
  firstPage: string;
  /** Accessible name of the paginator button that jumps to the last page. */
  lastPage: string;
  /** Accessible name of the page breadcrumb navigation (`ui-page-title [breadcrumb]`). */
  breadcrumb: string;
  /**
   * Texts of the Optimus UI components ui wraps (select, date picker, file upload), in Optimus' translation shape.
   * Without it the components use the Spanish fallback of `field/optimus-texts.ts`.
   */
  optimus?: UiOptimusTexts;
  /** `ui-file-upload`: the chosen file is larger than allowed (`size` and `max` already formatted, «2 KB»). */
  fileTooLarge?(name: string, size: string, max: string): string;
  /** `ui-file-upload`: the chosen file is not of an accepted type (`accept` already listed, «.p12, .pfx»). */
  fileWrongType?(name: string, accept: string): string;
}

/**
 * Optimus UI texts (a subset of its `Translation`): calendar names and buttons, empty option lists and the file
 * chooser. Kept as plain data so this file does not import Optimus UI (it is part of the initial bundle).
 */
export interface UiOptimusTexts {
  dayNames: string[];
  dayNamesShort: string[];
  dayNamesMin: string[];
  monthNames: string[];
  monthNamesShort: string[];
  /** 0 = Sunday. */
  firstDayOfWeek: number;
  /** Display format of the date picker (`dd/mm/yy` is day/month/four-digit year). */
  dateFormat: string;
  today: string;
  clear: string;
  weekHeader: string;
  chooseYear: string;
  chooseMonth: string;
  chooseDate: string;
  prevDecade: string;
  nextDecade: string;
  prevYear: string;
  nextYear: string;
  prevMonth: string;
  nextMonth: string;
  emptyMessage: string;
  emptyFilterMessage: string;
  choose: string;
  noFileChosenMessage: string;
  /** `{0}` is the number of files. */
  fileChosenMessage: string;
  fileSizeTypes: string[];
  aria: { listLabel: string; browseFiles: string; close: string };
}

export const UI_TEXTS = new InjectionToken<UiTexts>('UI_TEXTS', {
  providedIn: 'root',
  factory: () => ({
    fieldHelp: (label) => `Ayuda: ${label}`,
    showPassword: 'Mostrar la contraseña',
    firstPage: 'Primera página',
    lastPage: 'Última página',
    breadcrumb: 'Ruta de navegación',
  }),
});
