import { Component, inject, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatToolbar } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { PlayerService } from '../../core/playback/player-service';
import { MusicBar } from '../music-bar/music-bar';

/** Estructura común de las pantallas con sesión: barra superior con navegación y menú de usuario. */
@Component({
  selector: 'app-layout',
  imports: [
    MatButton,
    MatIcon,
    MatIconButton,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatToolbar,
    MusicBar,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
  ],
  template: `
    <mat-toolbar class="bar">
      <a class="brand" routerLink="/">Mediateca</a>
      <nav class="nav" aria-label="Principal">
        <a
          matButton="text"
          routerLink="/"
          routerLinkActive="active"
          [routerLinkActiveOptions]="{ exact: true }"
          ariaCurrentWhenActive="page"
        >
          Inicio
        </a>
        @if (session.user()?.isAdmin) {
          <a
            matButton="text"
            routerLink="/admin/bibliotecas"
            routerLinkActive="active"
            ariaCurrentWhenActive="page"
          >
            Administrar
          </a>
        }
      </nav>
      <span class="spacer"></span>
      <button
        matIconButton
        type="button"
        [matMenuTriggerFor]="userMenu"
        [attr.aria-label]="'Menú de ' + (session.user()?.displayName ?? 'usuario')"
      >
        <mat-icon aria-hidden="true">account_circle</mat-icon>
      </button>
      <mat-menu #userMenu="matMenu">
        <p class="menu-user">{{ session.user()?.displayName }}</p>
        <button mat-menu-item type="button" (click)="logout()" [disabled]="loggingOut()">
          <mat-icon aria-hidden="true">logout</mat-icon>
          <span>Cerrar sesión</span>
        </button>
      </mat-menu>
    </mat-toolbar>

    <main class="content" [class.with-bar]="player.current() !== null">
      <router-outlet />
    </main>

    <app-music-bar />
  `,
  styles: `
    .bar {
      position: sticky;
      top: 0;
      z-index: 10;
      background: var(--mat-sys-surface-container);
    }

    .brand {
      font: var(--mat-sys-title-large);
      color: var(--mat-sys-primary);
      text-decoration: none;
      border-radius: var(--mat-sys-corner-small);
      outline: none;
    }

    .brand:focus-visible {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 4px;
    }

    .nav {
      display: flex;
      gap: 4px;
      margin-left: 24px;
    }

    .nav .active {
      color: var(--mat-sys-primary);
    }

    .spacer {
      flex: 1;
    }

    .menu-user {
      margin: 0;
      padding: 8px 16px;
      font: var(--mat-sys-title-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .content {
      max-width: 1280px;
      margin: 0 auto;
      padding: 24px;
    }

    /* Deja espacio para que la barra de música fija no tape el final de la página. */
    .content.with-bar {
      padding-bottom: 120px;
    }

    @media (max-width: 480px) {
      .nav {
        margin-left: 8px;
      }

      .content {
        padding: 16px 12px;
      }
    }
  `,
})
export class AppLayout {
  protected readonly session = inject(SessionStore);
  protected readonly player = inject(PlayerService);
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
