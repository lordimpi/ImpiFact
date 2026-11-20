import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { UiToast, UiToastLabels } from './toast.models';

/**
 * Región de avisos (*toasts*) del panel, fija en una esquina. Componente de presentación: recibe los avisos y los
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
        <p class="ui-toast__text">{{ toast.text }}</p>
        @if (toast.reference) {
          <p class="ui-toast__reference">
            {{ labels().reference }}: <code>{{ toast.reference }}</code>
          </p>
        }
        <div class="ui-toast__actions">
          @if (toast.reference) {
            <button
              type="button"
              class="ui-toast__action"
              (click)="copyReference.emit(toast.reference)"
            >
              {{ labels().copy }}
            </button>
          }
          <button type="button" class="ui-toast__action" (click)="dismiss.emit(toast.id)">
            {{ labels().close }}
          </button>
        </div>
      </li>
    </ng-template>
  `,
  styles: `
    .ui-toasts {
      position: fixed;
      inset-block-end: var(--if-space-3);
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
    }
    .ui-toast--warning {
      border-inline-start-color: var(--if-color-warning);
    }
    .ui-toast--error {
      border-inline-start-color: var(--if-color-danger);
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
    .ui-toast__actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--if-space-1);
    }
    .ui-toast__action {
      min-block-size: 2rem;
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
    .ui-toast__action:focus-visible {
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
