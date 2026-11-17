import { Component, DOCUMENT, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { RouterOutlet } from '@angular/router';

import { SessionStore } from './core/auth/session-store';

@Component({
  selector: 'app-root',
  imports: [MatButton, RouterOutlet],
  template: `
    @if (session.apiError()) {
      <main class="offline" role="alert">
        <h1>Mediateca</h1>
        <p>
          No hay conexión con la API. Revisa que los contenedores estén corriendo
          (<code>docker compose ps</code>) y vuelve a intentarlo.
        </p>
        <button matButton="filled" type="button" (click)="retry()">Reintentar</button>
      </main>
    } @else {
      <router-outlet />
    }
  `,
  styles: `
    .offline {
      max-width: 32rem;
      margin: 0 auto;
      padding: 64px 24px;
    }

    h1 {
      margin: 0 0 16px;
      font: var(--mat-sys-headline-small);
      color: var(--mat-sys-primary);
    }

    p {
      margin: 0 0 24px;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class App {
  protected readonly session = inject(SessionStore);
  private readonly document = inject(DOCUMENT);

  protected retry(): void {
    this.document.location.reload();
  }
}
