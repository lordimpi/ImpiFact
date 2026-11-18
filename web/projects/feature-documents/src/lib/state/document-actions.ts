import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  Api,
  ApiConfiguration,
  StrictHttpResponse,
  downloadDocumentFile$Pdf,
  downloadDocumentFile$Xml,
  downloadDocumentFile$Zip,
  exportDocumentsCsv,
  regenerateAttachedDocument,
  regenerateDocumentPdf,
  resendDocumentEmail,
} from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST, ToastStore } from '@impifact/core';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, firstValueFrom } from 'rxjs';
import { DOCUMENTS_I18N_SCOPE } from '../documents-i18n';
import { DocumentFilters } from './list-filters';

/*
 * Acciones sobre documentos que comparten la lista y el detalle: descargas, exportación CSV (valores separados por
 * coma), reenvío del correo y regeneración de archivos. El token vive solo en memoria, así que los archivos se piden
 * con HttpClient (pasan por los interceptores con el Bearer) y se guardan desde un `Blob`, nunca con un enlace
 * directo al Api. Los errores del Api los avisa core con un *toast*.
 */

export type DocumentFileKind = 'xml' | 'pdf' | 'attached-document' | 'zip';

/** Resultado de abrir una vista previa: el error del Api (ya como `AppError`) si no se pudo. */
export type PreviewResult = { ok: true } | { ok: false; error: unknown };

/** Cómo mostrar un error de la vista previa: junto a los totales (errores por campo) o como aviso de la vista previa. */
export interface PreviewProblem {
  /** Validación del cuerpo, igual que al emitir. */
  validation: { message: string; details: string[] } | null;
  /** Clave del texto (`documents.*`) o mensaje del Api para el aviso bajo el botón. */
  notice: { key: string } | { message: string } | null;
}

/** Un 503 es el conversor de PDF caído; un 422 con campos, el cuerpo no válido; lo demás, el mensaje del Api. */
export function previewProblem(error: unknown): PreviewProblem {
  if (error instanceof AppError && error.hasFieldErrors) {
    return {
      validation: { message: error.message, details: Object.values(error.fieldErrors).flat() },
      notice: null,
    };
  }
  if (error instanceof AppError && error.status === 503) {
    return { validation: null, notice: { key: 'issue.previewUnavailable' } };
  }
  return {
    validation: null,
    notice: error instanceof AppError ? { message: error.message } : { key: 'issue.previewFailed' },
  };
}

/** Tiempo que la pestaña de la vista previa tiene para leer el PDF antes de liberar su URL temporal. */
const PREVIEW_URL_LIFETIME_MS = 60_000;

const EXTENSIONS: Record<DocumentFileKind, string> = {
  xml: 'xml',
  pdf: 'pdf',
  'attached-document': 'xml',
  zip: 'zip',
};

/** Nombre del archivo en `Content-Disposition`; el Api solo lo deja leer si lo expone por CORS. */
export function fileNameFrom(response: HttpResponse<unknown>, fallback: string): string {
  const header = response.headers.get('Content-Disposition') ?? '';
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header)?.[1];
  if (encoded) {
    try {
      return decodeURIComponent(encoded);
    } catch {
      // Un nombre mal codificado no impide la descarga: se usa el de respaldo.
    }
  }
  return /filename="?([^";]+)"?/i.exec(header)?.[1] ?? fallback;
}

/** Parámetros de `exportDocumentsCsv`: los mismos filtros de la lista, sin paginación. */
export function exportParams(filters: DocumentFilters): HttpParams {
  let params = new HttpParams();
  const single: [string, string | undefined][] = [
    ['from', filters.from],
    ['to', filters.to],
    ['kind', filters.kind],
    ['customer', filters.customer],
    ['number', filters.number],
  ];
  for (const [name, value] of single) {
    if (value) params = params.set(name, value);
  }
  for (const status of filters.status ?? []) {
    params = params.append('status', status);
  }
  return params;
}

