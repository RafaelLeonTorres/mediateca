import { Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatToolbar } from '@angular/material/toolbar';
import { Router } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';

/** Inicio. En la fase 2 muestra las bibliotecas; por ahora, el saludo y el estado vacío. */
@Component({
  selector: 'app-home',
  imports: [MatButton, MatToolbar],
  template: `
    <mat-toolbar class="bar">
      <span class="brand">Mediateca</span>
      <span class="spacer"></span>
      <span class="user">{{ session.user()?.displayName }}</span>
      <button matButton="text" type="button" (click)="logout()" [disabled]="loggingOut()">
        Cerrar sesión
      </button>
    </mat-toolbar>

    <main class="content">
      <h1>Hola, {{ session.user()?.displayName }}</h1>

      <section class="empty" aria-labelledby="empty-title">
        <h2 id="empty-title">Todavía no hay bibliotecas</h2>
        @if (session.user()?.isAdmin) {
          <p>
            Pronto vas a poder agregar tus carpetas de música, películas, series y anime desde la
            administración.
          </p>
        } @else {
          <p>Cuando el administrador te dé acceso a una biblioteca, va a aparecer aquí.</p>
        }
      </section>
    </main>
  `,
  styles: `
    .bar {
      position: sticky;
      top: 0;
      z-index: 1;
      background: var(--mat-sys-surface-container);
    }

    .brand {
      font: var(--mat-sys-title-large);
      color: var(--mat-sys-primary);
    }

    .spacer {
      flex: 1;
    }

    .user {
      margin-right: 8px;
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .content {
      max-width: 960px;
      margin: 0 auto;
      padding: 32px 24px;
    }

    h1 {
      margin: 0 0 24px;
      font: var(--mat-sys-headline-medium);
    }

    .empty {
      padding: 32px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container-low);
    }

    .empty h2 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-medium);
    }

    .empty p {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }

    @media (max-width: 480px) {
      .user {
        display: none;
      }
    }
  `,
})
export class Home {
  protected readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  protected readonly loggingOut = signal(false);

  protected async logout(): Promise<void> {
    this.loggingOut.set(true);
    try {
      await this.session.logout();
      await this.router.navigateByUrl('/login');
    } finally {
      this.loggingOut.set(false);
    }
  }
}
