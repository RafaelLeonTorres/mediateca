import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { MatChipSet, MatChip } from '@angular/material/chips';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { ItemsApi, imageUrl, type ChildItem, type ItemKind } from '../../core/items/items-api';
import {
  ITEM_KIND_ICONS,
  ITEM_KIND_LABELS,
  formatDuration,
  formatSize,
} from '../../shared/item-display';
import { PosterCard } from '../../shared/poster-card/poster-card';

/** Tipos de elemento que tienen hijos (temporadas, episodios, pistas). */
const KINDS_WITH_CHILDREN: readonly ItemKind[] = ['Series', 'Season', 'MusicAlbum'];

/**
 * Detalle de un elemento: ficha, ruta de navegación y, según el tipo, temporadas, episodios o pistas.
 * Las temporadas y las carpetas de una serie ("Lat", "Sub") se ven como tarjetas, igual que en Jellyfin.
 */
@Component({
  selector: 'app-item-page',
  imports: [MatChip, MatChipSet, MatIcon, MatProgressBar, PosterCard, RouterLink],
  template: `
    @if (detail.isLoading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Cargando" />
    }

    @if (detail.error()) {
      <section class="empty" aria-labelledby="missing-title">
        <h1 id="missing-title">No se encontró el elemento</h1>
        <p>Puede que se haya eliminado o que no tengas acceso.</p>
        <a routerLink="/">Volver al inicio</a>
      </section>
    } @else if (item(); as current) {
      <nav class="crumbs" aria-label="Ruta de navegación">
        <ol>
          <li>
            <a [routerLink]="['/bibliotecas', current.libraryId]">{{ current.libraryName }}</a>
          </li>
          @for (ancestor of current.ancestors; track ancestor.id) {
            <li>
              <a [routerLink]="['/items', ancestor.id]">{{ ancestor.title }}</a>
            </li>
          }
          <li aria-current="page">{{ current.title }}</li>
        </ol>
      </nav>

      <article class="detail" [class.wide]="current.kind === 'Episode'">
        <div class="poster">
          @if (current.hasImage && !imageFailed()) {
            <img
              [src]="image(current.id)"
              [alt]="'Póster de ' + current.title"
              (error)="imageFailed.set(true)"
            />
          } @else {
            <div class="placeholder">
              <mat-icon aria-hidden="true">{{ icon(current.kind) }}</mat-icon>
            </div>
          }
        </div>

        <div class="info">
          <p class="kind">{{ kindLabel(current.kind) }}</p>
          <h1>{{ current.title }}</h1>
          <p class="meta">{{ meta() }}</p>

          @if (current.genres.length > 0) {
            <mat-chip-set aria-label="Géneros">
              @for (genre of current.genres; track genre) {
                <mat-chip>{{ genre }}</mat-chip>
              }
            </mat-chip-set>
          }

          @if (current.overview) {
            <p class="overview">{{ current.overview }}</p>
          }
        </div>
      </article>

      @if (hasChildren()) {
        @if (children.isLoading()) {
          <mat-progress-bar mode="indeterminate" aria-label="Cargando" />
        }

        @if (seasons().length > 0) {
          <section aria-labelledby="seasons-title">
            <h2 id="seasons-title">Temporadas</h2>
            <ul class="grid">
              @for (season of seasons(); track season.id) {
                <li>
                  <app-poster-card
                    [id]="season.id"
                    [title]="season.title"
                    [kind]="season.kind"
                    [subtitle]="episodeCount(season)"
                    [hasImage]="season.hasImage"
                  />
                </li>
              }
            </ul>
          </section>
        }

        @if (entries().length > 0) {
          <section aria-labelledby="children-title">
            <h2 id="children-title">{{ entriesTitle() }}</h2>
            <ol class="children">
              @for (child of entries(); track child.id) {
                <li>
                  <a
                    class="child"
                    [class.episode]="child.kind === 'Episode'"
                    [routerLink]="['/items', child.id]"
                  >
                    @if (child.kind === 'Episode') {
                      <span class="thumb">
                        @if (child.hasImage && !failedThumbs().has(child.id)) {
                          <img
                            [src]="image(child.id)"
                            alt=""
                            loading="lazy"
                            (error)="thumbFailed(child.id)"
                          />
                        } @else {
                          <mat-icon aria-hidden="true">{{ icon(child.kind) }}</mat-icon>
                        }
                      </span>
                    }
                    <span class="number">{{ childNumber(child) }}</span>
                    <span class="child-title">{{ child.title }}</span>
                    <span class="duration">{{ duration(child.durationSeconds) }}</span>
                  </a>
                </li>
              }
            </ol>
          </section>
        } @else if (seasons().length === 0 && !children.isLoading()) {
          <section aria-labelledby="children-title">
            <h2 id="children-title">{{ entriesTitle() }}</h2>
            <p class="muted">No hay elementos para mostrar.</p>
          </section>
        }
      }
    }
  `,
  styleUrls: ['../../shared/poster-grid.scss'],
  styles: `
    .crumbs ol {
      display: flex;
      flex-wrap: wrap;
      gap: 4px 8px;
      margin: 0 0 24px;
      padding: 0;
      list-style: none;
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .crumbs li:not(:last-child)::after {
      content: '›';
      margin-left: 8px;
    }

    .crumbs a,
    .empty a {
      color: var(--mat-sys-primary);
      border-radius: var(--mat-sys-corner-extra-small);
      outline: none;
    }

    .crumbs a:focus-visible,
    .empty a:focus-visible,
    .child:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    .detail {
      display: grid;
      grid-template-columns: minmax(140px, 260px) 1fr;
      gap: 32px;
      align-items: start;
    }

    .poster {
      aspect-ratio: 2 / 3;
      overflow: hidden;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container-high);
    }

    .poster img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    /* La imagen de un episodio es una miniatura 16:9, no un póster. */
    .detail.wide {
      grid-template-columns: minmax(200px, 400px) 1fr;
    }

    .detail.wide .poster {
      aspect-ratio: 16 / 9;
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

    .kind {
      margin: 0;
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-primary);
    }

    h1 {
      margin: 4px 0 8px;
      font: var(--mat-sys-headline-large);
    }

    .meta,
    .muted {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }

    .overview {
      max-width: 70ch;
      margin: 16px 0 0;
      font: var(--mat-sys-body-large);
    }

    h2 {
      margin: 40px 0 12px;
      font: var(--mat-sys-title-large);
    }

    .children {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .child {
      display: grid;
      grid-template-columns: 4rem 1fr auto;
      gap: 12px;
      align-items: center;
      padding: 12px 16px;
      border-radius: var(--mat-sys-corner-medium);
      color: inherit;
      text-decoration: none;
      outline: none;
    }

    .child.episode {
      grid-template-columns: 128px 3rem 1fr auto;
      padding: 8px 16px 8px 8px;
    }

    .child:hover {
      background: var(--mat-sys-surface-container);
    }

    .thumb {
      display: grid;
      place-items: center;
      aspect-ratio: 16 / 9;
      overflow: hidden;
      border-radius: var(--mat-sys-corner-small);
      background: var(--mat-sys-surface-container-high);
      color: var(--mat-sys-on-surface-variant);
    }

    .thumb img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .number,
    .duration {
      color: var(--mat-sys-on-surface-variant);
      font-variant-numeric: tabular-nums;
    }

    .empty {
      padding: 32px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container-low);
    }

    .empty h1 {
      font: var(--mat-sys-title-medium);
    }

    @media (max-width: 640px) {
      .detail {
        grid-template-columns: 1fr;
        gap: 16px;
      }

      .poster {
        max-width: 200px;
      }

      .detail.wide {
        grid-template-columns: 1fr;
      }

      .detail.wide .poster {
        max-width: 100%;
      }

      .child.episode {
        grid-template-columns: 96px 2rem 1fr auto;
        gap: 8px;
      }
    }
  `,
})
export class ItemPage {
  /** Id del elemento, desde la ruta /items/:id. */
  readonly id = input.required<string>();

