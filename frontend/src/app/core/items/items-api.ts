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

/** Estado de emisión de una serie o película. */
export type ReleaseStatus = 'Finished' | 'Airing' | 'Upcoming' | 'Cancelled' | 'Hiatus';

/** Resultado de buscar metadatos de un elemento (solo se informa a administradores). */
export type MetadataStatus = 'Pending' | 'Matched' | 'NotFound' | 'Error';

/** Enlace a un sitio externo (AniList, MyAnimeList, IMDb). */
export interface ExternalLink {
  site: string;
  url: string;
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
  /** Dónde se quedó el usuario, en segundos (0 si no ha empezado o ya lo vio). */
  positionSeconds: number;
  /** El usuario ya lo vio. */
  played: boolean;
  /** Episodios que le faltan por ver en una serie o temporada; null en lo demás. */
  unplayedCount: number | null;
  /** Título original (japonés, romaji...) cuando difiere del título que se muestra. */
  originalTitle: string | null;
  /** Año en que terminó la emisión; null si sigue en emisión o es una película. */
  endYear: number | null;
  status: ReleaseStatus | null;
  studios: string[];
  tags: string[];
  directors: string[];
  actors: string[];
  runtimeMinutes: number | null;
  /** ¿Tiene imagen de fondo (propia o de un ancestro)? */
  hasBackdrop: boolean;
  links: ExternalLink[];
  /** Próximo episodio por emitirse (ISO 8601, UTC). */
  nextAiringAt: string | null;
  nextAiringEpisode: number | null;
  metadataUpdatedAt: string | null;
  /** Solo para administradores. */
  metadataStatus: MetadataStatus | null;
  metadataMessage: string | null;
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
  /** Dónde se quedó el usuario, en segundos (0 si no ha empezado o ya lo vio). */
  positionSeconds: number;
  /** El usuario ya lo vio. */
  played: boolean;
  /** Episodios que le faltan por ver en una temporada o carpeta; null en lo demás. */
  unplayedCount: number | null;
}

/**
 * Película o episodio de "Continuar viendo" y "A continuación", con el título de la serie y de su
 * carpeta y lo necesario para la barra de avance. `imageId` es el elemento cuya imagen se muestra
 * (el propio, o su temporada o serie si no tiene miniatura); null si ninguno tiene.
 */
export interface ResumeItem {
  id: string;
  kind: ItemKind;
  title: string;
  seriesTitle: string | null;
  parentTitle: string | null;
  imageId: string | null;
  durationSeconds: number | null;
  positionSeconds: number;
  lastPlayedAt: string | null;
}

/** Dirección de la imagen de un elemento. Responde 404 si no tiene. */
export function imageUrl(id: string, type: 'poster' | 'backdrop' = 'poster'): string {
  return type === 'backdrop' ? `/api/items/${id}/image?type=backdrop` : `/api/items/${id}/image`;
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

  /** Películas y episodios a medias del usuario, del más reciente al más antiguo. */
  continueWatching(limit = 20): HttpResourceRef<ResumeItem[] | undefined> {
    return httpResource<ResumeItem[]>(() => ({
      url: '/api/me/continue-watching',
      params: { limit },
    }));
  }

  /**
   * "A continuación" de una serie o carpeta: el episodio que sigue dentro de la misma carpeta. La API
   * responde 204 si no queda nada por ver, y el valor queda en null. Sin id no pide nada.
   */
  nextUp(id: () => string | undefined): HttpResourceRef<ResumeItem | null | undefined> {
    return httpResource<ResumeItem | null>(() => {
      const itemId = id();
      return itemId ? `/api/items/${itemId}/next-up` : undefined;
    });
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
