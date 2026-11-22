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
  DatePickerComponent,
  DigitsOnlyDirective,
  FieldComponent,
  InputDirective,
} from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { ReceivedFilters } from '../state/received-filters';

/**
 * Filters of the received documents list (presentation), with the layout of the documents list: the supplier NIT in
 * view and the issue dates folded under «Más filtros», which opens by itself when the URL filters by date. It emits
 * the new filters; the page takes them to the URL.
 */
@Component({
  selector: 'feat-received-filters',
  imports: [
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    DatePickerComponent,
    DigitsOnlyDirective,
    FieldComponent,
    InputDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="filters" novalidate (submit)="submit($event)">
      <fieldset class="filters__group">
        <legend class="filters__legend">
          {{ 'documents.received.filters.title' | transloco }}
        </legend>
        <div class="filters__grid">
          <ui-field
            [label]="'documents.received.filters.supplier' | transloco"
            [hint]="'documents.received.filters.supplierHint' | transloco"
          >
            <input
              uiInput
              uiDigitsOnly
              type="search"
              name="supplier"
              inputmode="numeric"
              maxlength="20"
              [placeholder]="'documents.received.filters.supplierPlaceholder' | transloco"
              [value]="supplierNit()"
              (input)="supplierNit.set(read($event))"
            />
          </ui-field>
        </div>

        <details
          #more
          class="filters__more"
          [open]="appliedDates()"
          (toggle)="moreOpen.set(more.open)"
        >
          <summary class="filters__summary">
            <span>{{
              (moreOpen() ? 'documents.received.filters.less' : 'documents.received.filters.more')
                | transloco
            }}</span>
            @if (from() || to()) {
              <ui-badge tone="info">{{
                'documents.received.filters.datesActive' | transloco
              }}</ui-badge>
            }
          </summary>
          <p class="filters__hint">{{ 'documents.received.filters.datesHint' | transloco }}</p>
          <div class="filters__grid">
            <ui-field [label]="'documents.received.filters.from' | transloco">
              <ui-date-picker
                [placeholder]="'documents.received.filters.datePlaceholder' | transloco"
                [value]="from()"
                (valueChange)="from.set($event)"
              />
            </ui-field>
            <ui-field
              [label]="'documents.received.filters.to' | transloco"
              [error]="
                rangeInvalid() ? ('documents.received.filters.rangeError' | transloco) : null
              "
            >
              <ui-date-picker
                [placeholder]="'documents.received.filters.datePlaceholder' | transloco"
                [minDate]="from() || undefined"
                [value]="to()"
                (valueChange)="to.set($event)"
              />
            </ui-field>
          </div>
        </details>

        <div class="filters__actions">
          <div class="filters__buttons">
            <ui-button
              variant="ghost"
              [label]="'documents.received.filters.clear' | transloco"
              (pressed)="clear()"
            />
            <ui-button type="submit" [label]="'documents.received.filters.apply' | transloco" />
          </div>
        </div>
      </fieldset>
    </form>
  `,
  styleUrl: './document-filters.component.css',
})
export class ReceivedFiltersComponent {
  /** Applied filters (the URL). */
  readonly filters = input<ReceivedFilters>({});
  readonly apply = output<ReceivedFilters>();

  protected readonly supplierNit = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  /** With dates in the URL, «Más filtros» starts open, so the active filter is visible. */
  protected readonly appliedDates = computed(() => !!(this.filters().from || this.filters().to));
  protected readonly moreOpen = signal(false);
  /** Shown after a try to apply a range that ends before it starts (the API answers 422). */
  protected readonly rangeInvalid = signal(false);

  constructor() {
    effect(() => {
      const filters = this.filters();
      this.supplierNit.set(filters.supplierNit ?? '');
      this.from.set(filters.from ?? '');
      this.to.set(filters.to ?? '');
      this.rangeInvalid.set(false);
    });
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const from = this.from();
    const to = this.to();
    // ISO dates compare as text.
    if (from && to && to < from) {
      this.rangeInvalid.set(true);
      return;
    }
    this.rangeInvalid.set(false);
    const filters: ReceivedFilters = {};
    if (this.supplierNit().trim()) filters.supplierNit = this.supplierNit().trim();
    if (from) filters.from = from;
    if (to) filters.to = to;
    this.apply.emit(filters);
  }

  protected clear(): void {
    this.apply.emit({});
  }
}
