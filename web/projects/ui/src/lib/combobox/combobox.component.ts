import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injectable,
  ViewEncapsulation,
  afterRenderEffect,
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
import { FilterService } from '@openng/optimus-ui/api';
import { Select } from '@openng/optimus-ui/select';
import { ButtonComponent } from '../button/button.component';
import { connectAccessor, selectPassThrough } from '../field/field-control';
import { FieldComponent } from '../field/field.component';
import { applyOptimusTexts } from '../field/optimus-texts';

/** Una opción de la lista: el id del registro y su nombre visible. */
export interface ComboboxOption {
  id: number;
  name: string;
}

/** Estado de la búsqueda: `idle` mientras falta algo para buscar (por ejemplo, el departamento). */
export type ComboboxStatus = 'idle' | 'loading' | 'ready' | 'error';

/** Textos del combobox, ya traducidos. */
export interface ComboboxLabels {
  /** Texto del campo vacío y de la caja de búsqueda que invita a escribir («Escriba para buscar…»). */
  placeholder?: string;
  /** Por qué todavía no hay opciones («Seleccione primero el departamento»). */
  idle?: string;
  loading: string;
  error: string;
  /** La búsqueda terminó sin opciones. */
  empty: string;
  retry: string;
}

/** An option as the Optimus select shows it: the id as text (the value of the form) and the name. */
interface ComboboxItem {
  value: string;
  name: string;
}

/**
 * The server searches: the select shows the options it receives as they are, without filtering them again in the
 * browser (the server matches without accents and by code; the browser would hide some of those results).
 */
@Injectable()
class ServerSideFilter extends FilterService {
  override filter<T>(value: T[]): T[] {
    return value;
  }
}

let nextId = 0;

/** Distancia al final de la lista (en píxeles) desde la que se pide la página siguiente. */
const LOAD_MORE_THRESHOLD_PX = 48;

/**
 * Lista con búsqueda en el servidor sobre el select de Optimus UI (`p-select` con caja de búsqueda): se abre, se
 * escribe en la caja para buscar, se recorre con las flechas, Enter elige y Escape cierra. Al bajar hasta el final de
 * la lista, con el mouse o con el teclado, pide la página siguiente (`loadMore`), sin botón. Quien lo usa busca en el
 * Api con lo que emite `termChange` y le pasa las opciones; aquí no se filtra nada. Es un control de Signal Forms
 * (`[formField]`): su valor es el id elegido como texto, vacío si no hay ninguno. El elegido conserva su nombre aunque
 * la búsqueda siguiente no lo traiga. Dentro de `ui-field` toma la etiqueta, la ayuda y el error. Con `locked` queda
 * deshabilitado por una razón ajena al campo (el municipio, mientras no hay departamento).
 *
 * Uso: `<ui-field ...><ui-combobox [formField]="form.municipality_id" [options]="..." [labels]="..."
 * [locked]="location.municipalityLocked()" /></ui-field>`
 */
