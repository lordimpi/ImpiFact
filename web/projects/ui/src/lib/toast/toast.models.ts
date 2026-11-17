/** Tipo de aviso: define el color y cómo lo anuncia el lector de pantalla (los errores, de inmediato). */
export type UiToastKind = 'info' | 'success' | 'warning' | 'error';

/** Aviso que dibuja `ui-toast-region`. `text` y `reference` llegan ya listos para mostrar. */
export interface UiToast {
  id: number;
  kind: UiToastKind;
  text: string;
  /** Código para soporte (por ejemplo, el identificador de correlación); se muestra con botón de copiar. */
  reference?: string | null;
}

/** Textos de la región de avisos, ya traducidos. */
export interface UiToastLabels {
  region: string;
  close: string;
  copy: string;
  reference: string;
}
