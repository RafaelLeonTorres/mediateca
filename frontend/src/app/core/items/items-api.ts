import { HttpResourceRef, httpResource } from '@angular/common/http';
import { Injectable } from '@angular/core';

import type { LibraryType } from '../libraries/libraries-api';

/** Tipo de elemento: lo que la API llama Kind. */
export type ItemKind = 'Movie' | 'Series' | 'Season' | 'Episode' | 'MusicAlbum' | 'Track';

/** Elemento de una cuadrícula o de una fila del inicio. */
export interface ItemSummary {
  id: string;
  kind: ItemKind;
  title: string;
  year: number | null;
  artist: string | null;
  hasImage: boolean;
}

/** Página de elementos de una biblioteca. */
export interface ItemPage {
  items: ItemSummary[];
  total: number;
  page: number;
  pageSize: number;
}

/** Un paso de la ruta de navegación (serie o temporada de un episodio, álbum de una pista). */
export interface ItemReference {
  id: string;
  kind: ItemKind;
  title: string;
}

/** Detalle de un elemento de cualquier tipo: GET /api/items/{id}. */
export interface ItemDetail {
  id: string;
  kind: ItemKind;
  libraryId: string;
  libraryName: string;
  libraryType: LibraryType;
  parentId: string | null;
  parentTitle: string | null;
  ancestors: ItemReference[];
  title: string;
  year: number | null;
  overview: string | null;
  contentRating: string | null;
  communityRating: number | null;
  genres: string[];
  hasImage: boolean;
  dateAdded: string;
  seasonNumber: number | null;
  episodeNumber: number | null;
  absoluteNumber: number | null;
  artist: string | null;
  trackNumber: number | null;
  discNumber: number | null;
  durationSeconds: number | null;
  fileSize: number | null;
}

/**
 * Hijo de un elemento: temporada, episodio o pista. Las carpetas dentro de una serie ("Lat", "Sub")
 * llegan como temporadas sin número; `childCount` es cuántos episodios tiene cada temporada.
 */
export interface ChildItem {
  id: string;
  kind: ItemKind;
  title: string;
  year: number | null;
  overview: string | null;
  hasImage: boolean;
  seasonNumber: number | null;
  episodeNumber: number | null;
  absoluteNumber: number | null;
  artist: string | null;
  trackNumber: number | null;
  discNumber: number | null;
  durationSeconds: number | null;
  childCount: number | null;
}

/** Dirección de la imagen de un elemento. Responde 404 si no tiene. */
export function imageUrl(id: string): string {
  return `/api/items/${id}/image`;
}

/** Lecturas de elementos. Los métodos se llaman en un contexto de inyección. */
@Injectable({ providedIn: 'root' })
export class ItemsApi {
  /** Lo último que se agregó a las bibliotecas del usuario. */
  recentlyAdded(limit = 20): HttpResourceRef<ItemSummary[] | undefined> {
    return httpResource<ItemSummary[]>(() => ({
      url: '/api/me/recently-added',
      params: { limit },
    }));
  }

  /** Detalle de un elemento. Sin id no pide nada. */
  detail(id: () => string | undefined): HttpResourceRef<ItemDetail | undefined> {
    return httpResource<ItemDetail>(() => {
      const itemId = id();
      return itemId ? `/api/items/${itemId}` : undefined;
    });
  }

  /** Temporadas, episodios o pistas del elemento. */
  children(id: () => string | undefined): HttpResourceRef<ChildItem[] | undefined> {
    return httpResource<ChildItem[]>(() => {
      const itemId = id();
      return itemId ? `/api/items/${itemId}/children` : undefined;
    });
  }
}
