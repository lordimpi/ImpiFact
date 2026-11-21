import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * ImpiFact wordmark above the auth card: the blue «IF» square and the product name, the same look as the sidebar
 * brand of the app shell (the auth pages render without the shell). Decorative mark, readable name.
 */
@Component({
  selector: 'feat-auth-brand',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="brand">
      <span class="brand__mark" aria-hidden="true">IF</span>
      <span class="brand__name">{{ 'auth.brand' | transloco }}</span>
    </p>
  `,
  styles: `
    .brand {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--if-space-2);
      margin: 0;
    }
    .brand__mark {
      display: inline-grid;
      place-items: center;
      inline-size: 2.25rem;
      block-size: 2.25rem;
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-bold);
      color: var(--if-color-on-primary);
      background: var(--if-color-primary);
      border-radius: var(--if-radius-md);
    }
    .brand__name {
      font-size: var(--if-font-size-lg);
      font-weight: var(--if-font-weight-bold);
      letter-spacing: -0.01em;
      color: var(--if-color-text);
    }
  `,
})
export class AuthBrandComponent {}
