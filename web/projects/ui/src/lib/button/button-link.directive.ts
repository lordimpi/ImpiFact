import { Directive, OnInit, effect, inject, input } from '@angular/core';
import { ButtonDirective } from '@openng/optimus-ui/button';
import { UiButtonSize, UiButtonVariant } from './button.component';

/**
 * A link (`<a>`, usually with `routerLink`) with the look of `ui-button`: same variants, sizes and focus ring,
 * because it renders through the same Optimus UI button styles. Use it for navigation that reads as an action,
 * such as «Emitir factura» in a page title; it stays a real link (opens in a new tab, announced as a link).
 *
 * `<a uiButtonLink variant="secondary" routerLink="/documentos/nota-credito">…</a>`
 */
@Directive({
  selector: 'a[uiButtonLink]',
  hostDirectives: [ButtonDirective],
  host: { class: 'ui-button-link' },
})
export class ButtonLinkDirective implements OnInit {
  readonly variant = input<UiButtonVariant>('primary');
  readonly size = input<UiButtonSize>('normal');

  private readonly button = inject(ButtonDirective, { self: true });

  constructor() {
    // Later changes: `severity` is the Optimus setter that recomputes every class once initialized.
    effect(() => this.apply());
  }

  /** The first classes are computed in Optimus' own `ngAfterViewInit`, so the values must be in place before it. */
  ngOnInit(): void {
    this.apply();
  }

  private apply(): void {
    const variant = this.variant();
    const size = this.size();
    this.button.text = variant === 'ghost' || variant === 'text';
    this.button.size = size === 'normal' ? undefined : size;
    this.button.severity = variant === 'secondary' || variant === 'danger' ? variant : undefined;
  }
}
