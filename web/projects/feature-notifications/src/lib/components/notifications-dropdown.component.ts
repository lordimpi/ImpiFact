import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NotificationResponse } from '@impifact/api';
import { NotificationsStore } from '@impifact/core';
import { ButtonComponent, EmptyStateComponent, LoadingComponent } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  formatNoticeDate,
  notificationsText,
  provideNotificationsI18n,
} from '../notifications-i18n';

/**
 * Contenido de la lista desplegable de la campana: los últimos avisos del store de core, marcar uno o todos como
 * leídos y el enlace a `/notificaciones`. El shell lo carga la primera vez que se abre la campana y se encarga de
 * abrirlo, cerrarlo y devolver el foco; al abrirse vuelve a pedir los avisos.
 */
@Component({
  selector: 'feat-notifications-dropdown',
  imports: [RouterLink, TranslocoPipe, ButtonComponent, EmptyStateComponent, LoadingComponent],
  providers: [provideNotificationsI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dropdown">
      <div class="dropdown-header">
        <h2 class="dropdown-title">{{ 'notifications.dropdown.title' | transloco }}</h2>
        <ui-button
          variant="text"
          size="small"
          [label]="'notifications.dropdown.markAll' | transloco"
          [disabled]="store.unreadCount() === 0"
          [loading]="markingAll()"
          (pressed)="markAll()"
        />
      </div>

      @if (!store.loaded() && !store.error()) {
        <ui-loading [label]="'notifications.dropdown.loading' | transloco" [lines]="2" />
      } @else if (store.error() && store.latest().length === 0) {
        <p class="dropdown-error" role="alert">
          {{ 'notifications.dropdown.errorTitle' | transloco }}
        </p>
      } @else if (store.latest().length === 0) {
        <ui-empty-state
          [title]="'notifications.dropdown.emptyTitle' | transloco"
          [description]="'notifications.dropdown.emptyDescription' | transloco"
        />
      } @else {
        <ul class="dropdown-list">
          @for (item of store.latest(); track item.id) {
            <li class="dropdown-item" [class.dropdown-item--unread]="!item.is_read">
              <span class="dropdown-item-title">
                @if (!item.is_read) {
                  <span class="dropdown-dot" aria-hidden="true"></span>
                  <span class="dropdown-sr-only"
                    >{{ 'notifications.dropdown.unread' | transloco }}:
                  </span>
                }
                {{ item.title }}
              </span>
              <span class="dropdown-item-body">{{ item.body }}</span>
              <span class="dropdown-item-date">{{ formatDate(item.created_at) }}</span>
              @if (!item.is_read) {
                <ui-button
                  variant="text"
                  size="small"
                  [label]="'notifications.dropdown.markRead' | transloco"
                  [ariaLabel]="markReadLabel(item)"
                  [loading]="busyId() === item.id"
                  (pressed)="markRead(item)"
                />
              }
            </li>
          }
        </ul>
      }

      <a class="dropdown-all" routerLink="/notificaciones">{{
        'notifications.dropdown.seeAll' | transloco
      }}</a>
    </div>
  `,
  styles: `
    .dropdown {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-2);
      padding: var(--if-space-3);
    }
    .dropdown-header {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-2);
      align-items: center;
      justify-content: space-between;
    }
    .dropdown-title {
      margin: 0;
      font-size: var(--if-font-size-md);
    }
    .dropdown-list {
      display: flex;
      flex-direction: column;
      margin: 0;
      padding: 0;
      list-style: none;
      max-block-size: 22rem;
      overflow-y: auto;
    }
    .dropdown-item {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--if-space-1);
      padding-block: var(--if-space-2);
      border-block-end: 1px solid var(--if-color-border);
    }
    .dropdown-item-title {
      font-weight: var(--if-font-weight-medium);
    }
    .dropdown-item--unread .dropdown-item-title {
      font-weight: var(--if-font-weight-bold);
    }
    .dropdown-dot {
      display: inline-block;
      inline-size: 0.5rem;
      block-size: 0.5rem;
      margin-inline-end: var(--if-space-1);
      border-radius: 50%;
      background: var(--if-color-primary);
    }
    .dropdown-item-body,
    .dropdown-item-date {
      color: var(--if-color-text-muted);
      font-size: var(--if-font-size-sm);
    }
    .dropdown-sr-only {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
    .dropdown-error {
      margin: 0;
      color: var(--if-color-danger);
    }
    .dropdown-all {
      align-self: flex-start;
      color: var(--if-color-primary);
    }
  `,
})
export class NotificationsDropdownComponent implements OnInit {
  protected readonly store = inject(NotificationsStore);
  private readonly transloco = inject(TranslocoService);

  protected readonly busyId = signal<string | null>(null);
  protected readonly markingAll = signal(false);
  protected readonly formatDate = formatNoticeDate;

  ngOnInit(): void {
    void this.store.refresh();
  }

  protected markReadLabel(item: NotificationResponse): string {
    return notificationsText(this.transloco, 'dropdown.markReadRow', { title: item.title ?? '' });
  }

  protected async markRead(item: NotificationResponse): Promise<void> {
    this.busyId.set(item.id ?? null);
    try {
      await this.store.markRead(item);
    } finally {
      this.busyId.set(null);
    }
  }

  protected async markAll(): Promise<void> {
    this.markingAll.set(true);
    try {
      await this.store.markAllRead();
    } finally {
      this.markingAll.set(false);
    }
  }
}
