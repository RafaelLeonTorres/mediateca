import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { ActivatedRoute, Router } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { describeError } from '../../shared/describe-error';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, MatButton, MatCheckbox, MatError, MatFormField, MatInput, MatLabel],
  template: `
    <main class="page">
      <section class="panel" aria-labelledby="login-title">
        <p class="brand">Mediateca</p>
        <h1 id="login-title">Inicia sesión</h1>
        <p class="intro">Tu música, películas, series y anime.</p>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field appearance="outline">
            <mat-label>Usuario</mat-label>
            <input
              matInput
              formControlName="userName"
              autocomplete="username"
              autocapitalize="none"
            />
            <mat-error>Escribe tu usuario.</mat-error>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Contraseña</mat-label>
            <input
              matInput
              type="password"
              formControlName="password"
              autocomplete="current-password"
            />
            <mat-error>Escribe tu contraseña.</mat-error>
          </mat-form-field>

          <mat-checkbox class="remember" formControlName="rememberMe">
            Mantener la sesión iniciada
          </mat-checkbox>

          @if (error()) {
            <p class="error" role="alert">{{ error() }}</p>
          }

          <button matButton="filled" type="submit" [disabled]="sending()">Iniciar sesión</button>
        </form>
      </section>
    </main>
  `,
  styleUrl: '../../shared/auth-page.scss',
})
export class LoginPage {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
    rememberMe: [true],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.sending.set(true);
    this.error.set(null);
    const { userName, password, rememberMe } = this.form.getRawValue();
    try {
      await this.session.login(userName.trim(), password, rememberMe);
      // Regresa a la pantalla que se pidió antes de iniciar sesión (solo rutas de esta app).
      const volver = this.route.snapshot.queryParamMap.get('volver');
      const destino = volver?.startsWith('/') && !volver.startsWith('//') ? volver : '/';
      await this.router.navigateByUrl(destino);
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.sending.set(false);
    }
  }
}
