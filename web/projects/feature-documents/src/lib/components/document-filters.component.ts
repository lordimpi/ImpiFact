import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import {
  BadgeComponent,
  ButtonComponent,
  FieldComponent,
  InputDirective,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
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
    FieldComponent,
    InputDirective,
    SelectDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="filters" novalidate (submit)="submit($event)">
      <fieldset class="filters__group">
        <legend class="filters__legend">{{ 'documents.filters.title' | transloco }}</legend>
        <div class="filters__grid">
          <ui-field [label]="'documents.filters.from' | transloco">
            <input
              uiInput
              type="date"
              name="from"
              [value]="from()"
              (change)="from.set(read($event))"
            />
          </ui-field>
          <ui-field [label]="'documents.filters.to' | transloco">
            <input uiInput type="date" name="to" [value]="to()" (change)="to.set(read($event))" />
          </ui-field>
          <ui-field [label]="'documents.filters.kind' | transloco">
            <select uiSelect name="kind" (change)="kind.set(read($event))">
              <option value="" [selected]="!kind()">
                {{ 'documents.filters.anyKind' | transloco }}
              </option>
              @for (option of kinds; track option) {
                <option [value]="option" [selected]="kind() === option">
                  {{ 'documents.' + kindKey(option) | transloco }}
                </option>
              }
            </select>
          </ui-field>
          <ui-field
            [label]="'documents.filters.customer' | transloco"
            [hint]="'documents.filters.customerHint' | transloco"
          >
            <input
              uiInput
              type="search"
              name="customer"
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
              [value]="number()"
              (input)="number.set(read($event))"
            />
          </ui-field>
        </div>

        <!-- The long DIAN status list stays folded; it opens by itself when the URL filters by status. -->
        <details class="filters__more" [open]="appliedStatuses() > 0">
          <summary class="filters__summary">
            <span>{{ 'documents.filters.more' | transloco }}</span>
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
  /** Filtros vigentes (los de la URL). */
  readonly filters = input<DocumentFilters>({});
  readonly apply = output<DocumentFilters>();

  protected readonly kinds = DOCUMENT_KINDS;
  protected readonly statuses = DOCUMENT_STATUSES;
  protected readonly kindKey = kindKey;
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
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
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
}
