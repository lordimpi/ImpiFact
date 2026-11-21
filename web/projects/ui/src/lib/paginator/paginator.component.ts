import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { Optimus } from '@openng/optimus-ui/config';
import { Paginator, PaginatorState } from '@openng/optimus-ui/paginator';
import { UI_TEXTS } from '../i18n/ui-texts';
import { UiPageChange, UiPagination, UiPaginatorLabels } from './pagination';

/** Optimus paginator inputs for one state of the pagination. */
interface PaginatorView {
  first: number;
  rows: number;
  totalRecords: number;
  pageLinks: boolean;
}

/**
 * Paginador de servidor sobre el `p-paginator` de Optimus UI: no conoce los datos, solo emite la página pedida.
 * Con total muestra primera, anterior, los números de página, siguiente y última; con cursor solo anterior y
 * siguiente.
 *
 * The Optimus paginator only knows page indexes, so the cursor mode is mapped onto a virtual list of one row per
 * page: the current page is the index `position`, there is one page before it when the Api sent a previous cursor
 * and one after it when it sent a next cursor. Optimus moves its own index as soon as a button is clicked, before the
 * Api answers; `position` follows that index, so the inputs bound after the answer always match the paginator's own
 * state (a stale input would not be re-sent and would leave a button wrongly disabled).
 */
@Component({
  selector: 'ui-paginator',
  imports: [Paginator],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="ui-paginator" [attr.aria-label]="labels().navigation">
      <p-paginator
        [first]="view().first"
        [rows]="view().rows"
        [totalRecords]="view().totalRecords"
        [showPageLinks]="view().pageLinks"
        [showFirstLastIcon]="view().pageLinks"
        [pageLinkSize]="5"
        (onPageChange)="onPageChange($event)"
      />
    </nav>
  `,
  styles: `
    .ui-paginator {
      display: flex;
      justify-content: flex-end;
    }
    .ui-paginator .p-paginator {
      flex-wrap: wrap;
      padding-inline: 0;
      background: transparent;
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class PaginatorComponent {
  readonly pagination = input.required<UiPagination>();
  readonly labels = input.required<UiPaginatorLabels>();

  readonly pageChange = output<UiPageChange>();

  private readonly optimus = inject(Optimus);
  private readonly texts = inject(UI_TEXTS);

  /**
   * Index of the current page in cursor mode: 0 without a previous cursor, otherwise at least 1. It keeps the index
   * Optimus moved to after a click, and only snaps back when the Api answer contradicts it.
   */
  private readonly position = linkedSignal<UiPagination, number>({
    source: this.pagination,
    computation: (pagination, previous) => {
      if (pagination.kind !== 'cursor' || pagination.previousCursor === null) return 0;
      return Math.max(1, previous?.value ?? 0);
    },
  });

  protected readonly view = computed<PaginatorView>(() => {
    const pagination = this.pagination();
    if (pagination.kind === 'pages') {
      const rows = Math.max(1, pagination.pageSize);
      return {
        first: (Math.max(1, pagination.page) - 1) * rows,
        rows,
        totalRecords: Math.max(0, pagination.total),
        pageLinks: true,
      };
    }
    const position = this.position();
    return {
      first: position,
      rows: 1,
      totalRecords: position + 1 + (pagination.nextCursor === null ? 0 : 1),
      pageLinks: false,
    };
  });

  constructor() {
    // Optimus names its buttons from the global translation. This view effect runs before the paginator below is
    // checked, so its buttons get the Spanish names from the first render.
    effect(() => {
      const labels = this.labels();
      this.optimus.setTranslation({
        aria: {
          ...this.optimus.translation.aria,
          prevPageLabel: labels.previous,
          nextPageLabel: labels.next,
          pageLabel: labels.page,
          firstPageLabel: this.texts.firstPage,
          lastPageLabel: this.texts.lastPage,
        },
      });
    });
  }

  protected onPageChange(state: PaginatorState): void {
    const pagination = this.pagination();
    const page = state.page ?? 0;
    if (pagination.kind === 'pages') {
      if (page + 1 !== pagination.page) this.pageChange.emit({ kind: 'pages', page: page + 1 });
      return;
    }
    const forward = page > this.position();
    const cursor = forward ? pagination.nextCursor : pagination.previousCursor;
    this.position.set(page);
    if (cursor !== null) {
      this.pageChange.emit({
        kind: 'cursor',
        cursor,
        direction: forward ? 'next' : 'previous',
      });
    }
  }
}
