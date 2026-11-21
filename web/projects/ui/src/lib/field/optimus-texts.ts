import { inject } from '@angular/core';
import { Optimus } from '@openng/optimus-ui/config';
import { UI_TEXTS, UiOptimusTexts } from '../i18n/ui-texts';

/**
 * Spanish (es-CO) fallback of the Optimus UI texts, for isolated tests without the Transloco provider of the app. The
 * app gives the same texts from `public/i18n/es-CO.json` (`ui.optimus`).
 */
export const OPTIMUS_TEXTS_ES: UiOptimusTexts = {
  dayNames: ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'],
  dayNamesShort: ['dom', 'lun', 'mar', 'mié', 'jue', 'vie', 'sáb'],
  dayNamesMin: ['do', 'lu', 'ma', 'mi', 'ju', 'vi', 'sá'],
  monthNames: [
    'enero',
    'febrero',
    'marzo',
    'abril',
    'mayo',
    'junio',
    'julio',
    'agosto',
    'septiembre',
    'octubre',
    'noviembre',
    'diciembre',
  ],
  monthNamesShort: [
    'ene',
    'feb',
    'mar',
    'abr',
    'may',
    'jun',
    'jul',
    'ago',
    'sep',
    'oct',
    'nov',
    'dic',
  ],
  firstDayOfWeek: 0,
  dateFormat: 'dd/mm/yy',
  today: 'Hoy',
  clear: 'Borrar',
  weekHeader: 'Sem',
  chooseYear: 'Elegir el año',
  chooseMonth: 'Elegir el mes',
  chooseDate: 'Elegir la fecha',
  prevDecade: 'Década anterior',
  nextDecade: 'Década siguiente',
  prevYear: 'Año anterior',
  nextYear: 'Año siguiente',
  prevMonth: 'Mes anterior',
  nextMonth: 'Mes siguiente',
  emptyMessage: 'No hay opciones',
  emptyFilterMessage: 'Ninguna opción coincide',
  choose: 'Seleccionar archivo',
  noFileChosenMessage: 'Ningún archivo seleccionado',
  fileChosenMessage: '{0} archivos',
  fileSizeTypes: ['B', 'KB', 'MB', 'GB', 'TB', 'PB', 'EB', 'ZB', 'YB'],
  aria: { listLabel: 'Opciones', browseFiles: 'Buscar archivos', close: 'Cerrar' },
};

const applied = new WeakSet<Optimus>();

/**
 * Gives Optimus UI the texts of `UI_TEXTS` (Spanish month and day names, empty lists, file chooser) the first time a
 * wrapper of ui is created. Optimus keeps its translation globally and its components follow later changes. The
 * texts are applied here and not when the theme starts, because Transloco loads them after the theme. The `aria`
 * group is merged so labels set elsewhere (for example by the paginator) are kept. Call it in an injection context.
 */
export function applyOptimusTexts(): void {
  const optimus = inject(Optimus);
  if (applied.has(optimus)) {
    return;
  }
  const texts = inject(UI_TEXTS).optimus ?? OPTIMUS_TEXTS_ES;
  // Transloco answers the key itself when the texts are not loaded yet: keep Optimus as it is and retry later.
  if (!Array.isArray(texts.monthNames)) {
    return;
  }
  applied.add(optimus);
  optimus.setTranslation({ ...texts, aria: { ...optimus.translation.aria, ...texts.aria } });
}
