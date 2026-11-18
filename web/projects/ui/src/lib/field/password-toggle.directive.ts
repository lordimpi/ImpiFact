import {
  DestroyRef,
  Directive,
  ElementRef,
  Renderer2,
  afterNextRender,
  effect,
  inject,
  signal,
} from '@angular/core';
import { UI_TEXTS } from '../i18n/ui-texts';

/** Ojo (contraseña oculta) y ojo tachado (visible); trazos con `currentColor` para seguir el tema. */
const EYE =
  '<svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12z"/><circle cx="12" cy="12" r="3"/></svg>';
const EYE_OFF =
  '<svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 19c-7 0-11-7-11-7a18.45 18.45 0 0 1 5.06-5.94"/><path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 7 11 7a18.5 18.5 0 0 1-2.16 3.19"/><path d="M14.12 14.12a3 3 0 1 1-4.24-4.24"/><path d="M1 1l22 22"/></svg>';

const HIDDEN_NAME = '<span class="ui-password__name"></span>';

/**
 * Botón con un ojo al final de un campo de contraseña para mostrarla u ocultarla. El campo sigue siendo el mismo
 * `<input type="password">` (los gestores de contraseñas lo reconocen y `[formField]` no cambia); la directiva lo
 * envuelve con el botón al pintarse. El botón es un conmutador (`aria-pressed`) con un nombre fijo, y al usarlo
 * devuelve el foco al campo.
 */
@Directive({
  selector: 'input[uiPasswordToggle]',
  host: { '[type]': 'visible() ? "text" : "password"' },
})
export class PasswordToggleDirective {
  protected readonly visible = signal(false);

  constructor() {
    const input = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
    const renderer = inject(Renderer2);
    const label = inject(UI_TEXTS).showPassword;
    const button: HTMLButtonElement = renderer.createElement('button');
    renderer.setAttribute(button, 'type', 'button');
    renderer.setAttribute(button, 'title', label);
    renderer.addClass(button, 'ui-password__toggle');
    const unlisten = renderer.listen(button, 'click', () => {
      this.visible.update((visible) => !visible);
      input.focus();
    });

    effect(() => {
      const visible = this.visible();
      renderer.setAttribute(button, 'aria-pressed', String(visible));
      // El nombre va como texto oculto y no en aria-label: así `getByLabel('Contraseña')` sigue encontrando solo el campo.
      renderer.setProperty(button, 'innerHTML', (visible ? EYE_OFF : EYE) + HIDDEN_NAME);
      (button.lastElementChild as HTMLElement).textContent = label;
    });

    let wrapper: HTMLElement | null = null;
    afterNextRender(() => {
      const parent = input.parentNode;
      if (!parent) return;
      wrapper = renderer.createElement('span');
      renderer.addClass(wrapper, 'ui-password');
      renderer.insertBefore(parent, wrapper, input);
      renderer.appendChild(wrapper, input);
      renderer.appendChild(wrapper, button);
    });

    inject(DestroyRef).onDestroy(() => {
      unlisten();
      wrapper?.remove();
    });
  }
}
