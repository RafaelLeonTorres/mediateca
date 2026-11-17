import { Component, computed, input, signal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { imageUrl, type ResumeItem } from '../../core/items/items-api';
import { ITEM_KIND_ICONS, formatDuration, progressPercent } from '../item-display';

/**
 * Tarjeta 16:9 de "Continuar viendo" y "A continuación": la miniatura del episodio (o el póster de su
 * serie), una barra con lo que lleva visto, el título de la serie y la carpeta con el episodio.
 * Al abrirla se reproduce directo, retomando donde se quedó.
 */
@Component({
  selector: 'app-resume-card',
  imports: [MatIcon, MatProgressBar, RouterLink],
  template: `
    <a class="card" [routerLink]="['/reproducir', item().id]">
      <div class="thumb">
        @if (showImage()) {
          <img [src]="src()" alt="" loading="lazy" (error)="failed.set(true)" />
        } @else {
          <div class="placeholder">
            <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
          </div>
        }
        <span class="play" aria-hidden="true">
          <mat-icon>play_arrow</mat-icon>
        </span>
        @if (percent() !== null) {
          <mat-progress-bar
            class="progress"
            mode="determinate"
            [value]="percent()"
            [attr.aria-label]="'Avance: ' + percentText() + ' %'"
          />
        }
      </div>
      <span class="title">{{ heading() }}</span>
      @if (subtitle()) {
        <span class="subtitle">{{ subtitle() }}</span>
      }
    </a>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: 4px;
      color: inherit;
      text-decoration: none;
      border-radius: var(--mat-sys-corner-medium);
      outline: none;
    }

    .thumb {
      position: relative;
      aspect-ratio: 16 / 9;
      overflow: hidden;
      border-radius: var(--mat-sys-corner-medium);
      background: var(--mat-sys-surface-container-high);
    }

    .card:focus-visible .thumb {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .placeholder {
      display: grid;
      place-items: center;
      width: 100%;
      height: 100%;
      color: var(--mat-sys-on-surface-variant);
    }

    .placeholder mat-icon {
      width: 48px;
      height: 48px;
      font-size: 48px;
    }

    /* El ícono de reproducir aparece al pasar el cursor o al enfocar la tarjeta. */
    .play {
      position: absolute;
      inset: 0;
      display: grid;
      place-items: center;
      background: rgb(0 0 0 / 40%);
      color: #fff;
      opacity: 0;
      transition: opacity 120ms ease;
    }

    .card:hover .play,
    .card:focus-visible .play {
      opacity: 1;
    }

    .progress {
      position: absolute;
      right: 0;
      bottom: 0;
      left: 0;
    }

    .title {
      margin-top: 4px;
      font: var(--mat-sys-title-small);
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .subtitle {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    @media (prefers-reduced-motion: reduce) {
      .play {
        transition: none;
      }
    }
  `,
})
export class ResumeCard {
  readonly item = input.required<ResumeItem>();

  /** La imagen respondió con error: se queda el marcador. */
  protected readonly failed = signal(false);

  protected readonly showImage = computed(() => this.item().imageId !== null && !this.failed());
  protected readonly src = computed(() => imageUrl(this.item().imageId ?? this.item().id));
  protected readonly icon = computed(() => ITEM_KIND_ICONS[this.item().kind]);

  /** Lo que lleva visto, de 0 a 100; null si no se sabe la duración. */
  protected readonly percent = computed(() =>
    progressPercent(this.item().positionSeconds, this.item().durationSeconds),
  );
  protected readonly percentText = computed(() => Math.round(this.percent() ?? 0));

  /** Título principal: la serie de un episodio, o la película. */
  protected readonly heading = computed(() => this.item().seriesTitle ?? this.item().title);

  /** "Lat · Episodio 3", o lo que queda por ver ("12:34 de 23:45"). */
  protected readonly subtitle = computed(() => {
    const item = this.item();
    const parts: string[] = [];
    if (item.seriesTitle) {
      if (item.parentTitle) {
        parts.push(item.parentTitle);
      }
      parts.push(item.title);
    }
    const position = formatDuration(item.positionSeconds > 0 ? item.positionSeconds : null);
    const duration = formatDuration(item.durationSeconds);
    if (position && duration) {
      parts.push(`${position} de ${duration}`);
    }
    return parts.join(' · ');
  });
}
