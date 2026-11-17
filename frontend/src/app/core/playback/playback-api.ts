import { HttpClient, HttpResourceRef, httpResource } from '@angular/common/http';
import { DOCUMENT, Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { ItemKind } from '../items/items-api';

/** Lo que necesita el reproductor de video: GET /api/items/{id}/playback. */
export interface PlaybackInfo {
  id: string;
  kind: ItemKind;
  title: string;
  /** Serie de un episodio. */
  seriesTitle: string | null;
  /** Temporada o carpeta ("Lat", "Sub") de un episodio, o álbum de una pista. */
  parentTitle: string | null;
  artist: string | null;
  parentId: string | null;
  streamUrl: string;
  contentType: string;
  isVideo: boolean;
  durationSeconds: number | null;
  /** Dónde se quedó el usuario. */
  positionSeconds: number;
  played: boolean;
  /** Vecinos con número dentro de la misma carpeta; null en los extremos. */
  previousId: string | null;
  nextId: string | null;
  nextTitle: string | null;
}

/** Dirección del archivo de un elemento (acepta Range). */
export function streamUrl(id: string): string {
  return `/api/items/${id}/stream`;
}

/** Nombre de la cookie con el token XSRF. Angular la lee solo en sus POST, PUT y DELETE. */
const XSRF_COOKIE = 'XSRF-TOKEN';
const XSRF_HEADER = 'X-XSRF-TOKEN';

/** Valor de una cookie (ya decodificado), o null si no existe. */
export function readCookie(document: Document, name: string): string | null {
  for (const part of document.cookie.split(';')) {
    const [key, ...rest] = part.trim().split('=');
    if (key === name) {
      const value = rest.join('=');
      try {
        return decodeURIComponent(value);
      } catch {
        return value;
      }
    }
  }
  return null;
}

/**
 * Reproducción: lo que lee el reproductor (`httpResource`) y lo que escribe el progreso y el visto
 * (`HttpClient`, con la cabecera XSRF que pone Angular). Los métodos de lectura se llaman en un
 * contexto de inyección.
 */
@Injectable({ providedIn: 'root' })
export class PlaybackApi {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  /** Datos de reproducción de un elemento. Sin id no pide nada. */
  info(id: () => string | undefined): HttpResourceRef<PlaybackInfo | undefined> {
    return httpResource<PlaybackInfo>(() => {
      const itemId = id();
      return itemId ? `/api/items/${itemId}/playback` : undefined;
    });
  }

  /** Guarda la posición. Pasado el 90 % de la duración, la API lo marca como visto. */
  async saveProgress(id: string, positionSeconds: number): Promise<void> {
    await firstValueFrom(this.http.put<void>(`/api/items/${id}/progress`, { positionSeconds }));
  }

  /**
   * Guarda la posición al salir de la página. Un PUT normal se cancela cuando la página se cierra, y
   * `sendBeacon` no puede mandar cabeceras; `fetch` con `keepalive` sí termina de enviarse, con la
   * cabecera XSRF leída de la cookie. No espera respuesta ni avisa de errores: ya no hay a quién.
   */
  saveProgressOnExit(id: string, positionSeconds: number): void {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    const token = readCookie(this.document, XSRF_COOKIE);
    if (token) {
      headers[XSRF_HEADER] = token;
    }
    fetch(`/api/items/${id}/progress`, {
      method: 'PUT',
      headers,
      body: JSON.stringify({ positionSeconds }),
      keepalive: true,
      credentials: 'same-origin',
    }).catch(() => undefined);
  }

  /** Marca como visto un elemento; en una serie, temporada o álbum, todo lo que contiene. */
  async markPlayed(id: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`/api/items/${id}/played`, null));
  }

  /** Marca como no visto un elemento (y borra su posición); en una serie, temporada o álbum, todo lo que contiene. */
  async markUnplayed(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`/api/items/${id}/played`));
  }
}