@Injectable()
export class DocumentActions {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  /** Descarga un archivo del documento y lo guarda. Devuelve si se descargó. */
  async download(
    doc: { id?: string; full_number?: string },
    kind: DocumentFileKind,
  ): Promise<boolean> {
    if (!doc.id) {
      return false;
    }
    const params = { id: doc.id, kind };
    // El cliente generado tipa el cuerpo como texto, pero lo pide como `blob`.
    const request: Observable<StrictHttpResponse<string>> =
      kind === 'pdf'
        ? this.api.invoke$Response(downloadDocumentFile$Pdf, params)
        : kind === 'zip'
          ? this.api.invoke$Response(downloadDocumentFile$Zip, params)
          : this.api.invoke$Response(downloadDocumentFile$Xml, params);
    try {
      const response = await firstValueFrom(request);
      const base = kind === 'attached-document' ? 'Attachment' : (doc.full_number ?? doc.id);
      this.save(
        response.body as unknown as Blob,
        fileNameFrom(response, `${base}.${EXTENSIONS[kind]}`),
      );
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Exporta a CSV con los filtros de la lista. El generado de `exportDocumentsCsv` descarta el cuerpo, así que se
   * pide con HttpClient a su misma ruta. Sobre 50.000 filas el Api responde 422 `documents.export_too_large`.
   */
  async exportCsv(filters: DocumentFilters): Promise<boolean> {
    const url = `${this.config.rootUrl}${exportDocumentsCsv.PATH}`;
    try {
      const response = await firstValueFrom(
        this.http.get(url, {
          params: exportParams(filters),
          responseType: 'blob',
          observe: 'response',
          context: new HttpContext().set(SKIP_ERROR_TOAST, true),
        }),
      );
      this.save(response.body ?? new Blob([]), fileNameFrom(response, 'documentos.csv'));
      this.notify('success', 'list.exported');
      return true;
    } catch (error) {
      const tooLarge = error instanceof AppError && error.code === 'documents.export_too_large';
      this.toasts.show({
        kind: 'error',
        text: tooLarge
          ? this.text('list.exportTooLarge')
          : error instanceof AppError
            ? error.message
            : this.text('notices.downloadFailed'),
        correlationId: error instanceof AppError ? error.correlationId : null,
      });
      return false;
    }
  }

  /**
   * Pide el PDF de vista previa (`path`, por ejemplo `previewInvoice.PATH`) con el cuerpo de la emisión y lo abre en
   * una pestaña nueva. La pestaña se abre ya, dentro del clic: abrirla al llegar la respuesta la bloquearía el
   * navegador como ventana emergente. El generado descarta el cuerpo, así que se pide con HttpClient como `blob`.
   * Si falla, cierra la pestaña y devuelve el error sin avisar: la página lo explica.
   */
  async openPreview(path: string, body: unknown): Promise<PreviewResult> {
    const tab = this.document.defaultView?.open('', '_blank') ?? null;
    try {
      const blob = await firstValueFrom(
        this.http.post(`${this.config.rootUrl}${path}`, body, {
          responseType: 'blob',
          context: new HttpContext().set(SKIP_ERROR_TOAST, true),
        }),
      );
      if (tab) {
        const url = URL.createObjectURL(blob);
        tab.location.href = url;
        setTimeout(() => URL.revokeObjectURL(url), PREVIEW_URL_LIFETIME_MS);
      } else {
        // Sin pestaña (el navegador la bloqueó igual): se descarga.
        this.save(blob, 'vista-previa.pdf');
      }
      return { ok: true };
    } catch (error) {
      tab?.close();
      return { ok: false, error };
    }
  }

  resendEmail(id: string): Promise<boolean> {
    return this.queue(this.api.invoke(resendDocumentEmail, { id }), 'notices.emailQueued');
  }

  regeneratePdf(id: string): Promise<boolean> {
    return this.queue(this.api.invoke(regenerateDocumentPdf, { id }), 'notices.pdfQueued');
  }

  regenerateAttached(id: string): Promise<boolean> {
    return this.queue(
      this.api.invoke(regenerateAttachedDocument, { id }),
      'notices.attachedQueued',
    );
  }

  private async queue(request: Observable<unknown>, successKey: string): Promise<boolean> {
    try {
      await firstValueFrom(request);
      this.notify('success', successKey);
      return true;
    } catch {
      return false;
    }
  }

  private save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    this.document.body.appendChild(link);
    link.click();
    link.remove();
    // Se libera después del clic, cuando el navegador ya tomó el archivo.
    setTimeout(() => URL.revokeObjectURL(url));
  }

  private notify(kind: 'success' | 'info', key: string): void {
    this.toasts.show({ kind, text: this.text(key) });
  }

  private text(key: string): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`);
  }
}
