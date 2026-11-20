import { Component, inject, signal } from '@angular/core';
import { FormField, form, submit } from '@angular/forms/signals';
import { MatButton } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogActions,
  MatDialogContent,
  MatDialogRef,
  MatDialogTitle,
} from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';

import { UsersApi, type AdminUser } from '../../core/admin/users-api';
import { describeError } from '../../shared/describe-error';
import { MIN_PASSWORD_LENGTH, newPasswordRules } from '../../shared/password-fields';

export interface UserPasswordDialogData {
  user: AdminUser;
}

/** El administrador pone una contraseña nueva a un usuario. Se cierra con true si se cambió. */
@Component({
  selector: 'app-user-password-dialog',
  imports: [
    FormField,
    MatButton,
    MatDialogActions,
    MatDialogContent,
    MatDialogTitle,
    MatError,
    MatFormField,
    MatHint,
    MatInput,
    MatLabel,
  ],
  template: `
    <h2 mat-dialog-title>Contraseña de {{ data.user.displayName }}</h2>

    <mat-dialog-content>
      <p class="help">
        Elige una contraseña nueva y díselo a la persona. Sus sesiones abiertas se cierran y tendrá
        que iniciar sesión con esta.
      </p>

      <form class="form" novalidate (submit)="$event.preventDefault(); save()">
        <mat-form-field appearance="outline">
          <mat-label>Contraseña nueva</mat-label>
          <input
            matInput
            type="password"
            [formField]="passwordForm.password"
            autocomplete="new-password"
          />
          <mat-hint>Al menos {{ minPassword }} caracteres.</mat-hint>
          <mat-error>La contraseña debe tener al menos {{ minPassword }} caracteres.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Repite la contraseña</mat-label>
          <input
            matInput
            type="password"
            [formField]="passwordForm.confirm"
            autocomplete="new-password"
          />
          <mat-error>
            @if (passwordForm.confirm().getError('required')) {
              Repite la contraseña.
            } @else {
              Las contraseñas no coinciden.
            }
          </mat-error>
        </mat-form-field>
      </form>

      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton="text" type="button" (click)="dialogRef.close(false)">Cancelar</button>
      <button matButton="filled" type="button" [disabled]="saving()" (click)="save()">
        Cambiar contraseña
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      min-width: min(400px, 80vw);
    }

    .help {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }

    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class UserPasswordDialog {
  protected readonly dialogRef = inject<MatDialogRef<UserPasswordDialog, boolean>>(MatDialogRef);
  protected readonly data = inject<UserPasswordDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(UsersApi);

  protected readonly minPassword = MIN_PASSWORD_LENGTH;
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);

  // Signal Forms: el modelo es una señal y el formulario es un árbol de campos sobre ella.
  private readonly model = signal({ password: '', confirm: '' });
  protected readonly passwordForm = form(this.model, (path) =>
    newPasswordRules(path.password, path.confirm),
  );

  protected async save(): Promise<void> {
    this.error.set(null);
    // submit() marca los campos como tocados (así se ven los errores) y solo corre si todo es válido.
    await submit(this.passwordForm, async () => {
      this.saving.set(true);
      try {
        await this.api.resetPassword(this.data.user.id, this.model().password);
        this.dialogRef.close(true);
      } catch (error) {
        this.error.set(describeError(error));
      } finally {
        this.saving.set(false);
      }
    });
  }
}
