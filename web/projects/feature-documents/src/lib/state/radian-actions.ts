import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  Api,
  DianEventListResponse,
  ReceivedDocumentResponse,
  SendEventBody,
  getDocumentDianEvents,
  getReceivedDocumentDianEvents,
  loadReceivedDocument,
  sendDocumentEvent,
  sendReceivedDocumentEvent,
} from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST, ToastStore } from '@impifact/core';
import { UiConfirm } from '@impifact/ui';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, firstValueFrom } from 'rxjs';
import { DOCUMENTS_I18N_SCOPE } from '../documents-i18n';
import { EventBlock, RadianEventCode, eventNameKey } from '../models/radian-events';

/*
 * RADIAN actions shared by the received invoice and the own invoice: send an event (always after the confirm dialog,
 * because the DIAN never undoes one), ask the DIAN which events it has, and load a received invoice by its CUFE. The
 * API remains the authority: its 422 (order of the events) and 403 (plan without RADIAN events) are explained in a
 * toast. The page reloads its data after each send, whatever the answer.
 */

/** Where the event goes: a received invoice (030 to 033) or an own invoice (034). */
export type EventTarget = 'received' | 'own';

/**
 * What happened with a send: the DIAN answer (`accepted`, `rejected`, `unconfirmed`), `cancelled` when the person
 * stepped back in the confirm dialog, `notFound` when the invoice is gone and `failed` for any other error.
 */
export type SendOutcome =
  'accepted' | 'rejected' | 'unconfirmed' | 'cancelled' | 'notFound' | 'failed';

/** Result of loading by CUFE: the invoice and whether it was new (201) or already loaded (200), or the error text. */
export type LoadResult =
  | { ok: true; document: ReceivedDocumentResponse; created: boolean }
  | { ok: false; message: string };

const FORBIDDEN = 403;
const NOT_FOUND = 404;
const CREATED = 201;

/** Status of the event in the answer; anything else counts as unconfirmed, as the API documents. */
function outcomeOf(status: string | null | undefined): 'accepted' | 'rejected' | 'unconfirmed' {
  return status === 'accepted' || status === 'rejected' ? status : 'unconfirmed';
}

@Injectable()
export class RadianActions {
  private readonly api = inject(Api);
  private readonly confirm = inject(UiConfirm);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  /**
   * Asks the confirm dialog and, when the person accepts, sends the event `body.event_code` to the invoice `id`.
   * `number` names the invoice in the question.
   */
  async send(
    target: EventTarget,
    id: string,
    number: string,
    body: SendEventBody & { event_code: RadianEventCode },
  ): Promise<SendOutcome> {
    const event = this.text(eventNameKey(body.event_code), { code: body.event_code });
    const go = await this.confirm.ask({
      header: this.text('radian.confirm.header', { event }),
      message: this.text('radian.confirm.message', { event, number }),
      acceptLabel: this.text('radian.confirm.accept'),
      rejectLabel: this.text('radian.confirm.reject'),
    });
    if (!go) {
      return 'cancelled';
    }
    const context = new HttpContext().set(SKIP_ERROR_TOAST, true);
    const request =
      target === 'received'
        ? this.api.invoke(sendReceivedDocumentEvent, { id, body }, context)
        : this.api.invoke(sendDocumentEvent, { id, body }, context);
    try {
      const answer = await firstValueFrom(request);
      const outcome = outcomeOf(answer.event?.status);
      this.toasts.show({
        kind: outcome === 'accepted' ? 'success' : outcome === 'rejected' ? 'error' : 'warning',
        text: this.text(`radian.sent.${outcome}`, { event }),
      });
      return outcome;
    } catch (error) {
      this.toasts.show({
        kind: 'error',
        text: this.errorText(error),
        correlationId: error instanceof AppError ? error.correlationId : null,
      });
      return error instanceof AppError && error.status === NOT_FOUND ? 'notFound' : 'failed';
    }
  }

  /**
   * What the DIAN has registered on the invoice (GetStatusEvent), or `null` when it could not be asked: a 503 (the
   * DIAN did not answer) gets a clear toast; other errors get the toast of core.
   */
  async dianEvents(target: EventTarget, id: string): Promise<DianEventListResponse | null> {
    const context = new HttpContext().set(SKIP_ERROR_TOAST, true);
    const request: Observable<DianEventListResponse> =
      target === 'received'
        ? this.api.invoke(getReceivedDocumentDianEvents, { id }, context)
        : this.api.invoke(getDocumentDianEvents, { id }, context);
    try {
      return await firstValueFrom(request);
    } catch (error) {
      const unavailable = error instanceof AppError && error.status === 503;
      this.toasts.show({
        kind: 'error',
        text: unavailable ? this.text('radian.dian.unavailable') : this.errorText(error),
        correlationId: error instanceof AppError ? error.correlationId : null,
      });
      return null;
    }
  }

  /** Loads a received invoice from the DIAN by its CUFE; the error comes back as text for the dialog field. */
  async loadByCufe(cufe: string): Promise<LoadResult> {
    try {
      const response = await firstValueFrom(
        this.api.invoke$Response(
          loadReceivedDocument,
          { body: { cufe } },
          new HttpContext().set(SKIP_ERROR_TOAST, true),
        ),
      );
      return { ok: true, document: response.body, created: response.status === CREATED };
    } catch (error) {
      return { ok: false, message: this.errorText(error) };
    }
  }

  /** Why an event is not available, for the text under its button. */
  blockText(block: EventBlock): string {
    if (block.reason === 'registered') {
      return this.text('radian.blocked.registered');
    }
    const event = this.text(eventNameKey(block.code), { code: block.code });
    return this.text(`radian.blocked.${block.reason}`, { event });
  }

  /** Text of an API error: the plan without RADIAN events, the field errors or the API message as it comes. */
  private errorText(error: unknown): string {
    if (!(error instanceof AppError)) {
      return this.transloco.translate('errors.unexpected');
    }
    // The PHP text of the plan speaks of documents; the panel names the RADIAN events the person was sending.
    if (error.status === FORBIDDEN && error.code === 'plan.limit_reached') {
      return this.text('radian.quota');
    }
    if (error.hasFieldErrors) {
      return Object.values(error.fieldErrors).flat().join(' ');
    }
    return error.message;
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`, params);
  }
}
