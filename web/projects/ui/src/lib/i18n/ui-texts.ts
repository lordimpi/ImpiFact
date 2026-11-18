import { InjectionToken } from '@angular/core';

/**
 * Textos propios de los componentes de ui que no llegan por sus entradas, ya traducidos. La aplicación los da con
 * Transloco (`app.config.ts`); sin proveedor queda un texto de respaldo para las pruebas aisladas.
 */
export interface UiTexts {
  /** Nombre accesible del botón de ayuda de un campo, a partir de la etiqueta del campo. */
  fieldHelp(label: string): string;
}

export const UI_TEXTS = new InjectionToken<UiTexts>('UI_TEXTS', {
  providedIn: 'root',
  factory: () => ({ fieldHelp: (label) => `Ayuda: ${label}` }),
});
