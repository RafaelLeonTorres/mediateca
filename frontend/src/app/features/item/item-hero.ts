import { Component, computed, input, linkedSignal } from '@angular/core';
import { MatChip, MatChipSet } from '@angular/material/chips';
import { MatIcon } from '@angular/material/icon';

import { imageUrl, type ItemDetail, type ReleaseStatus } from '../../core/items/items-api';
import { describeAiring } from '../../shared/airing-text';
import { ITEM_KIND_ICONS, formatDuration } from '../../shared/item-display';

/** Cuántas etiquetas se muestran como máximo. */
const MAX_TAGS = 10;

const STATUS_LABELS: Record<ReleaseStatus, string> = {
  Airing: 'En emisión',
  Finished: 'Finalizada',
  Upcoming: 'Próximamente',
  Cancelled: 'Cancelada',
  Hiatus: 'En pausa',
};

/**
 * Cabecera de la ficha de una serie o película, como en Jellyfin: imagen de fondo con un degradado
 * oscuro, póster, títulos, datos, sinopsis, géneros, estudios, etiquetas y enlaces. Los botones
 * (Reproducir, Marcar como visto...) llegan por proyección de contenido.
 */
@Component({
  selector: 'app-item-hero',
  imports: [MatChip, MatChipSet, MatIcon],
  template: `
    <header class="hero" [class.has-backdrop]="showBackdrop()">
      @if (showBackdrop()) {
        <img
          class="backdrop"
          [src]="backdropSrc()"
          alt=""
          aria-hidden="true"
          (error)="backdropFailed.set(true)"
        />
      }
      <div class="scrim" aria-hidden="true"></div>

      <div class="content">
        <div class="poster">
          @if (item().hasImage && !posterFailed()) {
            <img
              [src]="posterSrc()"
              [alt]="'Póster de ' + item().title"
              (error)="posterFailed.set(true)"
            />
          } @else {
            <div class="placeholder">
              <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
            </div>
          }
        </div>

        <div class="info">
          <h1>{{ item().title }}</h1>
          @if (originalTitle(); as original) {
            <p class="original" lang="ja">{{ original }}</p>
          }

          <p class="meta">
            @if (yearsText(); as years) {
              <span class="years">{{ years }}</span>
            }
            @if (statusLabel(); as status) {
              <span class="status">{{ status }}</span>
            }
            @if (item().communityRating; as rating) {
              <span class="rating">
                <mat-icon aria-hidden="true">star</mat-icon>
                <span class="sr-only">Calificación </span>{{ rating.toFixed(1) }}
              </span>
            }
            @if (item().contentRating; as contentRating) {
              <span class="badge">{{ contentRating }}</span>
            }
            @if (durationText(); as duration) {
              <span class="duration">{{ duration }}</span>
            }
          </p>

          @if (airing(); as text) {
            <p class="airing">
              <mat-icon aria-hidden="true">schedule</mat-icon>
              {{ text }}
            </p>
          }

          <div class="actions">
            <ng-content />
          </div>

          @if (item().overview; as overview) {
            <p class="overview">{{ overview }}</p>
          }

          @if (item().genres.length > 0) {
            <mat-chip-set aria-label="Géneros">
              @for (genre of item().genres; track genre) {
                <mat-chip>{{ genre }}</mat-chip>
              }
            </mat-chip-set>
          }

          @if (item().studios.length > 0) {
            <p class="line">
              <span class="label">Estudios</span> {{ item().studios.join(', ') }}
            </p>
          }
          @if (item().directors.length > 0) {
            <p class="line">
              <span class="label">Dirección</span> {{ item().directors.join(', ') }}
            </p>
          }
          @if (item().actors.length > 0) {
            <p class="line">
              <span class="label">Reparto</span> {{ item().actors.join(', ') }}
            </p>
          }

          @if (tags().length > 0) {
            <ul class="tags" aria-label="Etiquetas">
              @for (tag of tags(); track tag) {
                <li>{{ tag }}</li>
              }
            </ul>
          }

          @if (item().links.length > 0) {
            <ul class="links" aria-label="Enlaces externos">
              @for (link of item().links; track link.url) {
                <li>
                  <a [href]="link.url" target="_blank" rel="noopener noreferrer">
                    {{ link.site }}
                    <mat-icon aria-hidden="true">open_in_new</mat-icon>
                  </a>
                </li>
              }
            </ul>
          }

          @if (anilistUrl(); as url) {
            <p class="attribution">
              <a [href]="url" target="_blank" rel="noopener noreferrer">Datos de AniList</a>
            </p>
          }
        </div>
      </div>
    </header>
  `,
  styles: `
    :host {
      display: block;
    }

    .hero {
      position: relative;
      isolation: isolate;
      overflow: hidden;
      border-radius: var(--mat-sys-corner-extra-large);
      background: var(--mat-sys-surface-container);
    }

    .backdrop {
      position: absolute;
      inset: 0;
      z-index: -2;
      width: 100%;
      height: 100%;
      object-fit: cover;
      object-position: center top;
    }

    /* Degradado oscuro: la imagen se ve arriba y el texto queda sobre un fondo que lo hace legible. */
    .scrim {
      position: absolute;
      inset: 0;
      z-index: -1;
    }

    .has-backdrop .scrim {
      background: linear-gradient(
        to bottom,
        color-mix(in srgb, var(--mat-sys-surface) 35%, transparent) 0%,
        color-mix(in srgb, var(--mat-sys-surface) 85%, transparent) 55%,
        var(--mat-sys-surface) 100%
      );
    }

    .content {
      display: grid;
      grid-template-columns: minmax(140px, 240px) 1fr;
      gap: 32px;
      align-items: start;
      padding: 32px;
    }

    .has-backdrop .content {
      padding-top: 96px;
    }

    .poster {
      aspect-ratio: 2 / 3;
      overflow: hidden;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container-high);
      box-shadow: var(--mat-sys-level2);
    }

    .poster img {
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
      width: 72px;
      height: 72px;
      font-size: 72px;
    }

    h1 {
      margin: 0;
      font: var(--mat-sys-display-small);
      overflow-wrap: anywhere;
    }

    .original {
      margin: 4px 0 0;
      font: var(--mat-sys-title-medium);
      color: var(--mat-sys-on-surface-variant);
      overflow-wrap: anywhere;
    }

    .meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px 16px;
      margin: 12px 0;
      font: var(--mat-sys-body-large);
      color: var(--mat-sys-on-surface-variant);
    }

    .rating {
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }

    .rating mat-icon {
      width: 20px;
      height: 20px;
      font-size: 20px;
      color: var(--mat-sys-tertiary);
    }

    .badge {
      padding: 0 8px;
      border: 1px solid var(--mat-sys-outline);
      border-radius: var(--mat-sys-corner-extra-small);
      font: var(--mat-sys-label-large);
    }

    .airing {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0 0 12px;
      font: var(--mat-sys-title-small);
      color: var(--mat-sys-primary);
    }

    .airing mat-icon {
      width: 20px;
      height: 20px;
      font-size: 20px;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;
      margin: 8px 0 16px;
    }

    .actions:empty {
      display: none;
    }

    .overview {
      margin: 0 0 16px;
      max-width: 72ch;
      font: var(--mat-sys-body-large);
      white-space: pre-line;
    }

    .line {
      margin: 8px 0 0;
      font: var(--mat-sys-body-medium);
      overflow-wrap: anywhere;
    }

    .label {
      margin-right: 8px;
      color: var(--mat-sys-on-surface-variant);
    }

    ul {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin: 12px 0 0;
      padding: 0;
      list-style: none;
    }

    .tags li {
      padding: 2px 10px;
      border-radius: var(--mat-sys-corner-full);
      background: var(--mat-sys-surface-container-highest);
      font: var(--mat-sys-label-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    a {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      color: var(--mat-sys-primary);
      border-radius: var(--mat-sys-corner-extra-small);
      outline: none;
    }

    a:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    a mat-icon {
      width: 16px;
      height: 16px;
      font-size: 16px;
    }

    .attribution {
      margin: 12px 0 0;
      font: var(--mat-sys-label-small);
    }

    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }

    @media (max-width: 720px) {
      .content {
        grid-template-columns: 1fr;
        gap: 16px;
        padding: 16px;
      }

      .has-backdrop .content {
        padding-top: 64px;
      }

      .poster {
        width: min(55%, 220px);
      }

      h1 {
        font: var(--mat-sys-headline-medium);
      }
    }

    @media (prefers-reduced-motion: no-preference) {
      .backdrop {
        animation: fade-in 300ms ease-out;
      }

      @keyframes fade-in {
        from {
          opacity: 0;
        }
      }
    }
  `,
})
export class ItemHero {
  readonly item = input.required<ItemDetail>();

