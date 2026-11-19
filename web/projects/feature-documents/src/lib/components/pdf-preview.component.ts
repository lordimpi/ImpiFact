import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ButtonComponent, DialogComponent } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Vista previa de un PDF dentro del panel: un diálogo con el visor del navegador y un botón para descargarlo. Se usa
 * en lugar de una pestaña nueva porque abrir una pestaña vacía y llenarla al llegar el PDF falla en silencio con
 * algunos bloqueadores y configuraciones del navegador (la pestaña queda en blanco). La URL temporal del PDF se libera
 * al cerrar el diálogo o al cambiar de PDF.
 */
@Component({
  selector: 'feat-pdf-preview',
  imports: [DialogComponent, ButtonComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog
      [header]="'documents.issue.previewTitle' | transloco"
      [closeLabel]="'documents.issue.previewClose' | transloco"
      [visible]="!!url()"
      (closed)="closed.emit()"
      width="min(64rem, 95vw)"
    >
      @if (url()) {
        <iframe
          #frame
          class="pdf-preview__frame"
          [title]="'documents.issue.previewTitle' | transloco"
        ></iframe>
      }
      <div uiDialogFooter>
        <ui-button
          [label]="'documents.issue.previewDownload' | transloco"
          (pressed)="download.emit()"
        />
      </div>
    </ui-dialog>
  `,
  styles: `
    .pdf-preview__frame {
      display: block;
      inline-size: 100%;
      block-size: min(75vh, 60rem);
      border: 0;
    }
  `,
})
export class PdfPreviewComponent {
  /** PDF a mostrar; `null` cierra el diálogo. */
  readonly pdf = input<Blob | null>(null);
  /** El diálogo se cerró: la página vuelve `pdf` a `null`. */
  readonly closed = output<void>();
  readonly download = output<void>();

  protected readonly url = signal<string | null>(null);
  private readonly frame = viewChild<ElementRef<HTMLIFrameElement>>('frame');

  constructor() {
    effect((onCleanup) => {
      const pdf = this.pdf();
      if (!pdf) {
        this.url.set(null);
        return;
      }
      const raw = URL.createObjectURL(pdf);
      this.url.set(raw);
      onCleanup(() => URL.revokeObjectURL(raw));
    });
    // La URL `blob:` la crea esta misma página con el PDF que devolvió el Api, así que se pone directo en el iframe en
    // lugar de pasar por `DomSanitizer`: inyectarlo sumaba al paquete inicial unos 8 kB (el saneador de HTML de
    // Angular, que vive en el chunk compartido de @angular/core).
    effect(() => {
      const frame = this.frame()?.nativeElement;
      const url = this.url();
      if (frame && url && frame.src !== url) {
        frame.src = url;
      }
    });
  }
}
