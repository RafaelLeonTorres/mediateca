import { Component, computed, input, output, signal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';

import { formatDuration } from '../../shared/item-display';

/** Segundos que salta la flecha del teclado en la barra de avance. */
const SKIP_SECONDS = 10;

/**
 * Barra de controles propia del reproductor (la nativa del `<video>` no sirve cuando el lienzo de WebGPU lo
 * tapa): reproducir y pausa, anterior y siguiente, volumen con silencio, tiempo, barra de avance que se arrastra
 * y pantalla completa. Los botones de subtítulos y Reescalar se proyectan en el hueco de la derecha. No sabe nada
 * del `<video>`: recibe el estado y avisa de lo que el usuario pide.
 */
@Component({
  selector: 'app-player-controls',
  imports: [MatIcon],
  styleUrls: ['./player-ui.scss'],
  template: `
    <div class="bar" [class.hidden]="!visible()">
      <input
        type="range"
        class="range"
        aria-label="Barra de avance"
        min="0"
        step="1"
        [max]="seekMax()"
        [value]="shownTime()"
        [disabled]="seekMax() === 0"
        [style.--progress]="progress() + '%'"
        [attr.aria-valuetext]="time(shownTime()) + ' de ' + time(duration())"
        (input)="onScrub($event)"
        (change)="onSeekCommit($event)"
        (keydown)="onSeekKey($event)"
      />

      <div class="row">
        <button
          type="button"
          class="ctl"
          [attr.aria-label]="playing() ? 'Pausar' : 'Reproducir'"
          [attr.title]="playing() ? 'Pausar (K)' : 'Reproducir (K)'"
          (click)="togglePlay.emit()"
        >
          <mat-icon aria-hidden="true">{{ playing() ? 'pause' : 'play_arrow' }}</mat-icon>
        </button>
        <button
          type="button"
          class="ctl"
          aria-label="Episodio anterior"
          [disabled]="!hasPrevious()"
          (click)="goPrevious.emit()"
        >
          <mat-icon aria-hidden="true">skip_previous</mat-icon>
        </button>
        <button
          type="button"
          class="ctl"
          aria-label="Episodio siguiente"
          [disabled]="!hasNext()"
          (click)="goNext.emit()"
        >
          <mat-icon aria-hidden="true">skip_next</mat-icon>
        </button>

        <div class="volume">
          <button
            type="button"
            class="ctl"
            [attr.aria-label]="muted() ? 'Quitar el silencio' : 'Silenciar'"
            [attr.title]="muted() ? 'Quitar el silencio (M)' : 'Silenciar (M)'"
            (click)="toggleMute.emit()"
          >
            <mat-icon aria-hidden="true">{{ volumeIcon() }}</mat-icon>
          </button>
          <input
            type="range"
            class="range small"
            aria-label="Volumen"
            min="0"
            max="1"
            step="0.05"
            [value]="muted() ? 0 : volume()"
            [style.--progress]="(muted() ? 0 : volume() * 100) + '%'"
            (input)="onVolume($event)"
          />
        </div>

        <span class="time">{{ time(shownTime()) }} / {{ time(duration()) }}</span>
        <span class="spacer"></span>

        <ng-content />

        <button
          type="button"
          class="ctl"
          [attr.aria-label]="fullscreen() ? 'Salir de pantalla completa' : 'Pantalla completa'"
          [attr.title]="fullscreen() ? 'Salir de pantalla completa (F)' : 'Pantalla completa (F)'"
          (click)="toggleFullscreen.emit()"
        >
          <mat-icon aria-hidden="true">{{ fullscreen() ? 'fullscreen_exit' : 'fullscreen' }}</mat-icon>
        </button>
      </div>
    </div>
  `,
  styles: `
    :host {
      position: absolute;
      right: 0;
      bottom: 0;
      left: 0;
      z-index: 2;
    }

    .bar {
      padding: 32px 12px 8px;
      background: linear-gradient(transparent, rgb(0 0 0 / 80%));
      transition:
        opacity 0.2s,
        visibility 0.2s;
    }

    .bar.hidden {
      opacity: 0;
      visibility: hidden;
      pointer-events: none;
    }

    .row {
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .spacer {
      flex: 1;
    }

    .time {
      margin-left: 8px;
      color: #fff;
      font: var(--mat-sys-label-large);
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    .volume {
      display: flex;
      align-items: center;
    }

    .range {
      width: 100%;
      height: 16px;
      margin: 0;
      background: transparent;
      cursor: pointer;
      appearance: none;
    }

    .range.small {
      width: 88px;
    }

    .range::-webkit-slider-runnable-track {
      height: 4px;
      border-radius: 2px;
      background: linear-gradient(
        to right,
        var(--mat-sys-primary) var(--progress, 0%),
        rgb(255 255 255 / 30%) var(--progress, 0%)
      );
    }

    .range::-moz-range-track {
      height: 4px;
      border-radius: 2px;
      background: linear-gradient(
        to right,
        var(--mat-sys-primary) var(--progress, 0%),
        rgb(255 255 255 / 30%) var(--progress, 0%)
      );
    }

    .range::-webkit-slider-thumb {
      width: 14px;
      height: 14px;
      margin-top: -5px;
      border: 0;
      border-radius: 50%;
      background: #fff;
      appearance: none;
    }

    .range::-moz-range-thumb {
      width: 14px;
      height: 14px;
      border: 0;
      border-radius: 50%;
      background: #fff;
    }

    .range:focus-visible {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 4px;
    }

    .range:disabled {
      cursor: default;
      opacity: 0.5;
    }

    @media (max-width: 600px) {
      .range.small,
      .time {
        display: none;
      }
    }
  `,
})
export class PlayerControls {
  readonly visible = input(true);
  readonly playing = input(false);
  readonly currentTime = input(0);
  readonly duration = input(0);
  /** Volumen de 0 a 1. */
  readonly volume = input(1);
  readonly muted = input(false);
  readonly fullscreen = input(false);
  readonly hasPrevious = input(false);
  readonly hasNext = input(false);

  readonly togglePlay = output<void>();
  /** Segundos a los que ir (al soltar la barra de avance). */
  readonly seek = output<number>();
  /** Segundos que saltar (flechas en la barra de avance): +10 o -10. */
  readonly skip = output<number>();
  readonly volumeChange = output<number>();
  readonly toggleMute = output<void>();
  readonly goPrevious = output<void>();
  readonly goNext = output<void>();
  readonly toggleFullscreen = output<void>();

  /** Segundo al que apunta la barra mientras se arrastra (el video no se mueve hasta soltarla). */
  private readonly scrub = signal<number | null>(null);

  protected readonly seekMax = computed(() => {
    const duration = this.duration();
    return Number.isFinite(duration) && duration > 0 ? duration : 0;
  });
  protected readonly shownTime = computed(() => this.scrub() ?? this.currentTime());
  protected readonly progress = computed(() => {
    const max = this.seekMax();
    return max > 0 ? Math.min(100, Math.max(0, (this.shownTime() / max) * 100)) : 0;
  });
  protected readonly volumeIcon = computed(() => {
    if (this.muted() || this.volume() === 0) {
      return 'volume_off';
    }
    return this.volume() < 0.5 ? 'volume_down' : 'volume_up';
  });

  protected time(seconds: number): string {
    return formatDuration(seconds) || '0:00';
  }

  protected onScrub(event: Event): void {
    this.scrub.set((event.target as HTMLInputElement).valueAsNumber);
  }

  protected onSeekCommit(event: Event): void {
    this.seek.emit((event.target as HTMLInputElement).valueAsNumber);
    this.scrub.set(null);
  }

  /** Las flechas de la barra de avance saltan 10 s, igual que en el resto del reproductor (no 1 s). */
  protected onSeekKey(event: KeyboardEvent): void {
    if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
      event.preventDefault();
      event.stopPropagation();
      this.skip.emit(event.key === 'ArrowRight' ? SKIP_SECONDS : -SKIP_SECONDS);
    }
  }

  protected onVolume(event: Event): void {
    this.volumeChange.emit((event.target as HTMLInputElement).valueAsNumber);
  }
}