  /** La imagen de fondo o el póster respondieron con error. Se reinicia al cambiar de elemento. */
  protected readonly backdropFailed = linkedSignal<boolean>(() => {
    void this.item().id;
    return false;
  });
  protected readonly posterFailed = linkedSignal<boolean>(() => {
    void this.item().id;
    return false;
  });

  protected readonly showBackdrop = computed(() => this.item().hasBackdrop && !this.backdropFailed());
  protected readonly backdropSrc = computed(() => imageUrl(this.item().id, 'backdrop'));
  protected readonly posterSrc = computed(() => imageUrl(this.item().id));
  protected readonly icon = computed(() => ITEM_KIND_ICONS[this.item().kind]);

  /** El título original, solo si añade algo al título que se muestra. */
  protected readonly originalTitle = computed(() => {
    const item = this.item();
    return item.originalTitle && item.originalTitle !== item.title ? item.originalTitle : null;
  });

  /** "2026 – Actualidad", "2019 – 2021", "2010"; null sin año. */
  protected readonly yearsText = computed(() => {
    const { year, endYear, status, kind } = this.item();
    if (!year) {
      return null;
    }
    if (kind === 'Series') {
      if (endYear && endYear !== year) {
        return `${year} – ${endYear}`;
      }
      if (!endYear && status === 'Airing') {
        return `${year} – Actualidad`;
      }
    }
    return String(year);
  });

  protected readonly statusLabel = computed(() => {
    const status = this.item().status;
    return status ? STATUS_LABELS[status] : null;
  });

  protected readonly durationText = computed(() => {
    const item = this.item();
    if (item.runtimeMinutes) {
      return `${item.runtimeMinutes} min`;
    }
    return formatDuration(item.durationSeconds) || null;
  });

  /** "Se emite los martes a las 23:00", en la zona horaria del navegador. */
  protected readonly airing = computed(() => {
    const item = this.item();
    return item.status === 'Airing' ? describeAiring(item.nextAiringAt) : null;
  });

  protected readonly tags = computed(() => this.item().tags.slice(0, MAX_TAGS));

  /** Enlace de AniList, para mostrar la atribución que pide el servicio. */
  protected readonly anilistUrl = computed(
    () => this.item().links.find((link) => link.site === 'AniList')?.url ?? null,
  );
}
