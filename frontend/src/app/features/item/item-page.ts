import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatChipSet, MatChip } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import {
  ItemsApi,
  imageUrl,
  type ChildItem,
  type ItemDetail,
  type ItemKind,
} from '../../core/items/items-api';
import { SessionStore } from '../../core/auth/session-store';
import { MetadataApi, describeMetadataError } from '../../core/metadata/metadata-api';
import { PlaybackApi } from '../../core/playback/playback-api';
import { PlayerService, type QueueTrack } from '../../core/playback/player-service';
import {
  ITEM_KIND_ICONS,
  ITEM_KIND_LABELS,
  MIN_RESUME_SECONDS,
  formatDuration,
  formatSize,
  progressPercent,
} from '../../shared/item-display';
import { PosterCard } from '../../shared/poster-card/poster-card';
import { ResumeCard } from '../../shared/resume-card/resume-card';
import { IdentifyDialog } from './identify-dialog';
import { ItemHero } from './item-hero';

/** Tipos de elemento que tienen hijos (temporadas, episodios, pistas). */
const KINDS_WITH_CHILDREN: readonly ItemKind[] = ['Series', 'Season', 'MusicAlbum'];

/** Tipos con "A continuación": la serie y sus carpetas. */
const KINDS_WITH_NEXT_UP: readonly ItemKind[] = ['Series', 'Season'];

/** Tipos que se pueden marcar como vistos (lo que se reproduce en video, y lo que lo contiene). */
const KINDS_WITH_PLAYED: readonly ItemKind[] = ['Movie', 'Episode', 'Series', 'Season'];

/**
 * Detalle de un elemento: ficha, ruta de navegación y, según el tipo, temporadas, episodios o pistas.
 * Las temporadas y las carpetas de una serie ("Lat", "Sub") se ven como tarjetas, igual que en Jellyfin.
 * Reproducir (o continuar), marcar como visto, "A continuación" y, en un álbum, la cola de música.
 */
