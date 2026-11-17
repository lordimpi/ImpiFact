import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { InactivityMonitor, SessionStore } from '@impifact/core';
import { ButtonComponent, DialogComponent } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { provideAuthI18n } from '../auth-i18n';

/**
 * Diálogo de inactividad (sección 6 de la arquitectura): lo abre `InactivityMonitor` a los 25 minutos sin actividad.
 * «Seguir conectado», Escape o el botón de cerrar cuentan como actividad; «Cerrar sesión» la cierra al instante. Si
 * la persona no hace nada, el monitor cierra la sesión a los 30 minutos y el login explica el motivo.
 *
 * El shell lo monta con `loadInactivityWarning()` la primera vez que hay aviso.
 */
@Component({
  selector: 'feat-inactivity-warning',
  imports: [ButtonComponent, DialogComponent, TranslocoPipe],
  providers: [provideAuthI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog
      [header]="'auth.inactivity.title' | transloco"
      [closeLabel]="'auth.inactivity.close' | transloco"
      [visible]="monitor.warning()"
      (visibleChange)="onVisibleChange($event)"
    >
      <p>{{ 'auth.inactivity.body' | transloco: { minutes: monitor.minutesLeft() } }}</p>
      <div uiDialogFooter class="inactivity-actions">
        <ui-button
          variant="secondary"
          [label]="'auth.inactivity.logout' | transloco"
          (pressed)="logout()"
        />
        <ui-button [label]="'auth.inactivity.stay' | transloco" (pressed)="stay()" />
      </div>
    </ui-dialog>
  `,
  styles: `
    .inactivity-actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class InactivityWarningComponent {
  protected readonly monitor = inject(InactivityMonitor);
  private readonly session = inject(SessionStore);
  private loggingOut = false;

  protected stay(): void {
    this.monitor.stayConnected();
  }

  protected logout(): void {
    this.loggingOut = true;
    void this.session.logout();
  }

  /** El diálogo se cerró con Escape o con su botón: cuenta como «Seguir conectado». */
  protected onVisibleChange(visible: boolean): void {
    if (!visible && this.monitor.warning() && !this.loggingOut) {
      this.monitor.stayConnected();
    }
  }
}
