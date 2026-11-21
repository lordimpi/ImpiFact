import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { UiToast, UiToastLabels } from './toast.models';

/**
 * Región de avisos (*toasts*) del panel, fija arriba a la derecha, bajo la barra superior. Cada aviso lleva un ícono de
 * su tipo y se cierra con un botón de ícono (X) cuyo nombre accesible es `labels.close`. Componente de presentación: recibe los avisos y los
 * textos ya traducidos y emite `dismiss` y `copyReference`; quien lo usa decide cuándo abrir y cerrar cada aviso.
 *
 * Accesibilidad: dos regiones `aria-live` que existen siempre (un lector de pantalla solo anuncia cambios dentro de
 * una región que ya estaba en la página): los errores en `assertive` (se anuncian de inmediato) y el resto en
 * `polite` (al terminar lo que se esté leyendo).
 *
 * Usa botones nativos y no `ui-button`: la región está en el shell, y Optimus UI en el bundle inicial costaría
 * unos 90 kB solo por estos dos botones.
 */
@Component({
  selector: 'ui-toast-region',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ui-toasts" [attr.aria-label]="labels().region">
      <ul class="ui-toasts__list" aria-live="polite" aria-relevant="additions text">
        @for (toast of polite(); track toast.id) {
          <ng-container *ngTemplateOutlet="item; context: { $implicit: toast }" />
        }
      </ul>
      <ul class="ui-toasts__list" aria-live="assertive" aria-relevant="additions text">
        @for (toast of assertive(); track toast.id) {
          <ng-container *ngTemplateOutlet="item; context: { $implicit: toast }" />
        }
      </ul>
    </section>

    <ng-template #item let-toast>
      <li [class]="'ui-toast ui-toast--' + toast.kind">
        <svg class="ui-toast__icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          @switch (toast.kind) {
            @case ('success') {
              <circle cx="12" cy="12" r="9" />
              <path d="M8 12.5l2.5 2.5L16 9.5" />
            }
            @case ('error') {
              <circle cx="12" cy="12" r="9" />
              <path d="M9 9l6 6M15 9l-6 6" />
            }
            @case ('warning') {
              <path d="M12 3.5l9.5 16.5h-19z" />
              <path d="M12 10v4M12 17h.01" />
            }
            @default {
              <circle cx="12" cy="12" r="9" />
              <path d="M12 11v5M12 8h.01" />
            }
          }
        </svg>
        <div class="ui-toast__body">
          <p class="ui-toast__text">{{ toast.text }}</p>
          @if (toast.reference) {
            <p class="ui-toast__reference">
              {{ labels().reference }}: <code>{{ toast.reference }}</code>
            </p>
            <button
              type="button"
              class="ui-toast__action"
              (click)="copyReference.emit(toast.reference)"
            >
              {{ labels().copy }}
            </button>
          }
        </div>
        <button
          type="button"
          class="ui-toast__close"
          [attr.aria-label]="labels().close"
          (click)="dismiss.emit(toast.id)"
        >
          <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <path d="M6 6l12 12M18 6L6 18" />
          </svg>
        </button>
      </li>
    </ng-template>
  `,
  styles: `
    .ui-toasts {
      position: fixed;
      /* Top right, just under the top bar (it does not cover the user menu). */
      inset-block-start: calc(var(--if-topbar-height) + var(--if-space-2));
      inset-inline-end: var(--if-space-3);
      z-index: 1100;
      inline-size: min(24rem, calc(100vw - 2 * var(--if-space-3)));
      pointer-events: none;
    }
    .ui-toasts__list {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .ui-toasts__list:not(:empty) + .ui-toasts__list:not(:empty) {
      margin-block-start: var(--if-space-2);
    }
    .ui-toast {
      display: flex;
      align-items: flex-start;
      gap: var(--if-space-2);
      pointer-events: auto;
      padding: var(--if-space-3);
      border: 1px solid var(--if-color-border);
      border-inline-start: 4px solid var(--if-color-primary);
      border-radius: var(--if-radius-md);
      background: var(--if-color-bg);
      color: var(--if-color-text);
      font-size: var(--if-font-size-sm);
      box-shadow: var(--if-shadow-overlay);
    }
    .ui-toast--success {
      border-inline-start-color: var(--if-color-success);
      --ui-toast-tone: var(--if-color-success);
    }
    .ui-toast--warning {
      border-inline-start-color: var(--if-color-warning);
      --ui-toast-tone: var(--if-color-warning);
    }
    .ui-toast--error {
      border-inline-start-color: var(--if-color-danger);
      --ui-toast-tone: var(--if-color-danger);
    }
    .ui-toast svg {
      fill: none;
      stroke: currentColor;
      stroke-width: 2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .ui-toast__icon {
      flex: none;
      inline-size: 1.25rem;
      block-size: 1.25rem;
      color: var(--ui-toast-tone, var(--if-color-primary));
    }
    .ui-toast__body {
      flex: 1;
      min-inline-size: 0;
    }
    .ui-toast__text,
    .ui-toast__reference {
      margin: 0 0 var(--if-space-1);
    }
    .ui-toast__text {
      font-weight: var(--if-font-weight-medium);
    }
    .ui-toast__reference {
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .ui-toast__reference code {
      font-family: var(--if-font-family-mono);
      user-select: all;
    }
    .ui-toast__action {
      min-block-size: 2rem;
      margin-inline-start: calc(-1 * var(--if-space-2));
      padding: var(--if-space-1) var(--if-space-2);
      border: 0;
      border-radius: var(--if-radius-sm);
      background: transparent;
      color: var(--if-color-primary);
      font: inherit;
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-medium);
      cursor: pointer;
    }
    .ui-toast__action:hover {
      background: var(--if-color-primary-subtle);
    }
    /* 24px hit area (WCAG 2.5.8) around a small X. */
    .ui-toast__close {
      display: inline-grid;
      flex: none;
      place-items: center;
      inline-size: 1.75rem;
      block-size: 1.75rem;
      margin: calc(-1 * var(--if-space-1)) calc(-1 * var(--if-space-1)) 0 0;
      padding: 0;
      border: 0;
      border-radius: var(--if-radius-sm);
      background: transparent;
      color: var(--if-color-text-muted);
      cursor: pointer;
    }
    .ui-toast__close svg {
      inline-size: 1rem;
      block-size: 1rem;
    }
    .ui-toast__close:hover {
      color: var(--if-color-text);
      background: var(--if-color-surface);
    }
    .ui-toast__action:focus-visible,
    .ui-toast__close:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: var(--if-focus-ring-offset);
    }
  `,
})
export class ToastRegionComponent {
  readonly toasts = input.required<readonly UiToast[]>();
  readonly labels = input.required<UiToastLabels>();

  /** Se pidió cerrar el aviso con este `id`. */
  readonly dismiss = output<number>();
  /** Se pidió copiar este código de soporte. */
  readonly copyReference = output<string>();

  protected readonly polite = computed(() =>
    this.toasts().filter((toast) => toast.kind !== 'error'),
  );
  protected readonly assertive = computed(() =>
    this.toasts().filter((toast) => toast.kind === 'error'),
  );
}
