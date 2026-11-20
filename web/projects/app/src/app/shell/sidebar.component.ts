import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@impifact/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { injectCurrentPath } from './current-path';
import { NAV_GROUPS, NavItem, activeNavPath } from './nav-items';

/** Elements of the drawer that take the focus with Tab. */
const FOCUSABLE = 'a[href], button:not([disabled])';

/**
 * Sidebar of the panel: brand and main menu grouped by section, filtered by role and scope. From 1024px it is fixed;
 * below that it is an off-canvas drawer (`open`) that the top bar toggles. Escape, the close button and choosing a
 * link ask the shell to close it (`closed`). A closed drawer is hidden with `visibility`, so its links leave the tab
 * order. An open drawer is modal: the shell makes the page behind it inert and Tab and Shift+Tab cycle inside it.
 */
@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    id: 'app-sidebar',
    class: 'sidebar',
    '[class.is-open]': 'open()',
    '(keydown.escape)': 'open() && closed.emit(true)',
    '(keydown.tab)': 'trapFocus($event)',
    '(keydown.shift.tab)': 'trapFocus($event)',
  },
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  private readonly session = inject(SessionStore);
  private readonly path = injectCurrentPath();
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Drawer state on narrow screens; ignored from 1024px. */
  readonly open = input(false);
  /** Asks to close the drawer; `true` when the focus should go back to the menu button. */
  readonly closed = output<boolean>();

  /** Sections with the entries that match the session; entries that need a role only show with a session. */
  protected readonly groups = computed(() =>
    NAV_GROUPS.map((group) => ({
      ...group,
      items: group.items.filter((item) => this.allowed(item)),
    })).filter((group) => group.items.length > 0),
  );

  protected readonly activePath = computed(() =>
    activeNavPath(
      this.groups().flatMap((group) => group.items),
      this.path(),
    ),
  );

  /** Moves the focus to the first or the last focusable element from the opposite end of the open drawer. */
  protected trapFocus(event: Event): void {
    if (!this.open()) {
      return;
    }
    const focusable = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>(FOCUSABLE));
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (!first || !last) {
      return;
    }
    // Inside the drawer the browser moves the focus; only at an edge would it leave the drawer.
    const backwards = (event as KeyboardEvent).shiftKey;
    if (document.activeElement === (backwards ? first : last)) {
      event.preventDefault();
      (backwards ? last : first).focus();
    }
  }

  private allowed(item: NavItem): boolean {
    return (
      (!item.roles || (this.session.isAuthenticated() && this.session.hasRole(...item.roles))) &&
      (!item.scope || this.session.hasScope(item.scope))
    );
  }
}
