import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { PageTitleComponent } from '@impifact/ui';

/**
 * Home page for whoever does not see the dashboard (customer, platform): a welcome card. The owner and the seller
 * open the feature-dashboard dashboard on `/` (section 7 of the frontend architecture).
 */
@Component({
  selector: 'app-home-page',
  imports: [PageTitleComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-title>{{ 'home.title' | transloco }}</ui-page-title>
    <section class="home-card" aria-labelledby="home-welcome">
      <span class="home-card__mark" aria-hidden="true">IF</span>
      <div>
        <h2 id="home-welcome">{{ 'home.welcome' | transloco }}</h2>
        <p>{{ 'home.description' | transloco }}</p>
      </div>
    </section>
  `,
  styles: `
    .home-card {
      display: flex;
      align-items: flex-start;
      gap: var(--if-space-3);
      max-inline-size: 40rem;
      padding: var(--if-space-4);
      background: var(--if-color-surface);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-md);
      box-shadow: var(--if-shadow-card);
    }
    .home-card__mark {
      display: inline-grid;
      flex: none;
      place-items: center;
      inline-size: 2.5rem;
      block-size: 2.5rem;
      font-weight: var(--if-font-weight-bold);
      color: var(--if-sidebar-active-fg);
      background: var(--if-sidebar-active-bg);
      border-radius: var(--if-radius-md);
    }
    h2 {
      margin: 0 0 var(--if-space-1);
      font-size: var(--if-font-size-md);
    }
    p {
      margin: 0;
      color: var(--if-color-text-muted);
    }
  `,
})
export class HomePage {}
