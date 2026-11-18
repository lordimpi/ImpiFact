import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { CreatedApiKey } from '@impifact/api';
import { AppError, SessionStore, ToastStore } from '@impifact/core';
import {
  ButtonComponent,
  DialogComponent,
  FieldComponent,
  InputDirective,
  PageTitleComponent,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { apiKeysText, provideApiKeysI18n } from '../api-keys-i18n';
import {
  COMPANY_SCOPES,
  NAME_MAX_LENGTH,
  apiKeyBody,
  createApiKeyForm,
  emptyApiKey,
} from '../forms/api-key.form';
import { ApiKeysStore, SessionApiKey } from '../state/api-keys.store';

const DATE = new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeZone: 'America/Bogota' });

/**
 * Página `/llaves-api`: crea llaves con los alcances que la sesión ya tiene, muestra la llave completa **una sola
 * vez** en un diálogo con botón de copiar (y la borra de la memoria al cerrarlo) y revoca con confirmación. El Api no
 * lista las llaves: la tabla muestra solo las creadas en esta sesión, sin su texto completo.
 */
@Component({
  selector: 'feat-api-keys-page',
  imports: [
    FormField,
    TranslocoPipe,
    ButtonComponent,
    DialogComponent,
    FieldComponent,
    InputDirective,
    PageTitleComponent,
    TableComponent,
    TableCellDirective,
  ],
  providers: [provideApiKeysI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './api-keys.page.html',
  styleUrl: './api-keys.page.css',
})
export class ApiKeysPage {
  protected readonly store = inject(ApiKeysStore);
  private readonly session = inject(SessionStore);
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastStore);

  private readonly model = signal(emptyApiKey());
  protected readonly keyForm = createApiKeyForm(this.model);
  protected readonly selectedScopes = signal<readonly string[]>([]);
  protected readonly triedSubmit = signal(false);
  protected readonly generalError = signal<string | null>(null);
  protected readonly submitting = computed(() => this.keyForm().submitting());

  /** La llave recién creada con su texto completo; solo vive mientras el diálogo está abierto. */
  protected readonly secret = signal<CreatedApiKey | null>(null);
  protected readonly secretOpen = computed(() => this.secret() !== null);
  protected readonly copied = signal(false);

  protected readonly toRevoke = signal<SessionApiKey | null>(null);
  protected readonly revokeOpen = signal(false);

  /** Alcances que se pueden dar: los de la sesión, en orden fijo. */
  protected readonly availableScopes = computed(() =>
    COMPANY_SCOPES.filter((scope) => this.session.hasScope(scope)),
  );

  protected readonly nameError = computed(() => {
    const state = this.keyForm.name();
    const error = state.touched() ? state.errors()[0] : undefined;
    if (!error) {
      return null;
    }
    if (error.kind === 'required') {
      return this.text('form.nameRequired');
    }
    if (error.kind === 'maxLength') {
      return this.text('form.nameTooLong', { max: NAME_MAX_LENGTH });
    }
    return error.message ?? null;
  });
  protected readonly scopesError = computed(() =>
    this.triedSubmit() && this.selectedScopes().length === 0
      ? this.text('form.scopesRequired')
      : null,
  );

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'name', header: this.text('list.name') },
    { key: 'keyPrefix', header: this.text('list.prefix') },
    { key: 'scopes', header: this.text('list.scopes') },
    { key: 'expiresAt', header: this.text('list.expiresAt') },
    { key: 'revoked', header: this.text('list.status') },
    { key: 'actions', header: this.text('list.actions') },
  ]);
  protected readonly tableLabels = computed<UiTableLabels>(() => ({
    loading: '',
    emptyTitle: this.text('list.emptyTitle'),
    emptyDescription: this.text('list.emptyDescription'),
    errorTitle: '',
    retry: '',
    paginator: { navigation: '', previous: '', next: '', page: '' },
  }));
  protected readonly revokeLabels = computed(() => ({
    header: this.text('revoke.title'),
    body: this.text('revoke.body', { name: this.toRevoke()?.name ?? '' }),
  }));

  protected readonly trackById = (_: number, row: SessionApiKey) => row.id;

  protected scopeLabel(scope: string): string {
    return this.text(`scopes.${scope}`);
  }

  protected scopeList(row: SessionApiKey): string {
    return row.scopes.map((scope) => this.scopeLabel(scope)).join(', ');
  }

  protected expiry(row: SessionApiKey): string {
    return row.expiresAt ? DATE.format(new Date(row.expiresAt)) : this.text('list.never');
  }

  protected rowText(key: string, row: SessionApiKey): string {
    return this.text(key, { name: row.name });
  }

  protected toggleScope(scope: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.selectedScopes().filter((item) => item !== scope);
    // Se guarda en el orden de la lista, así el cuerpo de la solicitud no depende del orden de los clics.
    const next = checked ? [...current, scope] : current;
    this.selectedScopes.set(COMPANY_SCOPES.filter((item) => next.includes(item)));
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.triedSubmit.set(true);
    this.generalError.set(null);
    // `submit` marca los campos como tocados (así aparecen sus errores) y solo llama a `action` si son válidos.
    await submit(this.keyForm, {
      action: async () => {
        if (this.selectedScopes().length === 0) {
          return undefined;
        }
        try {
          const created = await this.store.create(apiKeyBody(this.model(), this.selectedScopes()));
          this.copied.set(false);
          this.secret.set(created);
          this.model.set(emptyApiKey());
          this.keyForm().reset();
          this.selectedScopes.set([]);
          this.triedSubmit.set(false);
        } catch (error) {
          this.generalError.set(
            error instanceof AppError ? error.message : this.text('form.unexpected'),
          );
        }
        return undefined;
      },
    });
  }

  /** Al cerrar el diálogo (botón, X o Escape) la llave completa sale de la memoria. */
  protected onSecretVisible(visible: boolean): void {
    if (!visible) {
      this.secret.set(null);
      this.copied.set(false);
    }
  }

  protected async copySecret(): Promise<void> {
    const key = this.secret()?.key;
    if (!key) {
      return;
    }
    try {
      await navigator.clipboard.writeText(key);
      this.copied.set(true);
    } catch {
      this.toasts.show({ kind: 'warning', text: this.text('secret.copyFailed') });
    }
  }

  protected askRevoke(row: SessionApiKey): void {
    this.toRevoke.set(row);
    this.revokeOpen.set(true);
  }

  protected async revoke(): Promise<void> {
    const row = this.toRevoke();
    if (!row) {
      return;
    }
    if (await this.store.revoke(row.id)) {
      this.toasts.show({ kind: 'success', text: this.text('revoke.done') });
    }
    this.revokeOpen.set(false);
    this.toRevoke.set(null);
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return apiKeysText(this.transloco, key, params);
  }
}
