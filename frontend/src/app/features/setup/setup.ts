import { Component, inject, signal } from '@angular/core';
import { FormField, form, maxLength, required, submit } from '@angular/forms/signals';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { describeError } from '../../shared/describe-error';
import { MIN_PASSWORD_LENGTH, newPasswordRules } from '../../shared/password-fields';

/** Asistente de primer arranque: crea la cuenta de administrador. */
@Component({
  selector: 'app-setup',
  imports: [FormField, MatButton, MatError, MatFormField, MatHint, MatInput, MatLabel],
  template: `
    <main class="page">
      <section class="panel" aria-labelledby="setup-title">
        <p class="brand">Mediateca</p>
        <h1 id="setup-title">Crea la cuenta de administrador</h1>
        <p class="intro">
          Es la primera vez que se abre Mediateca. Con esta cuenta vas a agregar las bibliotecas y
          a crear a los demás usuarios.
        </p>

        <form novalidate (submit)="$event.preventDefault(); create()">
          <mat-form-field appearance="outline">
            <mat-label>Tu nombre</mat-label>
            <input matInput [formField]="setupForm.displayName" autocomplete="name" />
            <mat-error>
              @if (setupForm.displayName().getError('maxLength')) {
                El nombre puede tener hasta 100 caracteres.
              } @else {
                Escribe tu nombre.
              }
            </mat-error>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Usuario</mat-label>
            <input
              matInput
              [formField]="setupForm.userName"
              autocomplete="username"
              autocapitalize="none"
            />
            <mat-hint>Con él vas a iniciar sesión.</mat-hint>
            <mat-error>Escribe un nombre de usuario.</mat-error>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Contraseña</mat-label>
            <input
              matInput
              type="password"
              [formField]="setupForm.password"
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
              [formField]="setupForm.confirm"
              autocomplete="new-password"
            />
            <mat-error>
              @if (setupForm.confirm().getError('required')) {
                Repite la contraseña.
              } @else {
                Las contraseñas no coinciden.
              }
            </mat-error>
          </mat-form-field>

          @if (error()) {
            <p class="error" role="alert">{{ error() }}</p>
          }

          <button matButton="filled" type="submit" [disabled]="saving()">Crear administrador</button>
        </form>
      </section>
    </main>
  `,
  styleUrl: '../../shared/auth-page.scss',
})
export class SetupPage {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  protected readonly minPassword = MIN_PASSWORD_LENGTH;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  // Signal Forms: el modelo es una señal y el formulario es un árbol de campos sobre ella.
  private readonly model = signal({ displayName: '', userName: '', password: '', confirm: '' });
  protected readonly setupForm = form(this.model, (path) => {
    required(path.displayName);
    maxLength(path.displayName, 100);
    required(path.userName);
    newPasswordRules(path.password, path.confirm);
  });

  protected async create(): Promise<void> {
    // submit() marca los campos como tocados (así se ven los errores) y solo corre si todo es válido.
    await submit(this.setupForm, async () => {
      this.saving.set(true);
      this.error.set(null);
      const { displayName, userName, password } = this.model();
      try {
        await this.session.setup(displayName.trim(), userName.trim(), password);
        await this.router.navigateByUrl('/');
      } catch (error) {
        this.error.set(describeError(error));
      } finally {
        this.saving.set(false);
      }
    });
  }
}
