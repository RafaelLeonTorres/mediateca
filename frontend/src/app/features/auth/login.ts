import { Component, inject, signal } from '@angular/core';
import { FormField, form, required, submit } from '@angular/forms/signals';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { ActivatedRoute, Router } from '@angular/router';

import { SESSION_CLOSED_PARAM, SESSION_CLOSED_VALUE } from '../../core/auth/session-expired';
import { SessionStore } from '../../core/auth/session-store';
import { describeError } from '../../shared/describe-error';

@Component({
  selector: 'app-login',
  imports: [FormField, MatButton, MatCheckbox, MatError, MatFormField, MatInput, MatLabel],
  template: `
    <main class="page">
      <section class="panel" aria-labelledby="login-title">
        <p class="brand">Mediateca</p>
        <h1 id="login-title">Inicia sesión</h1>
        <p class="intro">Tu música, películas, series y anime.</p>

        @if (sessionClosed) {
          <p class="notice" role="status">
            Tu sesión se cerró. Vuelve a iniciar sesión para continuar.
          </p>
        }

        <form novalidate (submit)="$event.preventDefault(); logIn()">
          <mat-form-field appearance="outline">
            <mat-label>Usuario</mat-label>
            <input
              matInput
              [formField]="loginForm.userName"
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
              [formField]="loginForm.password"
              autocomplete="current-password"
            />
            <mat-error>Escribe tu contraseña.</mat-error>
          </mat-form-field>

          <mat-checkbox class="remember" [formField]="loginForm.rememberMe">
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

  /** Se llegó aquí porque la API cerró la sesión (otro rol, otra contraseña o la cuenta se borró). */
  protected readonly sessionClosed =
    this.route.snapshot.queryParamMap.get(SESSION_CLOSED_PARAM) === SESSION_CLOSED_VALUE;
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);

  // Signal Forms: el modelo es una señal y el formulario es un árbol de campos sobre ella.
  private readonly model = signal({ userName: '', password: '', rememberMe: true });
  protected readonly loginForm = form(this.model, (path) => {
    required(path.userName);
    required(path.password);
  });

  protected async logIn(): Promise<void> {
    // submit() marca los campos como tocados (así se ven los errores) y solo corre si todo es válido.
    await submit(this.loginForm, async () => {
      this.sending.set(true);
      this.error.set(null);
      const { userName, password, rememberMe } = this.model();
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
    });
  }
}
