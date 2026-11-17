import type { ItemKind } from '../core/items/items-api';

/** Ícono de Material Symbols para cada tipo de elemento (se usa como marcador sin imagen). */
export const ITEM_KIND_ICONS: Record<ItemKind, string> = {
  Movie: 'movie',
  Series: 'tv',
  Season: 'video_library',
  Episode: 'tv',
  MusicAlbum: 'album',
  Track: 'music_note',
};

/** Nombre en español de cada tipo de elemento. */
export const ITEM_KIND_LABELS: Record<ItemKind, string> = {
  Movie: 'Película',
  Series: 'Serie',
  Season: 'Temporada',
  Episode: 'Episodio',
  MusicAlbum: 'Álbum',
  Track: 'Pista',
};

/** Desde esta posición (en segundos) tiene sentido ofrecer "Continuar desde"; antes, se empieza de cero. */
export const MIN_RESUME_SECONDS = 5;

/** Porcentaje de avance (0 a 100) para una barra; null si no hay posición, duración o ya se vio. */
export function progressPercent(
  positionSeconds: number | null | undefined,
  durationSeconds: number | null | undefined,
): number | null {
  if (!positionSeconds || positionSeconds <= 0 || !durationSeconds || durationSeconds <= 0) {
    return null;
  }
  return Math.min(100, Math.max(0, (positionSeconds / durationSeconds) * 100));
}

/** Duración legible: 3:05 o 1:02:07. Sin dato, cadena vacía. */
export function formatDuration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds) || seconds < 0) {
    return '';
  }
  const total = Math.round(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const secs = String(total % 60).padStart(2, '0');
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${secs}` : `${minutes}:${secs}`;
}

/** Tamaño legible de un archivo: 1.5 GB, 320 MB. Sin dato, cadena vacía. */
export function formatSize(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || !Number.isFinite(bytes) || bytes < 0) {
    return '';
  }
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${unit === 0 ? value : value.toFixed(1)} ${units[unit]}`;
}