  private readonly api = inject(ItemsApi);

  protected readonly detail = this.api.detail(() => this.id());
  protected readonly item = computed(() => this.detail.value());

  protected readonly hasChildren = computed(() => {
    const kind = this.item()?.kind;
    return kind !== undefined && KINDS_WITH_CHILDREN.includes(kind);
  });
  protected readonly children = this.api.children(() =>
    this.hasChildren() && this.item()?.id === this.id() ? this.id() : undefined,
  );
  protected readonly childList = computed(() => this.children.value() ?? []);

  /** Temporadas y carpetas (Lat, Sub), como tarjetas. */
  protected readonly seasons = computed(() => this.childList().filter((child) => child.kind === 'Season'));

  /** Episodios sueltos o pistas, como lista. */
  protected readonly entries = computed(() => this.childList().filter((child) => child.kind !== 'Season'));

  /** Miniaturas de episodio que respondieron con error. Se reinicia al cambiar de elemento. */
  protected readonly failedThumbs = linkedSignal<ReadonlySet<string>>(() => {
    this.id();
    return new Set<string>();
  });

  /** La imagen respondió con error: se muestra el marcador. Se reinicia al cambiar de elemento. */
  protected readonly imageFailed = linkedSignal<boolean>(() => {
    this.id();
    return false;
  });

  protected readonly entriesTitle = computed(() =>
    this.item()?.kind === 'MusicAlbum' ? 'Pistas' : 'Episodios',
  );

  /** Línea de datos: año, clasificación, calificación, artista, duración y tamaño. */
  protected readonly meta = computed(() => {
    const item = this.item();
    if (!item) {
      return '';
    }
    const parts: string[] = [];
    if (item.artist) {
      parts.push(item.artist);
    }
    if (item.year) {
      parts.push(String(item.year));
    }
    if (item.contentRating) {
      parts.push(item.contentRating);
    }
    if (item.communityRating) {
      parts.push(`${item.communityRating.toFixed(1)} / 10`);
    }
    const duration = formatDuration(item.durationSeconds);
    if (duration) {
      parts.push(duration);
    }
    const size = formatSize(item.fileSize);
    if (size) {
      parts.push(size);
    }
    return parts.join(' · ');
  });

  protected image(id: string): string {
    return imageUrl(id);
  }

  protected icon(kind: ItemKind): string {
    return ITEM_KIND_ICONS[kind];
  }

  protected kindLabel(kind: ItemKind): string {
    return ITEM_KIND_LABELS[kind];
  }

  protected duration(seconds: number | null): string {
    return formatDuration(seconds);
  }

  protected episodeCount(season: ChildItem): string {
    const count = season.childCount ?? 0;
    return count === 1 ? '1 episodio' : `${count} episodios`;
  }

  protected thumbFailed(id: string): void {
    this.failedThumbs.update((failed) => new Set(failed).add(id));
  }

  /** Número que se muestra a la izquierda: episodio (o su número absoluto) o pista (con disco si hay varios). */
  protected childNumber(child: ChildItem): string {
    switch (child.kind) {
      case 'Episode': {
        const number = child.episodeNumber ?? child.absoluteNumber;
        return number !== null ? `${number}` : '';
      }
      case 'Track': {
        const track = child.trackNumber ?? '';
        return child.discNumber && child.discNumber > 1
          ? `${child.discNumber}-${track}`
          : `${track}`;
      }
      default:
        return '';
    }
  }
}
