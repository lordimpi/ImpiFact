import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewEncapsulation,
  afterRenderEffect,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FileUpload } from '@openng/optimus-ui/fileupload';
import { linkToField, setAttribute } from '../field/field-control';
import { FieldComponent } from '../field/field.component';
import { OPTIMUS_TEXTS_ES, applyOptimusTexts } from '../field/optimus-texts';
import { UI_TEXTS } from '../i18n/ui-texts';

let nextId = 0;

const SIZE_UNITS = ['B', 'KB', 'MB', 'GB'];
const SIZE_FORMAT = new Intl.NumberFormat('es-CO', { maximumFractionDigits: 1 });

/** Size of a file in Spanish, with the largest unit that keeps it above 1 («1,5 MB»). */
export function formatFileSize(bytes: number): string {
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < SIZE_UNITS.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${SIZE_FORMAT.format(value)} ${SIZE_UNITS[unit]}`;
}

/**
 * File chooser of Optimus UI (`p-fileupload` in basic mode): a «Seleccionar archivo» button and the name of the chosen
 * file. It does not upload anything: it emits the chosen `File` (`fileChange`, `null` when it is rejected or cleared)
 * and the page sends it with its form. It checks the type (`accept`, extensions or MIME types as in `<input
 * type="file">`) and the size (`maxFileSize`, in bytes) and says in Spanish why a file is rejected. Inside `ui-field` the
 * button takes the id (the label points to it), the name («label + Seleccionar archivo») and the description
 * (explanation, hint, error, file name and rejection).
 *
 * Usage:
 * ```html
 * <ui-field [label]="'companies.certificate.file' | transloco" [error]="fileError()" required>
 *   <ui-file-upload accept=".p12,.pfx" [maxFileSize]="maxCertificateSize" (fileChange)="onFile($event)" />
 * </ui-field>
 * ```
 * `clear()` forgets the chosen file (for example after sending it).
 */
@Component({
  selector: 'ui-file-upload',
  imports: [FileUpload],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // No encapsulation: the Optimus file upload keeps its own classes; ours carry the ui-file-upload prefix.
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ui-file-upload">
      <p-fileupload
        mode="basic"
        [auto]="false"
        [customUpload]="true"
        [disabled]="disabled()"
        [chooseLabel]="chooseLabel()"
        (onSelect)="onSelect($event.currentFiles)"
      >
        <ng-template #filelabel>
          <span class="ui-file-upload__name" [id]="nameId">{{ file()?.name ?? noFile }}</span>
        </ng-template>
      </p-fileupload>
      @if (rejection(); as message) {
        <p class="ui-file-upload__error" role="alert" [id]="errorId">{{ message }}</p>
      }
    </div>
  `,
  styles: `
    .ui-file-upload {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
    }
    .ui-file-upload .p-fileupload-basic-content {
      flex-wrap: wrap;
    }
    .ui-file-upload__name {
      color: var(--if-color-text-muted);
      overflow-wrap: anywhere;
    }
    .ui-file-upload__error {
      margin: 0;
      font-size: var(--if-font-size-sm);
      color: var(--if-color-danger);
    }
  `,
})
export class FileUploadComponent {
  /** Accepted types, as the `accept` attribute of `<input type="file">` (`.p12,.pfx` or `image/*`). */
  readonly accept = input<string>();
  /** Largest size accepted, in bytes. */
  readonly maxFileSize = input<number>();
  /** Text of the button; by default «Seleccionar archivo» (`UI_TEXTS.optimus.choose`). */
  readonly chooseLabel = input<string>();
  readonly disabled = input(false, { transform: booleanAttribute });
  /** The chosen file, or `null` when it was rejected or cleared. */
  readonly fileChange = output<File | null>();

  protected readonly field = inject(FieldComponent, { optional: true });
  private readonly texts = inject(UI_TEXTS);
  protected readonly ownId = `ui-file-upload-${nextId++}`;
  protected readonly nameId = `${this.ownId}-name`;
  protected readonly errorId = `${this.ownId}-error`;
  protected readonly noFile = (this.texts.optimus ?? OPTIMUS_TEXTS_ES).noFileChosenMessage;

  private readonly upload = viewChild.required(FileUpload);
  private readonly chosen = signal<File | null>(null);
  /** The chosen file. */
  readonly file = this.chosen.asReadonly();
  protected readonly rejection = signal<string | null>(null);

  private readonly acceptList = computed(() =>
    (this.accept() ?? '')
      .split(',')
      .map((type) => type.trim())
      .filter((type) => type !== ''),
  );

  constructor() {
    applyOptimusTexts();
    linkToField(this.field, {
      control: 'button',
      describedBy: () => [this.nameId, this.rejection() ? this.errorId : null].join(' ').trim(),
    });
    // Optimus validates with English messages and three-decimal sizes, so `accept` goes straight to the input (it
    // filters the file dialog) and this component validates; the button gets the id the label points to.
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    afterRenderEffect({
      write: () => {
        const button = host.querySelector('button');
        const id = this.field?.controlId ?? this.ownId;
        setAttribute(button, 'id', id);
        // Name: the field label and the text of the button («Certificado Seleccionar archivo»).
        const text = button?.querySelector('.p-button-label') ?? null;
        setAttribute(text, 'id', `${id}-text`);
        setAttribute(
          button,
          'aria-labelledby',
          this.field ? `${this.field.labelId} ${id}-text` : null,
        );
        // The hidden input lives inside the button: only the button is the control.
        const fileInput = host.querySelector('input[type="file"]');
        setAttribute(fileInput, 'accept', this.accept() ?? null);
        setAttribute(fileInput, 'aria-hidden', 'true');
        setAttribute(fileInput, 'tabindex', '-1');
      },
    });
  }

  /** Forgets the chosen file. */
  clear(): void {
    this.upload().clear();
    this.chosen.set(null);
    this.rejection.set(null);
  }

  protected onSelect(files: readonly File[]): void {
    const file = files[0] ?? null;
    const rejection = file ? this.reject(file) : null;
    this.rejection.set(rejection);
    if (rejection) {
      this.upload().clear();
    }
    const chosen = rejection ? null : file;
    this.chosen.set(chosen);
    this.fileChange.emit(chosen);
  }

  /** Why the file cannot be used, or `null` when it can. */
  private reject(file: File): string | null {
    const accepted = this.acceptList();
    if (accepted.length > 0 && !accepted.some((type) => matchesType(file, type))) {
      const list = accepted.join(', ');
      return (
        this.texts.fileWrongType?.(file.name, list) ??
        `«${file.name}» no es de un tipo permitido. Use un archivo ${list}.`
      );
    }
    const max = this.maxFileSize();
    if (max !== undefined && file.size > max) {
      const [size, limit] = [formatFileSize(file.size), formatFileSize(max)];
      return (
        this.texts.fileTooLarge?.(file.name, size, limit) ??
        `«${file.name}» pesa ${size} y el máximo es ${limit}.`
      );
    }
    return null;
  }
}

/** Whether a file matches one entry of `accept`: an extension (`.p12`), a MIME type or a family (`image/*`). */
function matchesType(file: File, type: string): boolean {
  const wanted = type.toLowerCase();
  if (wanted.startsWith('.')) {
    return file.name.toLowerCase().endsWith(wanted);
  }
  if (wanted.endsWith('/*')) {
    return file.type.toLowerCase().startsWith(wanted.slice(0, -1));
  }
  return file.type.toLowerCase() === wanted;
}
