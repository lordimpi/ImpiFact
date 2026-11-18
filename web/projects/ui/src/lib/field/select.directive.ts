import { Directive, inject } from '@angular/core';
import { FieldComponent } from './field.component';

/**
 * Lista desplegable nativa (`<select>`) dentro de `ui-field`: la enlaza con su etiqueta, ayuda y error igual que
 * `uiInput`. Es nativa a propósito: accesible con teclado y lector de pantalla sin cargar más código de Optimus UI.
 * Los estilos están en `ui-field` (clase `ui-select`).
 */
@Directive({
  selector: 'select[uiSelect]',
  host: {
    class: 'ui-field__control ui-select',
    '[id]': 'field?.controlId',
    '[attr.aria-describedby]': 'field?.describedBy() ?? null',
    '[attr.aria-invalid]': 'field?.invalid() ? "true" : null',
    '[attr.aria-required]': 'field?.required() ? "true" : null',
  },
})
export class SelectDirective {
  protected readonly field = inject(FieldComponent, { optional: true });
}
