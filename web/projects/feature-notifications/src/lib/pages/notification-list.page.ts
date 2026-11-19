import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { Router } from '@angular/router';
import { NotificationResponse } from '@impifact/api';
import { NotificationsStore, ToastStore } from '@impifact/core';
import {
  ButtonComponent,
  FieldComponent,
  PageTitleComponent,
  SelectDirective,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  formatNoticeDate,
  notificationsText,
  provideNotificationsI18n,
} from '../notifications-i18n';
import { NotificationListStore } from '../state/notification-list.store';

/** Valor de `?estado=` que pide solo los avisos sin leer. */
const UNREAD_PARAM = 'sin-leer';

/**
 * Página `/notificaciones`: todos los avisos de la empresa activa con paginación por cursor en el Api y el filtro de
 * sin leer en la URL (`?estado=sin-leer`). Marcar uno o todos pasa por el store de la campana de core, así el número
 * del encabezado baja enseguida, y luego se vuelve a pedir la página visible.
 */
@Component({
  selector: 'feat-notification-list-page',
  imports: [
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    PageTitleComponent,
    SelectDirective,
    TableComponent,
    TableCellDirective,
  ],
  providers: [provideNotificationsI18n(), NotificationListStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notification-list.page.html',
  styleUrl: './notification-list.page.css',
})
export class NotificationListPage {
  protected readonly store = inject(NotificationListStore);
  protected readonly bell = inject(NotificationsStore);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastStore);

  /** Filtro en la URL (`?estado=sin-leer`). */
  readonly estado = input<string>();

  protected readonly onlyUnread = computed(() => this.estado() === UNREAD_PARAM);
  protected readonly busyId = signal<string | null>(null);
  protected readonly markingAll = signal(false);

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'title', header: this.text('list.notice') },
    { key: 'created_at', header: this.text('list.date') },
    { key: 'is_read', header: this.text('list.status') },
    { key: 'actions', header: this.text('list.actions') },
  ]);

  protected readonly tableLabels = computed<UiTableLabels>(() => ({
    loading: this.text('list.loading'),
    emptyTitle: this.text(this.onlyUnread() ? 'list.emptyUnreadTitle' : 'list.emptyTitle'),
    emptyDescription: this.text('list.emptyDescription'),
    errorTitle: this.text('list.errorTitle'),
    errorDescription: this.text('list.errorDescription'),
    retry: this.text('list.retry'),
    paginator: {
      navigation: this.text('list.paginator.navigation'),
      previous: this.text('list.paginator.previous'),
      next: this.text('list.paginator.next'),
      page: this.text('list.paginator.page'),
    },
  }));

  protected readonly trackById = (_: number, row: NotificationResponse) => row.id;
  protected readonly formatDate = formatNoticeDate;

  constructor() {
    // El filtro de la URL manda: cada cambio vuelve a la primera página en el Api.
    effect(() => {
      const unread = this.onlyUnread();
      untracked(() => this.store.setFilters({ unread }));
    });
  }

  protected changeFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], {
      queryParams: { estado: value === 'unread' ? UNREAD_PARAM : null },
      queryParamsHandling: 'merge',
    });
  }

  protected markReadLabel(row: NotificationResponse): string {
    return this.text('list.markReadRow', { title: row.title ?? '' });
  }

  protected async markRead(row: NotificationResponse): Promise<void> {
    this.busyId.set(row.id ?? null);
    try {
      if (await this.bell.markRead(row)) {
        this.store.refresh();
      }
    } finally {
      this.busyId.set(null);
    }
  }

  protected async markAll(): Promise<void> {
    this.markingAll.set(true);
    try {
      const count = await this.bell.markAllRead();
      if (count !== null) {
        this.toasts.show({ kind: 'success', text: this.text('list.markedAll', { count }) });
        this.store.refresh();
      }
    } finally {
      this.markingAll.set(false);
    }
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return notificationsText(this.transloco, key, params);
  }
}
