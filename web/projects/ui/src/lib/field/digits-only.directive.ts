import { Directive, ElementRef, booleanAttribute, inject, input } from '@angular/core';

const NON_DIGITS = /\D+/g;
const HAS_NON_DIGIT = /\D/;

/**
 * Campo de solo dígitos (NIT, cédula, teléfono): pide el teclado numérico, no deja escribir otra cosa y de un texto
 * pegado («900.123.456-8») conserva solo los dígitos. Si el valor llega de otra forma (autocompletar del navegador),
 * lo limpia y vuelve a avisar al formulario con otro evento `input`. `[uiDigitsOnly]="false"` lo apaga, por ejemplo
 * cuando el tipo de documento admite letras (pasaporte).
 */
@Directive({
  selector: 'input[uiDigitsOnly]',
  host: {
    '[attr.inputmode]': 'enabled() ? "numeric" : null',
    '(beforeinput)': 'onBeforeInput($event)',
    '(input)': 'onInput()',
  },
})
export class DigitsOnlyDirective {
  readonly enabled = input(true, { alias: 'uiDigitsOnly', transform: booleanAttribute });

  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;

  protected onBeforeInput(event: InputEvent): void {
    const data = event.data ?? event.dataTransfer?.getData('text/plain') ?? null;
    if (
      !this.enabled() ||
      !event.inputType.startsWith('insert') ||
      !data ||
      !HAS_NON_DIGIT.test(data)
    ) {
      return;
    }
    event.preventDefault();
    const digits = data.replace(NON_DIGITS, '');
    if (digits === '') {
      return;
    }
    const start = this.element.selectionStart ?? this.element.value.length;
    const end = this.element.selectionEnd ?? start;
    this.element.setRangeText(digits, start, end, 'end');
    this.notify();
  }

  protected onInput(): void {
    const value = this.element.value;
    if (!this.enabled() || !HAS_NON_DIGIT.test(value)) {
      return;
    }
    this.element.value = value.replace(NON_DIGITS, '');
    this.notify();
  }

  /** Otro `input` con el valor limpio: el formulario (`[formField]`) lee el valor en ese evento. */
  private notify(): void {
    this.element.dispatchEvent(new Event('input', { bubbles: true }));
  }
}
