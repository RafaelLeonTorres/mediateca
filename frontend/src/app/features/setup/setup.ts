import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormGroupDirective,
  NgForm,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { describeError } from '../../shared/describe-error';

/** Las dos contraseñas tienen que coincidir. */
function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  return group.get('password')?.value === group.get('confirm')?.value
    ? null
    : { passwordsMismatch: true };
}

/** Marca en rojo la confirmación también cuando no coincide con la contraseña. */
class ConfirmErrorMatcher implements ErrorStateMatcher {
  isErrorState(control: AbstractControl | null, form: FormGroupDirective | NgForm | null): boolean {
    return !!control?.touched && (control.invalid || !!form?.hasError('passwordsMismatch'));
  }
}

/** Asistente de primer arranque: crea la cuenta de administrador. */
@Component({
  selector: 'app-setup',
  imports: [ReactiveFormsModule, MatButton, MatError, MatFormField, MatHint, MatInput, MatLabel],
  template: `
    <main class="page">
      <section class="panel" aria-labelledby="setup-title">
        <p class="brand">Mediateca</p>
        <h1 id="setup-title">Crea la cuenta de administrador</h1>
        <p class="intro">
          Es la primera vez que se abre Mediateca. Con esta cuenta vas a agregar las bibliotecas y
          a crear a los demás usuarios.
        </p>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field appearance="outline">
            <mat-label>Tu nombre</mat-label>
            <input matInput formControlName="displayName" autocomplete="name" />
            <mat-error>
              @if (form.controls.displayName.hasError('maxlength')) {
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
              formControlName="userName"
              autocomplete="username"
              autocapitalize="none"
            />
            <mat-hint>Con él vas a iniciar sesión.</mat-hint>
            <mat-error>Escribe un nombre de usuario.</mat-error>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Contraseña</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="new-password" />
            <mat-hint>Al menos 8 caracteres.</mat-hint>
            <mat-error>La contraseña debe tener al menos 8 caracteres.</mat-error>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Repite la contraseña</mat-label>
            <input
              matInput
              type="password"
              formControlName="confirm"
              autocomplete="new-password"
              [errorStateMatcher]="confirmMatcher"
            />
            <mat-error>
              @if (form.controls.confirm.hasError('required')) {
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

  protected readonly confirmMatcher = new ConfirmErrorMatcher();
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      displayName: ['', [Validators.required, Validators.maxLength(100)]],
      userName: ['', Validators.required],
      password: ['', [Validators.required, Validators.minLength(8)]],
      confirm: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    const { displayName, userName, password } = this.form.getRawValue();
    try {
      await this.session.setup(displayName.trim(), userName.trim(), password);
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.saving.set(false);
    }
  }
}
