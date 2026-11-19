import { Directive } from '@angular/core';

/** Teclas que un `<input type="number">` acepta pero que no forman una cantidad o un precio («-5», «1e5»). */
const BLOCKED_KEYS = new Set(['-', '+', 'e', 'E']);
const NON_NEGATIVE_NUMBER = /^\d*(?:[.,]\d*)?$/;

/**
 * Campo numérico que no admite negativos ni notación científica (cantidades y precios): el navegador deja escribir
 * «-», «+» y «e» en un `type="number"`, así que se bloquean al teclear y al pegar. Pide el teclado decimal y fija
 * `min="0"`; el formulario sigue validando el valor (por ejemplo, que la cantidad sea mayor que cero).
 */
@Directive({
  selector: 'input[uiNonNegative]',
  host: {
    min: '0',
    inputmode: 'decimal',
    '(keydown)': 'onKeydown($event)',
    '(paste)': 'onPaste($event)',
  },
})
export class NonNegativeDirective {
  protected onKeydown(event: KeyboardEvent): void {
    if (!event.ctrlKey && !event.metaKey && BLOCKED_KEYS.has(event.key)) {
      event.preventDefault();
    }
  }

  protected onPaste(event: ClipboardEvent): void {
    const text = event.clipboardData?.getData('text/plain').trim() ?? '';
    if (!NON_NEGATIVE_NUMBER.test(text)) {
      event.preventDefault();
    }
  }
}
