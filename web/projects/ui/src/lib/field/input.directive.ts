import { Directive, inject } from '@angular/core';
import { InputText } from '@openng/optimus-ui/inputtext';
import { FieldComponent } from './field.component';

/**
 * Control de texto dentro de `ui-field`: aplica el estilo de Optimus UI y enlaza el control con su
 * etiqueta, ayuda y error. Funciona con formularios reactivos y con `[value]` porque el control es nativo.
 */
@Directive({
  selector: 'input[uiInput], textarea[uiInput]',
  hostDirectives: [InputText],
  host: {
    class: 'ui-field__control',
    '[id]': 'field?.controlId',
    '[attr.aria-describedby]': 'field?.describedBy() ?? null',
    '[attr.aria-invalid]': 'field?.invalid() ? "true" : null',
    '[attr.aria-required]': 'field?.required() ? "true" : null',
  },
})
export class InputDirective {
  protected readonly field = inject(FieldComponent, { optional: true });
}
