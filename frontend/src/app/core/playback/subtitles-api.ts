import { HttpResourceRef, httpResource } from '@angular/common/http';
import { Injectable } from '@angular/core';

/** De dónde sale la pista: dentro del video (MP4, MKV...) o un archivo suelto junto a él. */
export type SubtitleSource = 'embedded' | 'external';

/** Una pista de subtítulos de texto: GET /api/items/{id}/subtitles. */
export interface SubtitleTrack {
  /** Estable mientras el archivo no cambie: `e2` (stream 2 del contenedor) o `x0` (primer archivo suelto). */
  id: string;
  /** Etiqueta en español: "Español", "Inglés (forzados)", "Pista 3". */
  label: string;
  /** Código de 2 letras (`es`, `en`...) si se conoce; null si la pista no dice su idioma. */
  language: string | null;
  codec: string;
  source: SubtitleSource;
  isDefault: boolean;
  isForced: boolean;
  /** WebVTT de la pista: /api/items/{id}/subtitles/{trackId}.vtt */
  url: string;
}

/** Lo que se guarda como preferencia cuando el usuario elige "Desactivados". */
export const SUBTITLES_OFF = 'off';

/**
 * Qué pista se activa sola al abrir un video (PLAN.md, sección 11):
 * 1. Si el usuario eligió "Desactivados", ninguna.
 * 2. Si recuerda un idioma y hay una pista de ese idioma, esa (la que no es de forzados, si hay).
 * 3. Si no, una pista marcada como forzada o, después, como predeterminada.
 * 4. Si no, ninguna: por omisión los subtítulos están desactivados.
 */
export function pickInitialTrack(
  tracks: readonly SubtitleTrack[],
  preference: string | null,
): SubtitleTrack | null {
  if (tracks.length === 0 || preference === SUBTITLES_OFF) {
    return null;
  }
  if (preference) {
    const matches = tracks.filter((track) => track.language === preference);
    const match = matches.find((track) => !track.isForced) ?? matches[0];
    if (match) {
      return match;
    }
  }
  return tracks.find((track) => track.isForced) ?? tracks.find((track) => track.isDefault) ?? null;
}

/**
 * Pista que enciende la tecla C o el botón cuando no hay ninguna activa: la preferida por idioma, y si no,
 * la primera que no es de forzados.
 */
export function pickToggleTrack(
  tracks: readonly SubtitleTrack[],
  preference: string | null,
): SubtitleTrack | null {
  if (tracks.length === 0) {
    return null;
  }
  if (preference && preference !== SUBTITLES_OFF) {
    const preferred = tracks.find((track) => track.language === preference && !track.isForced);
    if (preferred) {
      return preferred;
    }
  }
  return tracks.find((track) => !track.isForced) ?? tracks[0];
}

/** Subtítulos de un video. Los datos viven en el servidor; aquí solo se lee la lista. */
@Injectable({ providedIn: 'root' })
export class SubtitlesApi {
  /** Pistas de texto de un video. Sin id no pide nada. Se llama en un contexto de inyección. */
  tracks(id: () => string | undefined): HttpResourceRef<SubtitleTrack[] | undefined> {
    return httpResource<SubtitleTrack[]>(() => {
      const itemId = id();
      return itemId ? `/api/items/${itemId}/subtitles` : undefined;
    });
  }
}