@Component({
  selector: 'ui-combobox',
  imports: [Select, ButtonComponent],
  viewProviders: [{ provide: FilterService, useClass: ServerSideFilter }],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Sin encapsulación: el select de Optimus conserva sus clases; las propias llevan el prefijo ui-combobox.
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ui-combobox">
      <p-select
        [options]="items()"
        optionLabel="name"
        optionValue="value"
        [filter]="true"
        [resetFilterOnHide]="true"
        [placeholder]="labels().placeholder"
        [filterPlaceholder]="labels().placeholder"
        [ariaFilterLabel]="labels().placeholder ?? field?.label()"
        [emptyMessage]="emptyMessage()"
        [emptyFilterMessage]="emptyMessage()"
        [inputId]="field?.controlId ?? ownId"
        [ariaLabelledBy]="field?.labelId"
        [disabled]="disabled() || locked()"
        [required]="required() || (field?.required() ?? false)"
        [invalid]="field?.invalid() ?? false"
        scrollHeight="16rem"
        [pt]="passThrough()"
        (onFilter)="onFilter($event.filter)"
        (onShow)="onShow()"
        (onHide)="onHide()"
      />
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
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
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
  /**
   * Disabled for a reason outside the form field, for example the municipality while no department is chosen. It
   * does not change the field: the value stays and Signal Forms keep validating it.
   */
  readonly locked = input(false, { transform: booleanAttribute });
  /** Texto escrito para buscar; vacío al cerrar la lista después de buscar. */
  readonly termChange = output<string>();
  readonly loadMore = output<void>();
  readonly retry = output<void>();
  /** El campo perdió el foco o se cerró la lista (Signal Forms lo marca como tocado). */
  readonly touch = output<void>();

  protected readonly field = inject(FieldComponent, { optional: true });
  protected readonly ownId = `ui-combobox-${nextId++}`;
  protected readonly statusId = `${this.ownId}-status`;

  private readonly select = viewChild.required(Select);
  protected readonly passThrough = selectPassThrough(this.field, () => this.statusId);
  private readonly open = signal(false);
  /** Lo que la persona escribió en la caja de búsqueda; vacío sin búsqueda. */
  private readonly term = signal('');
  /** The chosen option, to keep showing its name when a later search does not bring it. */
  private readonly chosen = signal<ComboboxOption | null>(null);

  protected readonly items = computed<ComboboxItem[]>(() => {
    const items = this.options().map((option) => ({ value: String(option.id), name: option.name }));
    const chosen = this.chosen();
    const value = this.value();
    if (chosen && String(chosen.id) === value && !items.some((item) => item.value === value)) {
      items.push({ value, name: chosen.name });
    }
    return items;
  });

  protected readonly emptyMessage = computed(() => {
    const labels = this.labels();
    switch (this.status()) {
      case 'idle':
        return labels.idle ?? labels.empty;
      case 'loading':
        return labels.loading;
      case 'error':
        return labels.error;
      default:
        return labels.empty;
    }
  });

  constructor() {
    applyOptimusTexts();
    connectAccessor(this.select, this.value, () => this.touch.emit());

    // Remember the name of the chosen option while it is among the options.
    effect(() => {
      const value = this.value();
      const option = this.options().find((candidate) => String(candidate.id) === value);
      if (option) this.chosen.set(option);
    });

    // The next page is asked for with the keyboard on the last option, with the open list scrolled to its end, or
    // when the list is too short to scroll.
    const destroyRef = inject(DestroyRef);
    let list: HTMLElement | null = null;
    const onScroll = () => {
      // jsdom has no layout (clientHeight 0): only a rendered list is measured.
      if (
        list &&
        list.clientHeight > 0 &&
        list.scrollTop + list.clientHeight >= list.scrollHeight - LOAD_MORE_THRESHOLD_PX
      ) {
        this.requestMore();
      }
    };
    destroyRef.onDestroy(() => list?.removeEventListener('scroll', onScroll));
    afterRenderEffect({
      write: () => {
        const open = this.open();
        const focused = this.select().focusedOptionIndex();
        const count = this.items().length;
        this.hasMore();
        this.status();
        const container = open ? this.listContainer() : null;
        if (container !== list) {
          list?.removeEventListener('scroll', onScroll);
          list = container;
          list?.addEventListener('scroll', onScroll, { passive: true });
        }
        if (!open) return;
        if (count > 0 && focused >= count - 1) this.requestMore();
        onScroll();
      },
    });
  }

  /** Para Signal Forms: `focusBoundControl()` lleva el foco al select. */
  focus(): void {
    this.select().focus();
  }

  protected onFilter(term: string | null | undefined): void {
    const text = term ?? '';
    this.term.set(text);
    this.termChange.emit(text);
  }

  protected onShow(): void {
    this.open.set(true);
  }

  protected onHide(): void {
    this.open.set(false);
    // Closing the list clears the search box: the next search starts from the first page.
    if (this.term() !== '') {
      this.term.set('');
      this.termChange.emit('');
    }
    this.touch.emit();
  }

  private listElement(): HTMLElement | null {
    return document.getElementById(`${this.select().id}_list`);
  }

  private listContainer(): HTMLElement | null {
    return this.listElement()?.parentElement ?? null;
  }

  private requestMore(): void {
    if (this.hasMore() && this.status() === 'ready') this.loadMore.emit();
  }
}
