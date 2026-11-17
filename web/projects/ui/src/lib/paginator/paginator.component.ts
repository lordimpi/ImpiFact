import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { ButtonDirective, ButtonLabel } from '@openng/optimus-ui/button';
import { UiPageChange, UiPagination, UiPaginatorLabels } from './pagination';

/** Páginas vecinas que se muestran a cada lado de la actual. */
const SIBLINGS = 1;

type PageItem = { kind: 'page'; page: number } | { kind: 'gap'; id: string };

/**
 * Paginador de servidor: no conoce los datos, solo emite la página pedida.
 * Con cursor muestra «anterior» y «siguiente»; con total muestra además los números de página.
 */
@Component({
  selector: 'ui-paginator',
  imports: [ButtonDirective, ButtonLabel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="ui-paginator" [attr.aria-label]="labels().navigation">
      <button
        pButton
        type="button"
        severity="secondary"
        [text]="true"
        [disabled]="!hasPrevious()"
        (click)="previous()"
      >
        <span pButtonLabel>{{ labels().previous }}</span>
      </button>
      @if (pages(); as items) {
        <ul class="ui-paginator__pages">
          @for (item of items; track item.kind === 'page' ? item.page : item.id) {
            <li>
              @if (item.kind === 'page') {
                <button
                  pButton
                  type="button"
                  [text]="item.page !== currentPage()"
                  [attr.aria-label]="pageLabel(item.page)"
                  [attr.aria-current]="item.page === currentPage() ? 'page' : null"
                  (click)="goTo(item.page)"
                >
                  <span pButtonLabel>{{ item.page }}</span>
                </button>
              } @else {
                <span class="ui-paginator__gap" aria-hidden="true">…</span>
              }
            </li>
          }
        </ul>
      }
      <button
        pButton
        type="button"
        severity="secondary"
        [text]="true"
        [disabled]="!hasNext()"
        (click)="next()"
      >
        <span pButtonLabel>{{ labels().next }}</span>
      </button>
    </nav>
  `,
  styles: `
    .ui-paginator {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: flex-end;
      gap: var(--if-space-1);
      padding-block: var(--if-space-2);
    }
    .ui-paginator__pages {
      display: flex;
      gap: var(--if-space-1);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .ui-paginator__gap {
      display: inline-flex;
      align-items: center;
      min-block-size: var(--if-control-min-height);
      padding-inline: var(--if-space-1);
      color: var(--if-color-text-muted);
    }
  `,
})
export class PaginatorComponent {
  readonly pagination = input.required<UiPagination>();
  readonly labels = input.required<UiPaginatorLabels>();

  readonly pageChange = output<UiPageChange>();

  /** Número de páginas en modo `pages`; al menos 1 aunque no haya registros. */
  private readonly pageCount = computed(() => {
    const pagination = this.pagination();
    if (pagination.kind !== 'pages') return 0;
    return Math.max(1, Math.ceil(pagination.total / Math.max(1, pagination.pageSize)));
  });

  protected readonly currentPage = computed(() => {
    const pagination = this.pagination();
    return pagination.kind === 'pages' ? pagination.page : 0;
  });

  protected readonly hasPrevious = computed(() => {
    const pagination = this.pagination();
    return pagination.kind === 'cursor' ? pagination.previousCursor !== null : pagination.page > 1;
  });

  protected readonly hasNext = computed(() => {
    const pagination = this.pagination();
    return pagination.kind === 'cursor'
      ? pagination.nextCursor !== null
      : pagination.page < this.pageCount();
  });

  /** Primera página, ventana alrededor de la actual y última, con huecos entre ellas. */
  protected readonly pages = computed<PageItem[] | null>(() => {
    if (this.pagination().kind !== 'pages') return null;
    const count = this.pageCount();
    const current = Math.min(Math.max(1, this.currentPage()), count);
    const start = Math.max(1, current - SIBLINGS);
    const end = Math.min(count, current + SIBLINGS);
    const items: PageItem[] = [];
    if (start > 1) items.push({ kind: 'page', page: 1 });
    if (start > 2) items.push({ kind: 'gap', id: 'start' });
    for (let page = start; page <= end; page++) items.push({ kind: 'page', page });
    if (end < count - 1) items.push({ kind: 'gap', id: 'end' });
    if (end < count) items.push({ kind: 'page', page: count });
    return items;
  });

  protected pageLabel(page: number): string {
    return this.labels().page.replace('{page}', String(page));
  }

  protected previous(): void {
    const pagination = this.pagination();
    if (pagination.kind === 'cursor') {
      if (pagination.previousCursor !== null) {
        this.pageChange.emit({
          kind: 'cursor',
          cursor: pagination.previousCursor,
          direction: 'previous',
        });
      }
    } else if (pagination.page > 1) {
      this.goTo(pagination.page - 1);
    }
  }

  protected next(): void {
    const pagination = this.pagination();
    if (pagination.kind === 'cursor') {
      if (pagination.nextCursor !== null) {
        this.pageChange.emit({ kind: 'cursor', cursor: pagination.nextCursor, direction: 'next' });
      }
    } else if (pagination.page < this.pageCount()) {
      this.goTo(pagination.page + 1);
    }
  }

  protected goTo(page: number): void {
    if (page !== this.currentPage()) this.pageChange.emit({ kind: 'pages', page });
  }
}