@Component({
  selector: 'app-item-page',
  imports: [
    MatButton,
    MatChip,
    MatChipSet,
    MatIcon,
    MatIconButton,
    MatProgressBar,
    NgTemplateOutlet,
    ItemHero,
    PosterCard,
    ResumeCard,
    RouterLink,
  ],
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

        <ng-template #actionsTpl>
          @if (playTarget(); as target) {
            <a matButton="filled" [routerLink]="['/reproducir', target.id]">
              <mat-icon aria-hidden="true">play_arrow</mat-icon>
              {{ playLabel() }}
            </a>
          }
          @if (current.kind === 'MusicAlbum') {
            <button
              matButton="filled"
              type="button"
              [disabled]="tracks().length === 0"
              (click)="playAlbum(0)"
            >
              <mat-icon aria-hidden="true">play_arrow</mat-icon>
              Reproducir álbum
            </button>
          }
          @if (current.kind === 'Track') {
            <button matButton="filled" type="button" (click)="playSingleTrack(current)">
              <mat-icon aria-hidden="true">play_arrow</mat-icon>
              Reproducir
            </button>
          }
          @if (canMarkPlayed()) {
            <button matButton="outlined" type="button" [disabled]="marking()" (click)="togglePlayed()">
              <mat-icon aria-hidden="true">{{ isPlayed() ? 'visibility_off' : 'check_circle' }}</mat-icon>
              {{ isPlayed() ? 'Marcar como no visto' : 'Marcar como visto' }}
            </button>
          }
          @if (isAdmin() && (current.kind === 'Series' || current.kind === 'Movie')) {
            <button matButton="outlined" type="button" [disabled]="busy()" (click)="identify(current)">
              <mat-icon aria-hidden="true">manage_search</mat-icon>
              Identificar
            </button>
            <button matButton="outlined" type="button" [disabled]="busy()" (click)="refreshMetadata(current)">
              <mat-icon aria-hidden="true">refresh</mat-icon>
              Actualizar metadatos
            </button>
          }
        </ng-template>

      @if (usesHero(current)) {
        <app-item-hero [item]="current">
          <ng-container [ngTemplateOutlet]="actionsTpl" />
        </app-item-hero>
        @if (actionError()) {
          <p class="error" role="alert">{{ actionError() }}</p>
        }
        @if (isAdmin() && metadataNote(current); as note) {
          <p class="note">{{ note }}</p>
        }
      } @else {
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

          <div class="actions">
            <ng-container [ngTemplateOutlet]="actionsTpl" />
          </div>
          @if (actionError()) {
            <p class="error" role="alert">{{ actionError() }}</p>
          }
          @if (isAdmin() && metadataNote(current); as note) {
            <p class="note">{{ note }}</p>
          }

          @if (current.overview) {
            <p class="overview">{{ current.overview }}</p>
          }
        </div>
      </article>
      }

      @if (hasChildren()) {
        @if (children.isLoading()) {
          <mat-progress-bar mode="indeterminate" aria-label="Cargando" />
        }

        @if (nextUpItem(); as next) {
          <section aria-labelledby="next-title">
            <h2 id="next-title">A continuación</h2>
            <div class="next">
              <app-resume-card [item]="next" />
            </div>
          </section>
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
                <li class="entry" [class.current]="isCurrentTrack(child)">
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
                        @if (child.played) {
                          <span class="badge" role="img" aria-label="Visto">
                            <mat-icon aria-hidden="true">check</mat-icon>
                          </span>
                        } @else if (percent(child) !== null) {
                          <mat-progress-bar
                            class="progress"
                            mode="determinate"
                            [value]="percent(child)"
                            [attr.aria-label]="'Avance de ' + child.title"
                          />
                        }
                      </span>
                    }
                    <span class="number">{{ childNumber(child) }}</span>
                    <span class="child-title">{{ child.title }}</span>
                    <span class="duration">{{ duration(child.durationSeconds) }}</span>
                  </a>
                  @if (child.kind === 'Episode') {
                    <a
                      matIconButton
                      class="row-play"
                      [routerLink]="['/reproducir', child.id]"
                      [attr.aria-label]="'Reproducir ' + child.title"
                    >
                      <mat-icon aria-hidden="true">play_arrow</mat-icon>
                    </a>
                  } @else if (child.kind === 'Track') {
                    <button
                      matIconButton
                      type="button"
                      class="row-play"
                      [attr.aria-label]="'Reproducir ' + child.title"
                      (click)="playAlbum(trackIndex(child))"
                    >
                      <mat-icon aria-hidden="true">{{ isCurrentTrack(child) ? 'equalizer' : 'play_arrow' }}</mat-icon>
                    </button>
                  }
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

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      margin: 16px 0 0;
    }

    .error {
      margin: 8px 0 0;
      color: var(--mat-sys-error);
    }

    .note {
      margin: 8px 0 0;
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    app-item-hero {
      margin-bottom: 24px;
    }

    .next {
      max-width: 360px;
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

    /* La fila es el enlace al detalle y, a su lado, el botón de reproducir. */
    .entry {
      display: grid;
      grid-template-columns: 1fr auto;
      align-items: center;
      gap: 4px;
    }

    .entry.current .child-title {
      color: var(--mat-sys-primary);
    }

    .child:hover {
      background: var(--mat-sys-surface-container);
    }

    .thumb {
      position: relative;
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

    /* Avance de un episodio a medias, abajo de su miniatura. */
    .progress {
      position: absolute;
      right: 0;
      bottom: 0;
      left: 0;
    }

    /* Marca de un episodio que ya se vio. */
    .badge {
      position: absolute;
      top: 4px;
      right: 4px;
      display: grid;
      place-items: center;
      width: 22px;
      height: 22px;
      border-radius: 50%;
      background: var(--mat-sys-primary);
      color: var(--mat-sys-on-primary);
    }

    .badge mat-icon {
      width: 16px;
      height: 16px;
      font-size: 16px;
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
  private readonly playback = inject(PlaybackApi);
  private readonly player = inject(PlayerService);
  private readonly metadata = inject(MetadataApi);
  private readonly dialog = inject(MatDialog);
  private readonly session = inject(SessionStore);

  protected readonly detail = this.api.detail(() => this.id());
  // En Angular 22, value() de un recurso con error lanza: se lee solo cuando hay valor.
  protected readonly item = computed(() => (this.detail.hasValue() ? this.detail.value() : undefined));

  protected readonly hasChildren = computed(() => {
    const kind = this.item()?.kind;
    return kind !== undefined && KINDS_WITH_CHILDREN.includes(kind);
  });
  protected readonly children = this.api.children(() =>
    this.hasChildren() && this.item()?.id === this.id() ? this.id() : undefined,
  );
  protected readonly childList = computed(() => (this.children.hasValue() ? (this.children.value() ?? []) : []));

  /** "A continuación" de una serie o carpeta: el episodio que sigue, o null si no queda nada. */
  protected readonly nextUp = this.api.nextUp(() => {
    const item = this.item();
    return item && item.id === this.id() && KINDS_WITH_NEXT_UP.includes(item.kind) ? item.id : undefined;
  });

  /** El episodio de "A continuación", o null si no hay (204) o si la solicitud falló. */
  protected readonly nextUpItem = computed(() => (this.nextUp.hasValue() ? this.nextUp.value() : null));

  /** Temporadas y carpetas (Lat, Sub), como tarjetas. */
  protected readonly seasons = computed(() => this.childList().filter((child) => child.kind === 'Season'));

  /** Episodios sueltos o pistas, como lista. */
  protected readonly entries = computed(() => this.childList().filter((child) => child.kind !== 'Season'));

  /** Pistas de un álbum, en orden. */
  protected readonly tracks = computed(() => this.childList().filter((child) => child.kind === 'Track'));

  /**
   * A dónde lleva "Reproducir": la película o el episodio mismo, o en una serie o carpeta el episodio de
   * "A continuación" (y si ya se vio todo, el primero de la lista, para volver a verlo).
   */
  protected readonly playTarget = computed<{ id: string; position: number } | null>(() => {
    const item = this.item();
    if (!item) {
      return null;
    }
    switch (item.kind) {
      case 'Movie':
      case 'Episode':
        return { id: item.id, position: item.positionSeconds };
      case 'Series':
      case 'Season': {
        if (this.nextUp.isLoading()) {
          return null;
        }
        const next = this.nextUpItem();
        if (next) {
          return { id: next.id, position: next.positionSeconds };
        }
        const first = this.entries().find((entry) => entry.kind === 'Episode');
        return first ? { id: first.id, position: 0 } : null;
      }
      default:
        return null;
    }
  });

  /** "Reproducir", o "Continuar desde 12:34" si ya había empezado. */
  protected readonly playLabel = computed(() => {
    const target = this.playTarget();
    return target && target.position >= MIN_RESUME_SECONDS
      ? `Continuar desde ${formatDuration(target.position)}`
      : 'Reproducir';
  });

  /** Se puede marcar como visto o no visto: lo que se ve en video y las series y carpetas con episodios. */
  protected readonly canMarkPlayed = computed(() => {
    const item = this.item();
    if (!item || !KINDS_WITH_PLAYED.includes(item.kind)) {
      return false;
    }
    if (item.kind === 'Series' || item.kind === 'Season') {
      return item.unplayedCount !== null && (item.unplayedCount > 0 || this.childList().length > 0);
    }
    return true;
  });

  /** Ya se vio: la película o el episodio, o una serie o carpeta sin episodios por ver. */
  protected readonly isPlayed = computed(() => {
    const item = this.item();
    if (!item) {
      return false;
    }
    return item.kind === 'Series' || item.kind === 'Season' ? item.unplayedCount === 0 : item.played;
  });

  /** El usuario es administrador: ve Identificar y Actualizar metadatos. */
  protected readonly isAdmin = computed(() => this.session.user()?.isAdmin === true);

  /** Se está identificando o actualizando: los botones esperan. */
  protected readonly busy = signal(false);

  /** Se está actualizando lo visto: los botones esperan. */
  protected readonly marking = signal(false);

  /** Mensaje cuando no se pudo actualizar lo visto. Se borra al cambiar de elemento. */
  protected readonly actionError = linkedSignal<string | null>(() => {
    this.id();
    return null;
  });

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

  /** Series y películas llevan la cabecera con imagen de fondo; lo demás, la ficha sencilla. */
  protected usesHero(item: ItemDetail): boolean {
    return item.kind === 'Series' || item.kind === 'Movie';
  }

  /** Nota para el administrador cuando los metadatos no se pudieron obtener. */
  protected metadataNote(item: ItemDetail): string | null {
    if (item.kind !== 'Series' && item.kind !== 'Movie') {
      return null;
    }
    switch (item.metadataStatus) {
      case 'NotFound':
        return item.metadataMessage ?? 'No se encontraron metadatos. Usa Identificar para elegirlos.';
      case 'Error':
        return item.metadataMessage ?? 'No se pudieron obtener los metadatos.';
      case 'Pending':
        return 'Los metadatos están en la cola.';
      default:
        return null;
    }
  }

  /** Abre el diálogo para elegir a mano qué serie o película es; si se aplicó algo, vuelve a pedir la ficha. */
  protected async identify(item: ItemDetail): Promise<void> {
    const applied = await new Promise<boolean | undefined>((resolve) => {
      this.dialog
        .open(IdentifyDialog, {
          data: { id: item.id, title: item.title, year: item.year },
          width: '640px',
          maxWidth: '95vw',
        })
        .afterClosed()
        .subscribe(resolve);
    });
    if (applied) {
      this.reloadAll();
    }
  }

  /** Vuelve a pedir los metadatos al proveedor, con el id que ya tiene la serie o película. */
  protected async refreshMetadata(item: ItemDetail): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.actionError.set(null);
    try {
      const outcome = await this.metadata.refresh(item.id);
      if (outcome.status === 'NotFound') {
        this.actionError.set(outcome.message ?? 'No se encontraron metadatos.');
      }
      this.reloadAll();
    } catch (error) {
      this.actionError.set(describeMetadataError(error));
    } finally {
      this.busy.set(false);
    }
  }

  private reloadAll(): void {
    this.detail.reload();
    this.children.reload();
    this.nextUp.reload();
  }

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

  /** "12 episodios", con cuántos faltan por ver cuando ya se empezó, o "Vista" si no falta ninguno. */
  protected episodeCount(season: ChildItem): string {
    const count = season.childCount ?? 0;
    const text = count === 1 ? '1 episodio' : `${count} episodios`;
    const unplayed = season.unplayedCount;
    if (unplayed === null || count === 0) {
      return text;
    }
    if (unplayed === 0) {
      return `${text} · Vista`;
    }
    return unplayed < count ? `${text} · ${unplayed} sin ver` : text;
  }

  /** Avance de un episodio a medias (0 a 100); null si no ha empezado, ya se vio o no se sabe su duración. */
  protected percent(child: ChildItem): number | null {
    return child.played ? null : progressPercent(child.positionSeconds, child.durationSeconds);
  }

  /** Marca como visto o no visto, y vuelve a pedir lo que depende de ello. */
  protected async togglePlayed(): Promise<void> {
    const item = this.item();
    if (!item || this.marking()) {
      return;
    }
    this.marking.set(true);
    this.actionError.set(null);
    try {
      if (this.isPlayed()) {
        await this.playback.markUnplayed(item.id);
      } else {
        await this.playback.markPlayed(item.id);
      }
      this.detail.reload();
      this.children.reload();
      this.nextUp.reload();
    } catch {
      this.actionError.set('No se pudo actualizar lo que ya viste. Inténtalo de nuevo.');
    } finally {
      this.marking.set(false);
    }
  }

  /** La pista está sonando o es la que sigue en la barra de música. */
  protected isCurrentTrack(child: ChildItem): boolean {
    return child.kind === 'Track' && this.player.current()?.id === child.id;
  }

  protected trackIndex(child: ChildItem): number {
    return this.tracks().findIndex((track) => track.id === child.id);
  }

  /** Reproduce el álbum en orden desde esa pista (la primera si no se indica). */
  protected playAlbum(startIndex = 0): void {
    const album = this.item();
    if (!album || startIndex < 0) {
      return;
    }
    this.player.play(
      this.tracks().map((track) => toQueueTrack(track, album)),
      startIndex,
    );
  }

  /** Reproduce solo esa pista, desde su página de detalle. */
  protected playSingleTrack(track: ItemDetail): void {
    this.player.play([
      {
        id: track.id,
        title: track.title,
        artist: track.artist,
        album: track.parentTitle,
        durationSeconds: track.durationSeconds,
        imageId: track.hasImage ? track.id : null,
      },
    ]);
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

/** La pista de un álbum como la entiende la cola de la barra de música. */
function toQueueTrack(track: ChildItem, album: ItemDetail): QueueTrack {
  return {
    id: track.id,
    title: track.title,
    artist: track.artist ?? album.artist,
    album: album.title,
    durationSeconds: track.durationSeconds,
    imageId: track.hasImage ? track.id : album.hasImage ? album.id : null,
  };
}
