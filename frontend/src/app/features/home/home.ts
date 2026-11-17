import { Component, computed, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { ItemsApi } from '../../core/items/items-api';
import {
  LIBRARY_TYPE_LABELS,
  LibrariesApi,
  type LibraryType,
} from '../../core/libraries/libraries-api';
import { PosterCard } from '../../shared/poster-card/poster-card';

/** Íconos de Material Symbols por tipo de biblioteca. */
const LIBRARY_ICONS: Record<LibraryType, string> = {
  Movies: 'movie',
  Series: 'tv',
  Anime: 'animation',
  Music: 'library_music',
};

/** Inicio: lo agregado recientemente y las bibliotecas del usuario. */
@Component({
  selector: 'app-home',
  imports: [MatButton, MatIcon, MatProgressBar, PosterCard, RouterLink],
  template: `
    <h1>Hola, {{ session.user()?.displayName }}</h1>

    @if (libraries.isLoading() || recent.isLoading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Cargando" />
    }

    @if (libraries.error() || recent.error()) {
      <p class="error" role="alert">
        No se pudo cargar el contenido. Revisa la conexión con la API e inténtalo de nuevo.
      </p>
    } @else if (loaded() && libraryList().length === 0) {
      <section class="empty" aria-labelledby="empty-title">
        <h2 id="empty-title">Todavía no hay bibliotecas</h2>
        @if (session.user()?.isAdmin) {
          <p>Agrega tus carpetas de música, películas, series y anime para empezar.</p>
          <a matButton="filled" routerLink="/admin/bibliotecas">Agregar una biblioteca</a>
        } @else {
          <p>Cuando el administrador te dé acceso a una biblioteca, va a aparecer aquí.</p>
        }
      </section>
    } @else {
      @if (recentList().length > 0) {
        <section aria-labelledby="recent-title">
          <h2 id="recent-title">Agregado recientemente</h2>
          <ul class="row">
            @for (item of recentList(); track item.id) {
              <li>
                <app-poster-card
                  [id]="item.id"
                  [title]="item.title"
                  [kind]="item.kind"
                  [subtitle]="item.artist ?? item.year?.toString() ?? ''"
                  [hasImage]="item.hasImage"
                />
              </li>
            }
          </ul>
        </section>
      }

      @if (libraryList().length > 0) {
        <section aria-labelledby="libraries-title">
          <h2 id="libraries-title">Mis bibliotecas</h2>
          <ul class="libraries">
            @for (library of libraryList(); track library.id) {
              <li>
                <a class="library" [routerLink]="['/bibliotecas', library.id]">
                  <mat-icon aria-hidden="true">{{ icon(library.type) }}</mat-icon>
                  <span class="name">{{ library.name }}</span>
                  <span class="meta">
                    {{ typeLabel(library.type) }} · {{ library.itemCount }}
                    {{ library.itemCount === 1 ? 'elemento' : 'elementos' }}
                  </span>
                </a>
              </li>
            }
          </ul>
        </section>
      }
    }
  `,
  styles: `
    h1 {
      margin: 0 0 24px;
      font: var(--mat-sys-headline-medium);
    }

    h2 {
      margin: 32px 0 16px;
      font: var(--mat-sys-title-large);
    }

    .error {
      color: var(--mat-sys-error);
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
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .row {
      display: grid;
      grid-auto-flow: column;
      grid-auto-columns: 140px;
      gap: 16px;
      overflow-x: auto;
      padding-bottom: 12px;
    }

    .libraries {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
      gap: 16px;
    }

    .library {
      display: flex;
      flex-direction: column;
      gap: 4px;
      height: 100%;
      box-sizing: border-box;
      padding: 20px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container);
      color: inherit;
      text-decoration: none;
      outline: none;
    }

    .library:hover {
      background: var(--mat-sys-surface-container-high);
    }

    .library:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    .library mat-icon {
      color: var(--mat-sys-primary);
      margin-bottom: 8px;
    }

    .name {
      font: var(--mat-sys-title-medium);
    }

    .meta {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class Home {
  protected readonly session = inject(SessionStore);
  protected readonly libraries = inject(LibrariesApi).libraries();
  protected readonly recent = inject(ItemsApi).recentlyAdded();

  protected readonly libraryList = computed(() => this.libraries.value() ?? []);
  protected readonly recentList = computed(() => this.recent.value() ?? []);
  /** Las bibliotecas ya llegaron (para no mostrar el estado vacío mientras se cargan). */
  protected readonly loaded = computed(() => this.libraries.status() === 'resolved');

  protected icon(type: LibraryType): string {
    return LIBRARY_ICONS[type];
  }

  protected typeLabel(type: LibraryType): string {
    return LIBRARY_TYPE_LABELS[type];
  }
}
