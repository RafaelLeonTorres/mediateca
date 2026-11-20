import { Component, inject, signal } from '@angular/core';
import { FormField, form, maxLength, pattern, required, submit } from '@angular/forms/signals';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';

import { AccountApi } from '../../core/auth/account-api';
import { SessionStore } from '../../core/auth/session-store';
import { describeError } from '../../shared/describe-error';
import { MIN_PASSWORD_LENGTH, newPasswordRules } from '../../shared/password-fields';

/** Mi cuenta: cualquier usuario cambia su nombre visible y su contraseña. */
@Component({
  selector: 'app-my-account',
  imports: [FormField, MatButton, MatError, MatFormField, MatHint, MatInput, MatLabel],
  template: `
    <h1>Mi cuenta</h1>

    <section class="card" aria-labelledby="profile-title">
      <h2 id="profile-title">Perfil</h2>
      <p class="meta">
        Usuario: <strong>{{ session.user()?.userName }}</strong> ·
        {{ session.user()?.isAdmin ? 'Administrador' : 'Usuario' }}
      </p>

      <form novalidate (submit)="$event.preventDefault(); saveName()">
        <mat-form-field appearance="outline">
          <mat-label>Nombre visible</mat-label>
          <input matInput [formField]="profileForm.displayName" autocomplete="name" />
          <mat-error>Escribe un nombre de hasta 100 caracteres.</mat-error>
        </mat-form-field>

        @if (nameError()) {
          <p class="error" role="alert">{{ nameError() }}</p>
        }
        @if (nameSaved()) {
          <p class="done" role="status">Se guardó tu nombre.</p>
        }

        <button matButton="filled" type="submit" [disabled]="savingName()">Guardar nombre</button>
      </form>
    </section>

    <section class="card" aria-labelledby="password-title">
      <h2 id="password-title">Contraseña</h2>
      <p class="meta">
        Al cambiarla, esta sesión sigue abierta y las demás (otros navegadores o dispositivos) se
        cierran.
      </p>

      <form novalidate (submit)="$event.preventDefault(); savePassword()">
        <mat-form-field appearance="outline">
          <mat-label>Contraseña actual</mat-label>
          <input
            matInput
            type="password"
            [formField]="passwordForm.current"
            autocomplete="current-password"
          />
          <mat-error>Escribe tu contraseña actual.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Contraseña nueva</mat-label>
          <input
            matInput
            type="password"
            [formField]="passwordForm.next"
            autocomplete="new-password"
          />
          <mat-hint>Al menos {{ minPassword }} caracteres.</mat-hint>
          <mat-error>La contraseña debe tener al menos {{ minPassword }} caracteres.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Repite la contraseña nueva</mat-label>
          <input
            matInput
            type="password"
            [formField]="passwordForm.confirm"
            autocomplete="new-password"
          />
          <mat-error>
            @if (passwordForm.confirm().getError('required')) {
              Repite la contraseña nueva.
            } @else {
              Las contraseñas no coinciden.
            }
          </mat-error>
        </mat-form-field>

        @if (passwordError()) {
          <p class="error" role="alert">{{ passwordError() }}</p>
        }
        @if (passwordSaved()) {
          <p class="done" role="status">
            Se cambió tu contraseña. Las demás sesiones abiertas se cerraron.
          </p>
        }

        <button matButton="filled" type="submit" [disabled]="savingPassword()">
          Cambiar contraseña
        </button>
      </form>
    </section>
  `,
  styles: `
    :host {
      display: block;
      max-width: 560px;
    }

    h1 {
      margin: 0 0 24px;
      font: var(--mat-sys-headline-medium);
    }

    .card {
      margin-bottom: 24px;
      padding: 24px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container);
    }

    h2 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-large);
    }

    .meta {
      margin: 0 0 20px;
      color: var(--mat-sys-on-surface-variant);
    }

    form {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 4px;
    }

    mat-form-field {
      width: 100%;
    }

    .error {
      margin: 0 0 12px;
      color: var(--mat-sys-error);
    }

    .done {
      margin: 0 0 12px;
      color: var(--mat-sys-primary);
    }

    @media (max-width: 480px) {
      .card {
        padding: 20px 16px;
      }
    }
  `,
})
export class MyAccount {
  protected readonly session = inject(SessionStore);
  private readonly api = inject(AccountApi);

  protected readonly minPassword = MIN_PASSWORD_LENGTH;

  // Signal Forms: cada modelo es una señal y cada formulario es un árbol de campos sobre ella.
  private readonly profileModel = signal({ displayName: this.session.user()?.displayName ?? '' });
  protected readonly profileForm = form(this.profileModel, (path) => {
    required(path.displayName);
    // Con al menos un carácter que no sea espacio.
    pattern(path.displayName, /\S/);
    maxLength(path.displayName, 100);
  });
  protected readonly savingName = signal(false);
  protected readonly nameError = signal<string | null>(null);
  protected readonly nameSaved = signal(false);

  private readonly passwordModel = signal({ current: '', next: '', confirm: '' });
  protected readonly passwordForm = form(this.passwordModel, (path) => {
    required(path.current);
    newPasswordRules(path.next, path.confirm);
  });
  protected readonly savingPassword = signal(false);
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordSaved = signal(false);

  protected async saveName(): Promise<void> {
    this.nameError.set(null);
    this.nameSaved.set(false);
    // submit() marca los campos como tocados (así se ven los errores) y solo corre si todo es válido.
    await submit(this.profileForm, async () => {
      this.savingName.set(true);
      try {
        const user = await this.api.updateName(this.profileModel().displayName.trim());
        // La barra superior muestra el nombre nuevo.
        this.session.updateUser(user);
        this.profileModel.set({ displayName: user.displayName });
        this.nameSaved.set(true);
      } catch (error) {
        this.nameError.set(describeError(error));
      } finally {
        this.savingName.set(false);
      }
    });
  }

  protected async savePassword(): Promise<void> {
    this.passwordError.set(null);
    this.passwordSaved.set(false);
    await submit(this.passwordForm, async () => {
      this.savingPassword.set(true);
      try {
        const { current, next } = this.passwordModel();
        await this.api.changePassword(current, next);
        // Vacía los campos y olvida que se tocaron: así los campos vacíos no se marcan en rojo.
        this.passwordForm().reset({ current: '', next: '', confirm: '' });
        this.passwordSaved.set(true);
      } catch (error) {
        this.passwordError.set(describeError(error));
      } finally {
        this.savingPassword.set(false);
      }
    });
  }
}
