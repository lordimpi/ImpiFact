import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  ViewEncapsulation,
  booleanAttribute,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ButtonComponent } from '../button/button.component';
import { FieldComponent } from '../field/field.component';

/** Una opción de la lista: el id del registro y su nombre visible. */
export interface ComboboxOption {
  id: number;
  name: string;
}

/** Estado de la búsqueda: `idle` mientras falta algo para buscar (por ejemplo, el departamento). */
export type ComboboxStatus = 'idle' | 'loading' | 'ready' | 'error';

/** Textos del combobox, ya traducidos. */
export interface ComboboxLabels {
  /** Por qué todavía no hay opciones («Seleccione primero el departamento»). */
  idle?: string;
  loading: string;
  error: string;
  /** La búsqueda terminó sin opciones. */
  empty: string;
  retry: string;
}

let nextId = 0;

/** Distancia al final de la lista (en píxeles) desde la que se pide la página siguiente. */
const LOAD_MORE_THRESHOLD_PX = 48;

/**
 * Combobox con búsqueda en el servidor (presentación; patrón ARIA «combobox» con lista): se escribe para filtrar, se
 * recorre con las flechas, Enter elige y Escape cierra. Al bajar hasta el final de la lista, con el mouse o con el
 * teclado, pide la página siguiente (`loadMore`), sin botón. Quien lo usa busca en el Api con lo que emite
 * `termChange` y le pasa las opciones; aquí no se filtra nada. Es un control de Signal Forms (`[formField]`): su
 * valor es el id elegido como texto, vacío si no hay ninguno. Dentro de `ui-field` toma la etiqueta, la ayuda y el
 * error.
 *
 * Uso: `<ui-field ...><ui-combobox [formField]="form.municipality_id" [options]="..." [labels]="..." /></ui-field>`
 */
