import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TabsComponent, UiTab } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { platformText } from '../platform-i18n';

/** Tabs between the platform sections (plans and companies); each stays active on its child pages. */
@Component({
  selector: 'feat-platform-nav',
  imports: [TabsComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-tabs [label]="'platform.nav.label' | transloco" [tabs]="tabs" />`,
})
export class PlatformNavComponent {
  private readonly transloco = inject(TranslocoService);

  protected readonly tabs: readonly UiTab[] = [
    { label: platformText(this.transloco, 'nav.plans'), link: '/plataforma/planes', exact: false },
    {
      label: platformText(this.transloco, 'nav.companies'),
      link: '/plataforma/empresas',
      exact: false,
    },
  ];
}
