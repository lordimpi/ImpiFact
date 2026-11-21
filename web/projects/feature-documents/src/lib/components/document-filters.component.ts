import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import {
  BadgeComponent,
  ButtonComponent,
  DatePickerComponent,
  FieldComponent,
  InputDirective,
  SelectComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { DOCUMENTS_I18N_SCOPE } from '../documents-i18n';
import { DOCUMENT_KINDS, DOCUMENT_STATUSES, kindKey, statusKey } from '../models/document-status';
import { DocumentFilters } from '../state/list-filters';

/**
 * Formulario de filtros de la lista (presentación): muestra los filtros vigentes y emite los nuevos al aplicar. La
 * página los lleva a la URL; aquí no se consulta el Api.
 */
@Component({
  selector: 'feat-document-filters',
  imports: [
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    DatePickerComponent,
    FieldComponent,
    InputDirective,
    SelectComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="filters" novalidate (submit)="submit($event)">
      <fieldset class="filters__group">
        <legend class="filters__legend">{{ 'documents.filters.title' | transloco }}</legend>
        <div class="filters__grid">
          <ui-field [label]="'documents.filters.from' | transloco">
            <ui-date-picker
              [placeholder]="'documents.filters.datePlaceholder' | transloco"
              [value]="from()"
              (valueChange)="from.set($event)"
            />
          </ui-field>
          <ui-field [label]="'documents.filters.to' | transloco">
            <ui-date-picker
              [placeholder]="'documents.filters.datePlaceholder' | transloco"
              [value]="to()"
              (valueChange)="to.set($event)"
            />
          </ui-field>
          <ui-field [label]="'documents.filters.kind' | transloco">
            <!-- '' is «all types»: an option of its own, so the person can go back to it. -->
            <ui-select
              [options]="kindOptions()"
              [placeholder]="'documents.filters.anyKind' | transloco"
              [value]="kind()"
              (valueChange)="kind.set($event)"
            />
          </ui-field>
          <ui-field
            [label]="'documents.filters.customer' | transloco"
            [hint]="'documents.filters.customerHint' | transloco"
          >
            <input
              uiInput
              type="search"
              name="customer"
              [placeholder]="'documents.filters.customerPlaceholder' | transloco"
              [value]="customer()"
              (input)="customer.set(read($event))"
            />
          </ui-field>
          <ui-field
            [label]="'documents.filters.number' | transloco"
            [hint]="'documents.filters.numberHint' | transloco"
          >
            <input
              uiInput
              type="search"
              name="number"
              [placeholder]="'documents.filters.numberPlaceholder' | transloco"
              [value]="number()"
              (input)="number.set(read($event))"
            />
          </ui-field>
        </div>

        <!-- The long DIAN status list stays folded; it opens by itself when the URL filters by status. The summary
             follows the real state (toggle event): «Más filtros» folded, «Menos filtros» open. -->
        <details
          #more
          class="filters__more"
          [open]="appliedStatuses() > 0"
          (toggle)="moreOpen.set(more.open)"
        >
          <summary class="filters__summary">
            <span>{{
              (moreOpen() ? 'documents.filters.less' : 'documents.filters.more') | transloco
            }}</span>
            @if (status().length > 0) {
              <ui-badge tone="info">{{
                'documents.filters.statusCount' | transloco: { count: status().length }
              }}</ui-badge>
            }
          </summary>
          <fieldset class="filters__statuses">
            <legend>{{ 'documents.filters.status' | transloco }}</legend>
            <p class="filters__hint">{{ 'documents.filters.statusHint' | transloco }}</p>
            <div class="filters__checks">
              @for (option of statuses; track option) {
                <label class="filters__chip">
                  <input
                    type="checkbox"
                    [checked]="status().includes(option)"
                    (change)="toggleStatus(option, $event)"
                  />
                  {{ 'documents.' + statusKey(option) | transloco }}
                </label>
              }
            </div>
          </fieldset>
        </details>

        <div class="filters__actions">
          <label class="filters__check">
            <input
              type="checkbox"
              [checked]="withTotal()"
              (change)="withTotal.set(checked($event))"
            />
            {{ 'documents.filters.withTotal' | transloco }}
          </label>
          <div class="filters__buttons">
            <ui-button
              variant="ghost"
              [label]="'documents.filters.clear' | transloco"
              (pressed)="clear()"
            />
            <ui-button type="submit" [label]="'documents.filters.apply' | transloco" />
          </div>
        </div>
      </fieldset>
    </form>
  `,
  styleUrl: './document-filters.component.css',
})
export class DocumentFiltersComponent {
  private readonly transloco = inject(TranslocoService);

  /** Filtros vigentes (los de la URL). */
  readonly filters = input<DocumentFilters>({});
  readonly apply = output<DocumentFilters>();

  /** Document types for the select; '' (all types) first. */
  protected readonly kindOptions = computed(() => [
    { id: '', name: this.text('filters.anyKind') },
    ...DOCUMENT_KINDS.map((kind) => ({ id: kind, name: this.text(kindKey(kind)) })),
  ]);
  protected readonly statuses = DOCUMENT_STATUSES;
  protected readonly statusKey = statusKey;

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly kind = signal('');
  protected readonly status = signal<string[]>([]);
  protected readonly customer = signal('');
  protected readonly number = signal('');
  protected readonly withTotal = signal(false);
  /** DIAN statuses of the applied filters (the URL): with any, «Más filtros» starts open. */
  protected readonly appliedStatuses = computed(() => this.filters().status?.length ?? 0);
  /** Whether «Más filtros» is open right now (by the person or by the applied statuses). */
  protected readonly moreOpen = signal(false);

  constructor() {
    // Cuando la URL cambia (atrás, enlace compartido), el formulario muestra lo que se está consultando.
    effect(() => {
      const filters = this.filters();
      this.from.set(filters.from ?? '');
      this.to.set(filters.to ?? '');
      this.kind.set(filters.kind ?? '');
      this.status.set(filters.status ?? []);
      this.customer.set(filters.customer ?? '');
      this.number.set(filters.number ?? '');
      this.withTotal.set(filters.withTotal ?? false);
    });
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected toggleStatus(option: string, event: Event): void {
    const others = this.status().filter((value) => value !== option);
    this.status.set(this.checked(event) ? [...others, option] : others);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const filters: DocumentFilters = {};
    if (this.from()) filters.from = this.from();
    if (this.to()) filters.to = this.to();
    if (this.kind()) filters.kind = this.kind();
    if (this.status().length) {
      // Mismo orden siempre, así la URL de la misma búsqueda es la misma.
      filters.status = DOCUMENT_STATUSES.filter((value) => this.status().includes(value));
    }
    if (this.customer().trim()) filters.customer = this.customer().trim();
    if (this.number().trim()) filters.number = this.number().trim();
    if (this.withTotal()) filters.withTotal = true;
    this.apply.emit(filters);
  }

  protected clear(): void {
    this.apply.emit({});
  }

  private text(key: string): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`);
  }
}