@Component({
  selector: 'ui-combobox',
  imports: [ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Sin encapsulación para compartir el estilo de los controles de ui-field; las clases llevan el prefijo ui-combobox.
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ui-combobox">
      <div class="ui-combobox__box">
        <input
          #control
          class="ui-field__control ui-select ui-combobox__input"
          type="text"
          role="combobox"
          autocomplete="off"
          aria-autocomplete="list"
          [id]="field?.controlId ?? ownId"
          [value]="text()"
          [disabled]="disabled()"
          [attr.aria-expanded]="open()"
          [attr.aria-controls]="listId"
          [attr.aria-activedescendant]="activeOptionId()"
          [attr.aria-describedby]="describedBy()"
          [attr.aria-invalid]="field?.invalid() ? 'true' : null"
          [attr.aria-required]="field?.required() || required() ? 'true' : null"
          (input)="onType($event)"
          (keydown)="onKeydown($event)"
          (click)="show()"
          (blur)="onBlur()"
        />
        <span class="ui-combobox__arrow" aria-hidden="true"></span>
      </div>
      @if (open()) {
        <ul
          #list
          class="ui-combobox__list"
          role="listbox"
          [id]="listId"
          [attr.aria-labelledby]="field?.labelId ?? null"
          (scroll)="onScroll()"
        >
          @for (option of options(); track option.id; let index = $index) {
            <!-- El teclado lo maneja el campo de texto (aria-activedescendant): la opción no recibe foco. -->
            <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
            <li
              class="ui-combobox__option"
              role="option"
              [id]="optionId(option)"
              [class.ui-combobox__option--active]="index === active()"
              [attr.aria-selected]="'' + option.id === value()"
              (mousedown)="$event.preventDefault()"
              (click)="choose(option)"
            >
              {{ option.name }}
            </li>
          }
          @if (hasMore()) {
            <li #sentinel class="ui-combobox__sentinel" role="presentation"></li>
          }
        </ul>
      }
      <div class="ui-combobox__status" [id]="statusId" aria-live="polite">
        @switch (status()) {
          @case ('idle') {
            @if (labels().idle) {
              <p class="ui-combobox__muted">{{ labels().idle }}</p>
            }
          }
          @case ('loading') {
            <p class="ui-combobox__muted">{{ labels().loading }}</p>
          }
          @case ('error') {
            <p class="ui-combobox__error">{{ labels().error }}</p>
            <ui-button
              variant="text"
              size="small"
              [label]="labels().retry"
              (pressed)="retry.emit()"
            />
          }
          @default {
            @if (options().length === 0) {
              <p class="ui-combobox__muted">{{ labels().empty }}</p>
            }
          }
        }
      </div>
    </div>
  `,
  styles: `
    .ui-combobox {
      position: relative;
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
    }
    .ui-combobox__box {
      position: relative;
      display: flex;
    }
    .ui-combobox__input {
      flex: 1;
      min-inline-size: 0;
      padding-inline-end: calc(var(--if-control-padding-x) * 3);
    }
    .ui-combobox__arrow {
      position: absolute;
      inset-block-start: 50%;
      inset-inline-end: var(--if-control-padding-x);
      inline-size: 0.5rem;
      block-size: 0.5rem;
      border-inline-end: 2px solid var(--if-color-text-muted);
      border-block-end: 2px solid var(--if-color-text-muted);
      transform: translateY(-75%) rotate(45deg);
      pointer-events: none;
    }
    .ui-combobox__list {
      position: absolute;
      inset-block-start: calc(var(--if-control-min-height) + var(--if-space-1));
      inset-inline: 0;
      z-index: 30;
      max-block-size: 16rem;
      margin: 0;
      padding: var(--if-space-1) 0;
      overflow-y: auto;
      list-style: none;
      background: var(--if-color-bg);
      border: 1px solid var(--if-color-border-strong);
      border-radius: var(--if-radius-sm);
      box-shadow: var(--if-shadow-overlay);
    }
    .ui-combobox__option {
      padding: var(--if-space-2) var(--if-control-padding-x);
      color: var(--if-color-text);
      cursor: pointer;
    }
    .ui-combobox__option:hover,
    .ui-combobox__option--active {
      background: var(--if-color-primary-subtle);
    }
    .ui-combobox__option[aria-selected='true'] {
      font-weight: var(--if-font-weight-bold);
    }
    .ui-combobox__sentinel {
      block-size: 1px;
    }
    .ui-combobox__status:empty {
      display: none;
    }
    .ui-combobox__muted,
    .ui-combobox__error {
      margin: 0;
      font-size: var(--if-font-size-sm);
    }
    .ui-combobox__muted {
      color: var(--if-color-text-muted);
    }
    .ui-combobox__error {
      color: var(--if-color-danger);
    }
  `,
})
export class ComboboxComponent {
  /** Id elegido como texto; vacío si no hay ninguno. Lo enlaza `[formField]`. */
  readonly value = model('');
  readonly options = input<readonly ComboboxOption[]>([]);
  readonly status = input<ComboboxStatus>('ready');
  /** El servidor tiene más opciones para la búsqueda actual. */
  readonly hasMore = input(false, { transform: booleanAttribute });
  readonly labels = input.required<ComboboxLabels>();
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  /** Texto escrito para buscar; vacío al elegir una opción o al salir del campo. */
  readonly termChange = output<string>();
  readonly loadMore = output<void>();
  readonly retry = output<void>();
  /** El campo perdió el foco (Signal Forms lo marca como tocado). */
  readonly touch = output<void>();

  protected readonly field = inject(FieldComponent, { optional: true });
  protected readonly ownId = `ui-combobox-${nextId++}`;
  protected readonly listId = `${this.ownId}-list`;
  protected readonly statusId = `${this.ownId}-status`;

  private readonly control = viewChild.required<ElementRef<HTMLInputElement>>('control');
  private readonly list = viewChild<ElementRef<HTMLUListElement>>('list');
  private readonly sentinel = viewChild<ElementRef<HTMLLIElement>>('sentinel');

  protected readonly open = signal(false);
  protected readonly active = signal(-1);
  /** Lo que la persona escribió; `null` mientras muestra el nombre de la opción elegida. */
  private readonly query = signal<string | null>(null);

  private readonly selectedName = computed(
    () => this.options().find((option) => String(option.id) === this.value())?.name ?? '',
  );
  protected readonly text = computed(() => this.query() ?? this.selectedName());
  protected readonly activeOptionId = computed(() => {
    const option = this.open() ? this.options()[this.active()] : undefined;
    return option ? this.optionId(option) : null;
  });
  protected readonly describedBy = computed(() => {
    const ids = [this.field?.describedBy() ?? null, this.statusId].filter((id) => id !== null);
    return ids.join(' ');
  });

  constructor() {
    // Con el final de la lista a la vista se pide la página siguiente (también si la lista no alcanza a tener scroll).
    const destroyRef = inject(DestroyRef);
    let observer: IntersectionObserver | null = null;
    destroyRef.onDestroy(() => observer?.disconnect());
    effect(() => {
      observer?.disconnect();
      observer = null;
      const sentinel = this.sentinel()?.nativeElement;
      const list = this.list()?.nativeElement;
      if (!sentinel || !list || typeof IntersectionObserver === 'undefined') {
        return;
      }
      observer = new IntersectionObserver(
        (entries) => {
          if (entries.some((entry) => entry.isIntersecting)) this.requestMore();
        },
        { root: list },
      );
      observer.observe(sentinel);
    });
  }

  /** Para Signal Forms: `focusBoundControl()` lleva el foco al campo de texto. */
  focus(options?: FocusOptions): void {
    this.control().nativeElement.focus(options);
  }

  protected optionId(option: ComboboxOption): string {
    return `${this.listId}-${option.id}`;
  }

  protected show(): void {
    if (this.disabled() || this.open()) {
      return;
    }
    this.open.set(true);
    this.active.set(Math.max(this.selectedIndex(), 0));
  }

  protected onType(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.query.set(text);
    this.termChange.emit(text);
    this.open.set(true);
    this.active.set(-1);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.options().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.open()) {
          this.show();
        } else if (count > 0) {
          this.moveTo(Math.min(this.active() + 1, count - 1));
        }
        if (this.active() >= count - 1) this.requestMore();
        return;
      case 'ArrowUp':
        event.preventDefault();
        if (this.open() && count > 0) this.moveTo(Math.max(this.active() - 1, 0));
        return;
      case 'Enter': {
        const option = this.open() ? this.options()[this.active()] : undefined;
        if (option) {
          event.preventDefault();
          this.choose(option);
        } else if (this.open()) {
          event.preventDefault();
        }
        return;
      }
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          event.stopPropagation();
          this.open.set(false);
        } else if (this.query() !== null) {
          this.restore();
        }
        return;
      case 'Tab':
        this.open.set(false);
        return;
    }
  }

  protected choose(option: ComboboxOption): void {
    this.value.set(String(option.id));
    this.restore();
    this.open.set(false);
  }

  protected onBlur(): void {
    this.open.set(false);
    if (this.query() === '') {
      // Borrar el texto y salir deja el campo sin valor.
      this.value.set('');
      this.restore();
    } else if (this.query() !== null) {
      this.restore();
    }
    this.touch.emit();
  }

  protected onScroll(): void {
    const list = this.list()?.nativeElement;
    if (list && list.scrollTop + list.clientHeight >= list.scrollHeight - LOAD_MORE_THRESHOLD_PX) {
      this.requestMore();
    }
  }

  /** Vuelve a mostrar el nombre de lo elegido y deja la búsqueda sin texto. */
  private restore(): void {
    const typed = this.query() !== null && this.query() !== '';
    this.query.set(null);
    if (typed) this.termChange.emit('');
  }

  private requestMore(): void {
    if (this.hasMore() && this.status() === 'ready') this.loadMore.emit();
  }

  private selectedIndex(): number {
    return this.options().findIndex((option) => String(option.id) === this.value());
  }

  private moveTo(index: number): void {
    this.active.set(index);
    const option = this.options()[index];
    const element = option ? document.getElementById(this.optionId(option)) : null;
    // jsdom no tiene scrollIntoView.
    element?.scrollIntoView?.({ block: 'nearest' });
  }
}
