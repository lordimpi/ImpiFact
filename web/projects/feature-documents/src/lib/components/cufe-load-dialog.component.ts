import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  effect,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { ButtonComponent, DialogComponent, FieldComponent, InputDirective } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';

/** The API takes a hexadecimal code of up to 96 characters (a CUFE is a SHA-384, 96 characters). */
const CUFE = /^[0-9a-fA-F]{1,96}$/;

/** Why a CUFE cannot be sent, as a key under `documents.received.load`; `null` when it can. */
export function cufeError(value: string): 'required' | 'format' | null {
  const cufe = value.trim();
  if (!cufe) return 'required';
  return CUFE.test(cufe) ? null : 'format';
}

/**
 * «Cargar por CUFE» (presentation): the CUFE field, checked before it goes, and the reason of a rejection from the API
 * (`error`) next to the field. It emits `submitted` with the CUFE trimmed and in lower case.
 */
@Component({
  selector: 'feat-cufe-load-dialog',
  imports: [TranslocoPipe, ButtonComponent, DialogComponent, FieldComponent, InputDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog
      [header]="'documents.received.load.header' | transloco"
      [closeLabel]="'documents.received.load.close' | transloco"
      width="36rem"
      [(visible)]="visible"
    >
      <form novalidate (submit)="submit($event)">
        <p class="cufe-help">{{ 'documents.received.load.help' | transloco }}</p>
        <ui-field
          [label]="'documents.received.load.field' | transloco"
          [help]="'documents.received.load.fieldHelp' | transloco"
          [error]="shownError()"
          required
        >
          <textarea
            uiInput
            rows="3"
            class="cufe-input"
            autocomplete="off"
            spellcheck="false"
            [placeholder]="'documents.received.load.placeholder' | transloco"
            [value]="value()"
            (input)="onInput($event)"
          ></textarea>
        </ui-field>
      </form>
      <div uiDialogFooter class="cufe-actions">
        <ui-button
          variant="secondary"
          [label]="'documents.received.load.cancel' | transloco"
          (pressed)="visible.set(false)"
        />
        <ui-button
          [label]="'documents.received.load.submit' | transloco"
          [loading]="busy()"
          (pressed)="submit()"
        />
      </div>
    </ui-dialog>
  `,
  styles: `
    .cufe-help {
      margin: 0 0 var(--if-space-3);
      color: var(--if-color-text-muted);
    }
    .cufe-input {
      font-family: var(--if-font-family-mono);
      font-size: var(--if-font-size-sm);
      resize: vertical;
      overflow-wrap: anywhere;
    }
    .cufe-actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class CufeLoadDialogComponent {
  readonly visible = model(false);
  readonly busy = input(false, { transform: booleanAttribute });
  /** Reason the API gave for not loading it, already as text. */
  readonly error = input<string | null>(null);
  /** Text of the client check, already translated, by key (`required`, `format`). */
  readonly messages = input.required<Record<'required' | 'format', string>>();
  readonly submitted = output<string>();

  protected readonly value = signal('');
  /** Client error after the first try; it clears while the person types. */
  private readonly clientError = signal<'required' | 'format' | null>(null);
  /** The API error belongs to the CUFE that was sent: it hides once the person changes it. */
  private readonly edited = signal(false);

  protected readonly shownError = computed(() => {
    const client = this.clientError();
    if (client) return this.messages()[client];
    return this.edited() ? null : this.error();
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        this.value.set('');
        this.clientError.set(null);
        this.edited.set(true);
      }
    });
  }

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLTextAreaElement).value);
    this.clientError.set(null);
    this.edited.set(true);
  }

  protected submit(event?: Event): void {
    event?.preventDefault();
    const problem = cufeError(this.value());
    this.clientError.set(problem);
    if (problem) {
      return;
    }
    this.edited.set(false);
    this.submitted.emit(this.value().trim().toLowerCase());
  }
}
