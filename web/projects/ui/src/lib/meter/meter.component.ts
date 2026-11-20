import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Desde esta proporción del máximo la barra se marca como cerca del límite. */
const HIGH_RATIO = 0.9;

/**
 * Medidor de consumo (por ejemplo, documentos usados del plan): una barra con `role="meter"` y su texto visible, que
 * es lo que lee el lector de pantalla (`aria-valuetext`). El color nunca va solo: el texto dice cuánto queda. Sin
 * `max` (consumo sin límite) muestra solo el texto. Componente de presentación: recibe los textos ya traducidos.
 */
@Component({
  selector: 'ui-meter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-meter" [class.ui-meter--high]="high()">
      @if (max() !== null) {
        <div
          class="ui-meter__track"
          role="meter"
          aria-valuemin="0"
          [attr.aria-label]="label()"
          [attr.aria-valuenow]="value()"
          [attr.aria-valuemax]="max()"
          [attr.aria-valuetext]="valueText()"
        >
          <div class="ui-meter__fill" [style.inline-size.%]="percent()"></div>
        </div>
      }
      <p class="ui-meter__text">{{ valueText() }}</p>
    </div>
  `,
  styles: `
    .ui-meter {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
    }
    .ui-meter__track {
      block-size: 0.5rem;
      overflow: hidden;
      background: var(--if-color-surface-raised);
      border-radius: var(--if-radius-pill);
    }
    .ui-meter__fill {
      block-size: 100%;
      background: var(--if-color-primary);
      border-radius: var(--if-radius-pill);
    }
    .ui-meter--high .ui-meter__fill {
      background: var(--if-color-danger);
    }
    .ui-meter__text {
      margin: 0;
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
  `,
})
export class MeterComponent {
  /** Nombre accesible del medidor (por ejemplo, «Facturas y notas»). */
  readonly label = input.required<string>();
  readonly value = input.required<number>();
  /** Máximo; `null` cuando no hay límite. */
  readonly max = input<number | null>(null);
  /** Texto que se muestra y se anuncia («40 de 100 usados, quedan 60»). */
  readonly valueText = input.required<string>();

  protected readonly ratio = computed(() => {
    const max = this.max();
    return max === null || max <= 0 ? (max === 0 ? 1 : 0) : this.value() / max;
  });
  protected readonly percent = computed(() => Math.min(100, Math.max(0, this.ratio() * 100)));
  protected readonly high = computed(() => this.max() !== null && this.ratio() >= HIGH_RATIO);
}
