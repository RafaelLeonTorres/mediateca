import { Component, inject, linkedSignal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { MatIconButton } from '@angular/material/button';

import { imageUrl } from '../../core/items/items-api';
import { PlayerService, RESTART_AFTER_SECONDS } from '../../core/playback/player-service';
import { formatDuration } from '../item-display';

/**
 * Barra de reproducción fija de la música: carátula, título y artista, anterior, reproducir o pausa,
 * siguiente y la barra de avance. Solo aparece cuando hay una cola; se cierra con la equis.
 */
@Component({
  selector: 'app-music-bar',
  imports: [MatIcon, MatIconButton],
  template: `
    @if (player.current(); as track) {
      <section class="bar" aria-label="Reproductor de música">
        <div class="cover">
          @if (track.imageId && !coverFailed()) {
            <img [src]="cover(track.imageId)" alt="" (error)="coverFailed.set(true)" />
          } @else {
            <mat-icon aria-hidden="true">music_note</mat-icon>
          }
        </div>

        <div class="info">
          <span class="title">{{ track.title }}</span>
          <span class="artist">{{ artistLine(track.artist, track.album) }}</span>
          @if (player.error(); as message) {
            <span class="error" role="alert">{{ message }}</span>
          }
        </div>

        <div class="controls">
          <button
            matIconButton
            type="button"
            aria-label="Pista anterior"
            [disabled]="!player.hasPrevious() && player.position() <= restartAfter"
            (click)="player.previous()"
          >
            <mat-icon aria-hidden="true">skip_previous</mat-icon>
          </button>
          <button
            matIconButton
            type="button"
            class="toggle"
            [attr.aria-label]="player.playing() ? 'Pausar' : 'Reproducir'"
            (click)="player.toggle()"
          >
            <mat-icon aria-hidden="true">{{ player.playing() ? 'pause' : 'play_arrow' }}</mat-icon>
          </button>
          <button
            matIconButton
            type="button"
            aria-label="Pista siguiente"
            [disabled]="!player.hasNext()"
            (click)="player.next()"
          >
            <mat-icon aria-hidden="true">skip_next</mat-icon>
          </button>
        </div>

        <div class="seek">
          <span class="time">{{ time(player.position()) }}</span>
          <input
            type="range"
            aria-label="Avance de la pista"
            min="0"
            step="1"
            [max]="max()"
            [value]="player.position()"
            [attr.aria-valuetext]="time(player.position()) + ' de ' + time(player.duration())"
            (input)="seek($event)"
          />
          <span class="time">{{ time(player.duration()) }}</span>
        </div>

        <button matIconButton type="button" aria-label="Cerrar el reproductor" (click)="player.stop()">
          <mat-icon aria-hidden="true">close</mat-icon>
        </button>
      </section>
    }
  `,
  styles: `
    .bar {
      position: fixed;
      right: 0;
      bottom: 0;
      left: 0;
      z-index: 20;
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto minmax(120px, 2fr) auto;
      grid-template-areas: 'info controls seek close';
      align-items: center;
      gap: 8px 16px;
      box-sizing: border-box;
      padding: 8px 16px calc(8px + env(safe-area-inset-bottom));
      background: var(--mat-sys-surface-container-high);
      box-shadow: var(--mat-sys-level2);
    }

    .cover {
      display: none;
    }

    .info {
      grid-area: info;
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .controls {
      grid-area: controls;
      display: flex;
      align-items: center;
    }

    .seek {
      grid-area: seek;
      display: flex;
      align-items: center;
      gap: 8px;
      min-width: 0;
    }

    button[aria-label='Cerrar el reproductor'] {
      grid-area: close;
    }

    .title {
      font: var(--mat-sys-title-small);
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .artist {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .error {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-error);
    }

    .time {
      font: var(--mat-sys-body-small);
      font-variant-numeric: tabular-nums;
      color: var(--mat-sys-on-surface-variant);
    }

    input[type='range'] {
      flex: 1;
      min-width: 0;
      accent-color: var(--mat-sys-primary);
    }

    input[type='range']:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    @media (min-width: 720px) {
      .bar {
        grid-template-columns: 56px minmax(0, 1fr) auto minmax(160px, 2fr) auto;
        grid-template-areas: 'cover info controls seek close';
      }

      .cover {
        grid-area: cover;
        display: grid;
        place-items: center;
        width: 56px;
        height: 56px;
        overflow: hidden;
        border-radius: var(--mat-sys-corner-small);
        background: var(--mat-sys-surface-container-highest);
        color: var(--mat-sys-on-surface-variant);
      }

      .cover img {
        display: block;
        width: 100%;
        height: 100%;
        object-fit: cover;
      }
    }

    @media (max-width: 719px) {
      .bar {
        grid-template-columns: minmax(0, 1fr) auto auto;
        grid-template-areas:
          'info controls close'
          'seek seek seek';
      }
    }
  `,
})
export class MusicBar {
  protected readonly player = inject(PlayerService);

  /** Desde estos segundos, "anterior" reinicia la pista aunque no haya una anterior. */
  protected readonly restartAfter = RESTART_AFTER_SECONDS;

  /** La carátula respondió con error: se muestra el ícono. Se reinicia al cambiar de pista. */
  protected readonly coverFailed = linkedSignal<boolean>(() => {
    this.player.current();
    return false;
  });

  /** Fin de la barra de avance: la duración, o lo que lleva si todavía no se conoce. */
  protected max(): number {
    return Math.max(this.player.duration(), this.player.position(), 1);
  }

  protected cover(id: string): string {
    return imageUrl(id);
  }

  protected time(seconds: number): string {
    return formatDuration(seconds) || '0:00';
  }

  protected artistLine(artist: string | null, album: string | null): string {
    return [artist, album].filter((part) => !!part).join(' · ');
  }

  protected seek(event: Event): void {
    this.player.seek(Number((event.target as HTMLInputElement).value));
  }
}
