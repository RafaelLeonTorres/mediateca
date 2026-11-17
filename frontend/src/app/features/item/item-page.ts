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

/** Tipos de elemento que tienen hijos (temporadas, episodios, pistas). */
const KINDS_WITH_CHILDREN: readonly ItemKind[] = ['Series', 'Season', 'MusicAlbum'];

/** Detalle de un elemento: ficha, ruta de navegación y, según el tipo, temporadas, episodios o pistas. */
@Component({
  selector: 'app-item-page',
  imports: [MatChip, MatChipSet, MatIcon, MatProgressBar, RouterLink],
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

      <article class="detail">
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
        <section aria-labelledby="children-title">
          <h2 id="children-title">{{ childrenTitle() }}</h2>
          @if (children.isLoading()) {
            <mat-progress-bar mode="indeterminate" aria-label="Cargando" />
          }
          @if (childList().length === 0 && !children.isLoading()) {
            <p class="muted">No hay elementos para mostrar.</p>
          }
          <ol class="children">
            @for (child of childList(); track child.id) {
              <li>
                <a class="child" [routerLink]="['/items', child.id]">
                  <span class="number">{{ childNumber(child) }}</span>
                  <span class="child-title">{{ child.title }}</span>
                  <span class="duration">{{ duration(child.durationSeconds) }}</span>
                </a>
              </li>
            }
          </ol>
        </section>
      }
    }
  `,
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

    .child:hover {
      background: var(--mat-sys-surface-container);
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

  /** La imagen respondió con error: se muestra el marcador. Se reinicia al cambiar de elemento. */
  protected readonly imageFailed = linkedSignal<boolean>(() => {
    this.id();
    return false;
  });

  protected readonly childrenTitle = computed(() => {
    switch (this.item()?.kind) {
      case 'Series':
        return 'Temporadas';
      case 'Season':
        return 'Episodios';
      default:
        return 'Pistas';
    }
  });

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

  /** Número que se muestra a la izquierda: episodio, pista (con disco si hay varios) o temporada. */
  protected childNumber(child: ChildItem): string {
    switch (child.kind) {
      case 'Episode':
        return child.episodeNumber !== null ? `${child.episodeNumber}` : '';
      case 'Track': {
        const track = child.trackNumber ?? '';
        return child.discNumber && child.discNumber > 1
          ? `${child.discNumber}-${track}`
          : `${track}`;
      }
      case 'Season':
        return child.seasonNumber === 0 ? 'Esp.' : `T${child.seasonNumber ?? ''}`;
      default:
        return '';
    }
  }
}
