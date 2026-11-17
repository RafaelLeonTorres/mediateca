import { HttpResourceRef, HttpResourceRequest, httpResource } from '@angular/common/http';
import { Injectable } from '@angular/core';

import type { ItemPage } from '../items/items-api';

/** Tipos de biblioteca, tal como los entrega la API (enumeración serializada como texto). */
export type LibraryType = 'Movies' | 'Series' | 'Anime' | 'Music';

/** Nombre en español de cada tipo de biblioteca. */
export const LIBRARY_TYPE_LABELS: Record<LibraryType, string> = {
  Movies: 'Películas',
  Series: 'Series',
  Anime: 'Anime',
  Music: 'Música',
};

/** Biblioteca visible para el usuario: GET /api/libraries. */
export interface Library {
  id: string;
  name: string;
  type: LibraryType;
  itemCount: number;
}

/** Géneros y años que existen en una biblioteca: GET /api/libraries/{id}/filters. */
export interface LibraryFilters {
  genres: string[];
  years: number[];
}

/** Orden de la cuadrícula: por título, por año (más nuevo primero) o por fecha de alta. */
export type ItemSort = 'title' | 'year' | 'added';

export const ITEM_SORT_LABELS: Record<ItemSort, string> = {
  title: 'Título',
  year: 'Año',
  added: 'Agregado recientemente',
};

/** Elementos por página de la cuadrícula. */
export const ITEMS_PAGE_SIZE = 48;

/** Búsqueda, filtros, orden y página de la cuadrícula de una biblioteca. */
export interface ItemsQuery {
  search: string;
  genre: string;
  year: number | null;
  sort: ItemSort;
  /** Empieza en 1. */
  page: number;
}

export const EMPTY_ITEMS_QUERY: ItemsQuery = {
  search: '',
  genre: '',
  year: null,
  sort: 'title',
  page: 1,
};

/**
 * Arma la petición GET /api/libraries/{id}/items. Solo manda los parámetros que tienen valor,
 * para que la URL quede corta y la API use sus valores por omisión.
 */
export function buildItemsRequest(libraryId: string, query: ItemsQuery): HttpResourceRequest {
  const params: Record<string, string | number> = {
    sort: query.sort,
    page: query.page,
    pageSize: ITEMS_PAGE_SIZE,
  };
  const search = query.search.trim();
  if (search) {
    params['search'] = search;
  }
  if (query.genre) {
    params['genre'] = query.genre;
  }
  if (query.year !== null) {
    params['year'] = query.year;
  }
  return { url: `/api/libraries/${libraryId}/items`, params };
}

/**
 * Lecturas de bibliotecas para cualquier usuario. Los métodos que devuelven un recurso se llaman
 * en un contexto de inyección (el constructor o un inicializador de campo de un componente).
 */
@Injectable({ providedIn: 'root' })
export class LibrariesApi {
  /** Bibliotecas que el usuario puede ver. */
  libraries(): HttpResourceRef<Library[] | undefined> {
    return httpResource<Library[]>(() => '/api/libraries');
  }

  /** Géneros y años de una biblioteca. Sin id no pide nada. */
  filters(id: () => string | undefined): HttpResourceRef<LibraryFilters | undefined> {
    return httpResource<LibraryFilters>(() => {
      const libraryId = id();
      return libraryId ? `/api/libraries/${libraryId}/filters` : undefined;
    });
  }

  /** Una página de elementos; se vuelve a pedir cuando cambia la biblioteca o la consulta. */
  items(
    id: () => string | undefined,
    query: () => ItemsQuery,
  ): HttpResourceRef<ItemPage | undefined> {
    return httpResource<ItemPage>(() => {
      const libraryId = id();
      return libraryId ? buildItemsRequest(libraryId, query()) : undefined;
    });
  }
}
