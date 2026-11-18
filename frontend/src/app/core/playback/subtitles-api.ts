import { HttpClient, HttpErrorResponse, HttpResourceRef, httpResource } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

/** De dónde sale la pista: dentro del video (MP4, MKV...), un archivo suelto junto a él o uno subido desde aquí. */
export type SubtitleSource = 'embedded' | 'external' | 'uploaded';

/** Una pista de subtítulos de texto: GET /api/items/{id}/subtitles. */
export interface SubtitleTrack {
  /** Estable mientras el archivo no cambie: `e2` (stream 2), `x0` (primer archivo suelto) o `u…` (subido). */
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
  /** Quien pregunta la puede borrar: solo las subidas, por quien las subió o un administrador. */
  canDelete: boolean;
}

/** Lo que acepta "Cargar archivo…", igual que el servidor. */
export const SUBTITLE_FILE_TYPES = '.srt,.vtt,.ass,.ssa';

/** Lo más que puede pesar un subtítulo subido: 8 MB, como en el servidor. */
export const MAX_SUBTITLE_BYTES = 8 * 1024 * 1024;

/** Idiomas que se ofrecen al subir un subtítulo; el código vacío deja la pista sin idioma. */
export const SUBTITLE_LANGUAGES: readonly { code: string; label: string }[] = [
  { code: 'es', label: 'Español' },
  { code: 'en', label: 'Inglés' },
  { code: 'ja', label: 'Japonés' },
  { code: 'pt', label: 'Portugués' },
  { code: 'fr', label: 'Francés' },
  { code: 'de', label: 'Alemán' },
  { code: 'it', label: 'Italiano' },
  { code: 'zh', label: 'Chino' },
  { code: 'ko', label: 'Coreano' },
  { code: '', label: 'Otro o sin idioma' },
];

/** Formas en que un nombre de archivo dice su idioma (sin acentos y en minúsculas), como las reconoce el servidor. */
const LANGUAGE_ALIASES: readonly (readonly [string, readonly string[]])[] = [
  ['es', ['es', 'spa', 'esp', 'spanish', 'espanol', 'castellano', 'latino', 'lat']],
  ['en', ['en', 'eng', 'english', 'ingles']],
  ['ja', ['ja', 'jpn', 'jap', 'japanese', 'japones']],
  ['pt', ['pt', 'por', 'ptb', 'portuguese', 'portugues']],
  ['fr', ['fr', 'fre', 'fra', 'french', 'frances']],
  ['de', ['de', 'ger', 'deu', 'german', 'aleman']],
  ['it', ['it', 'ita', 'italian', 'italiano']],
  ['zh', ['zh', 'chi', 'zho', 'chinese', 'chino']],
  ['ko', ['ko', 'kor', 'korean', 'coreano']],
];

const LANGUAGE_WORDS: ReadonlyMap<string, string> = new Map(
  LANGUAGE_ALIASES.flatMap(([code, aliases]) =>
    aliases.map((alias): [string, string] => [alias, code]),
  ),
);

const fold = (text: string) => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

/** El archivo tiene una de las extensiones que acepta el servidor. */
export function isSubtitleFileName(name: string): boolean {
  return /\.(srt|vtt|ass|ssa)$/i.test(name.trim());
}

/**
 * Idioma que se propone al subir un archivo: el que dice su nombre ("Episodio 01.es.srt", "algo.spa.forced.ass" o
 * una palabra como "Spanish"), y si no dice nada, el último que eligió el usuario. Vacío si no hay ninguno.
 */
export function guessSubtitleLanguage(fileName: string, preferred: string | null = null): string {
  const stem = fileName.trim().replace(/\.[^.]*$/, '');
  // Primero las marcas del final, separadas por punto, como las lee el servidor.
  const segments = stem.split('.').slice(1).map(fold).reverse();
  for (const segment of segments) {
    if (segment === 'forced' || segment.startsWith('forzad') || segment === 'default') {
      continue;
    }
    const code = LANGUAGE_WORDS.get(segment.split(/[-_]/)[0] ?? '');
    if (!code) {
      break;
    }
    return code;
  }
  // Después, una palabra larga en cualquier parte: "[Spanish]", "Medalist 02 Español". Las cortas ("de", "en")
  // son palabras comunes en los títulos.
  const words = fold(stem)
    .split(/[^\p{L}\p{N}]+/u)
    .reverse();
  const word = words.find((candidate) => candidate.length >= 4 && LANGUAGE_WORDS.has(candidate));
  if (word) {
    return LANGUAGE_WORDS.get(word) ?? '';
  }
  return preferred && SUBTITLE_LANGUAGES.some((language) => language.code === preferred)
    ? preferred
    : '';
}

/** Mensaje para el usuario cuando falla subir o borrar un subtítulo. */
export function subtitleErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 413) {
      return 'El archivo pesa más de 8 MB.';
    }
    if (error.status === 0) {
      return 'No se pudo conectar con el servidor. Revisa tu conexión e inténtalo de nuevo.';
    }
    const detail = (error.error as { detail?: unknown } | null)?.detail;
    if (typeof detail === 'string' && detail.trim()) {
      return detail;
    }
  }
  return fallback;
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

/** Subtítulos de un video: la lista de pistas, y subir o borrar un archivo propio. Los datos viven en el servidor. */
@Injectable({ providedIn: 'root' })
export class SubtitlesApi {
  private readonly http = inject(HttpClient);

  /** Pistas de texto de un video. Sin id no pide nada. Se llama en un contexto de inyección. */
  tracks(id: () => string | undefined): HttpResourceRef<SubtitleTrack[] | undefined> {
    return httpResource<SubtitleTrack[]>(() => {
      const itemId = id();
      return itemId ? `/api/items/${itemId}/subtitles` : undefined;
    });
  }

  /**
   * Sube un archivo de subtítulos para el video. El cuerpo es el archivo tal cual (no un formulario) y el nombre va
   * en `?name=`. `language` vacío deja la pista sin idioma. Responde la pista nueva, ya con su etiqueta.
   */
  upload(itemId: string, file: File, language: string): Observable<SubtitleTrack> {
    return this.http.post<SubtitleTrack>(`/api/items/${itemId}/subtitles`, file, {
      params: { name: file.name, language },
      headers: { 'Content-Type': 'application/octet-stream' },
    });
  }

  /** Borra un subtítulo subido (solo quien lo subió o un administrador). */
  remove(itemId: string, trackId: string): Observable<void> {
    return this.http.delete<void>(`/api/items/${itemId}/subtitles/${trackId}`);
  }
}
