import { Component, computed, input, signal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

import { imageUrl, type ItemKind } from '../../core/items/items-api';
import { ITEM_KIND_ICONS } from '../item-display';

/**
 * Tarjeta con póster 2:3, título y una línea de detalle. Si el elemento no tiene imagen o la
 * imagen no carga (404), muestra un marcador con el ícono del tipo.
 */
@Component({
  selector: 'app-poster-card',
  imports: [MatIcon, RouterLink],
  template: `
    <a class="card" [routerLink]="['/items', id()]">
      <div class="poster">
        @if (showImage()) {
          <img
            [src]="src()"
            [alt]="'Póster de ' + title()"
            loading="lazy"
            (error)="failed.set(true)"
          />
        } @else {
          <div class="placeholder">
            <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
          </div>
        }
      </div>
      <span class="title">{{ title() }}</span>
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

    .poster {
      aspect-ratio: 2 / 3;
      overflow: hidden;
      border-radius: var(--mat-sys-corner-medium);
      background: var(--mat-sys-surface-container-high);
      transition: transform 120ms ease;
    }

    .card:hover .poster {
      transform: translateY(-2px);
    }

    .card:focus-visible .poster {
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

    .title {
      margin-top: 4px;
      font: var(--mat-sys-title-small);
      overflow: hidden;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
    }

    .subtitle {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    @media (prefers-reduced-motion: reduce) {
      .poster {
        transition: none;
      }
    }
  `,
})
export class PosterCard {
  readonly id = input.required<string>();
  readonly title = input.required<string>();
  readonly kind = input<ItemKind>('Movie');
  readonly subtitle = input<string>('');
  readonly hasImage = input(false);

  /** La imagen respondió con error (por ejemplo 404): se queda el marcador. */
  protected readonly failed = signal(false);

  protected readonly showImage = computed(() => this.hasImage() && !this.failed());
  protected readonly src = computed(() => imageUrl(this.id()));
  protected readonly icon = computed(() => ITEM_KIND_ICONS[this.kind()]);
}
