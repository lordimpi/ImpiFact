import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { EventPersonBody, SendEventBody } from '@impifact/api';
import {
  ButtonComponent,
  CheckboxComponent,
  DialogComponent,
  FieldComponent,
  InputDirective,
  SelectComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { DOCUMENTS_I18N_SCOPE } from '../documents-i18n';
import { CLAIM, CLAIM_REASONS, ReceivedEventCode } from '../models/radian-events';

/** The API takes up to 100 characters in each field of `issuer_party`. */
const MAX_LENGTH = 100;

const PERSON_FIELDS = [
  'identification_number',
  'first_name',
  'last_name',
  'job_title',
  'organization_department',
] as const;

type PersonField = (typeof PERSON_FIELDS)[number];

/** Text keys of each person field: label and placeholder under `documents.radian.dialog`. */
const PERSON_KEYS: Record<PersonField, string> = {
  identification_number: 'identification',
  first_name: 'firstName',
  last_name: 'lastName',
  job_title: 'jobTitle',
  organization_department: 'department',
};

const AUTOCOMPLETE: Record<PersonField, string> = {
  identification_number: 'off',
  first_name: 'given-name',
  last_name: 'family-name',
  job_title: 'organization-title',
  organization_department: 'off',
};

/** The event to send, ready for `RadianActions.send`. */
export type EventRequest = SendEventBody & { event_code: ReceivedEventCode };

/**
 * Dialog of an event on a received invoice (presentation): it explains the event, asks the claim reason of a 031 and,
 * optionally, the person who receives or decides (`issuer_party`; without it the API writes the company). It checks
 * the fields and emits `submitted`; the page asks the confirm dialog and sends it.
 */
@Component({
  selector: 'feat-radian-event-dialog',
  imports: [
    TranslocoPipe,
    ButtonComponent,
    CheckboxComponent,
    DialogComponent,
    FieldComponent,
    InputDirective,
    SelectComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (code(); as code) {
      <ui-dialog
        [header]="'documents.radian.dialog.header.' + code | transloco"
        [closeLabel]="'documents.radian.dialog.close' | transloco"
        width="36rem"
        [(visible)]="visible"
      >
        <form novalidate class="event-form" (submit)="submit($event)">
          <p class="event-form__explain">
            {{ 'documents.radian.dialog.explain.' + code | transloco }}
          </p>
          @if (code === claim) {
            <ui-field
              [label]="'documents.radian.dialog.reason' | transloco"
              [error]="showErrors() ? reasonError() : null"
              required
            >
              <ui-select
                [options]="reasons()"
                [placeholder]="'documents.radian.dialog.reasonPlaceholder' | transloco"
                [value]="reason()"
                (valueChange)="reason.set($event)"
              />
            </ui-field>
          }
          <ui-checkbox
            [checked]="withPerson()"
            (checkedChange)="withPerson.set($event)"
            [label]="'documents.radian.dialog.withPerson' | transloco"
          />
          @if (withPerson()) {
            <fieldset class="event-form__person">
              <legend>{{ 'documents.radian.dialog.person' | transloco }}</legend>
              <div class="event-form__grid">
                @for (field of personFields; track field) {
                  <ui-field
                    [label]="'documents.radian.dialog.' + keys[field] | transloco"
                    [error]="showErrors() ? personErrors()[field] : null"
                    required
                  >
                    <input
                      uiInput
                      type="text"
                      [attr.autocomplete]="autocomplete[field]"
                      [placeholder]="
                        'documents.radian.dialog.' + keys[field] + 'Placeholder' | transloco
                      "
                      [value]="person()[field]"
                      (input)="setPerson(field, $event)"
                    />
                  </ui-field>
                }
              </div>
            </fieldset>
          } @else {
            <p class="event-form__hint">
              {{ 'documents.radian.dialog.withoutPerson' | transloco }}
            </p>
          }
        </form>
        <div uiDialogFooter class="event-form__actions">
          <ui-button
            variant="secondary"
            [label]="'documents.radian.dialog.cancel' | transloco"
            (pressed)="visible.set(false)"
          />
          <ui-button
            [label]="'documents.radian.dialog.submit' | transloco"
            [loading]="busy()"
            (pressed)="submit()"
          />
        </div>
      </ui-dialog>
    }
  `,
  styles: `
    .event-form__explain,
    .event-form__hint {
      margin: 0 0 var(--if-space-3);
    }
    .event-form__hint {
      margin: var(--if-space-2) 0 0;
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .event-form__person {
      margin: var(--if-space-3) 0 0;
      padding: 0;
      border: 0;
      min-inline-size: 0;
    }
    .event-form__person legend {
      padding: 0;
      margin-block-end: var(--if-space-2);
      font-weight: var(--if-font-weight-medium);
    }
    .event-form__grid {
      display: grid;
      gap: 0 var(--if-space-3);
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 13rem), 1fr));
    }
    .event-form__actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class RadianEventDialogComponent {
  private readonly transloco = inject(TranslocoService);

  /** Event of the dialog; `null` renders nothing. */
  readonly code = input<ReceivedEventCode | null>(null);
  readonly visible = model(false);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly submitted = output<EventRequest>();

  protected readonly claim = CLAIM;
  protected readonly personFields = PERSON_FIELDS;
  protected readonly keys = PERSON_KEYS;
  protected readonly autocomplete = AUTOCOMPLETE;

  protected readonly reasons = computed(() =>
    CLAIM_REASONS.map((id) => ({
      id,
      name: `${id} · ${this.text(`radian.claimReasons.${id}`)}`,
    })),
  );

  protected readonly reason = signal('');
  protected readonly withPerson = signal(false);
  protected readonly person = signal<Record<PersonField, string>>(emptyPerson());
  /** Errors show after the first try to continue, like the other forms of the panel. */
  protected readonly showErrors = signal(false);

  protected readonly reasonError = computed(() =>
    this.reason() ? null : this.text('radian.dialog.reasonRequired'),
  );

  protected readonly personErrors = computed(() => {
    const errors: Partial<Record<PersonField, string>> = {};
    for (const field of PERSON_FIELDS) {
      const value = this.person()[field].trim();
      if (!value) {
        errors[field] = this.text('radian.dialog.required');
      } else if (value.length > MAX_LENGTH) {
        errors[field] = this.text('radian.dialog.tooLong');
      }
    }
    return errors;
  });

  constructor() {
    // Each time it opens for an event, the form starts clean.
    effect(() => {
      if (this.visible()) {
        this.reason.set('');
        this.withPerson.set(false);
        this.person.set(emptyPerson());
        this.showErrors.set(false);
      }
    });
  }

  protected setPerson(field: PersonField, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.person.update((person) => ({ ...person, [field]: value }));
  }

  protected submit(event?: Event): void {
    event?.preventDefault();
    const code = this.code();
    if (!code) {
      return;
    }
    const claimInvalid = code === CLAIM && this.reasonError() !== null;
    const personInvalid = this.withPerson() && Object.keys(this.personErrors()).length > 0;
    if (claimInvalid || personInvalid) {
      this.showErrors.set(true);
      return;
    }
    const request: EventRequest = { event_code: code };
    if (code === CLAIM) {
      request.claim_reason = this.reason();
    }
    if (this.withPerson()) {
      const person = this.person();
      const issuer: EventPersonBody = {};
      for (const field of PERSON_FIELDS) {
        issuer[field] = person[field].trim();
      }
      request.issuer_party = issuer;
    }
    this.submitted.emit(request);
  }

  private text(key: string): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`);
  }
}

function emptyPerson(): Record<PersonField, string> {
  return {
    identification_number: '',
    first_name: '',
    last_name: '',
    job_title: '',
    organization_department: '',
  };
}
