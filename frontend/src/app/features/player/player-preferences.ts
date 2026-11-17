import { DEFAULT_RESCALE, type RescaleSettings, sanitizeRescale } from './rescale-settings';

/** Claves de localStorage con lo que recuerda el reproductor. */
export const RESCALE_STORAGE_KEY = 'mediateca.player.rescale';
export const SUBTITLE_STORAGE_KEY = 'mediateca.player.subtitles';

/**
 * localStorage, o null si el navegador no lo deja usar (modo privado, cookies bloqueadas): hasta obtener la
 * propiedad puede lanzar. Todo lo de aquí funciona sin él: simplemente no se recuerda nada.
 */
function storage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

function read(key: string): string | null {
  try {
    return storage()?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

function write(key: string, value: string | null): void {
  try {
    if (value === null) {
      storage()?.removeItem(key);
    } else {
      storage()?.setItem(key, value);
    }
  } catch {
    // Sin espacio o sin permiso: no se recuerda y ya.
  }
}

/** Lo último que eligió el usuario en el panel Reescalar; los valores por omisión si no hay nada o está dañado. */
export function readRescaleSettings(): RescaleSettings {
  const text = read(RESCALE_STORAGE_KEY);
  if (text === null) {
    return { ...DEFAULT_RESCALE };
  }
  try {
    return sanitizeRescale(JSON.parse(text));
  } catch {
    return { ...DEFAULT_RESCALE };
  }
}

export function writeRescaleSettings(settings: RescaleSettings): void {
  write(RESCALE_STORAGE_KEY, JSON.stringify(settings));
}

/** Idioma de subtítulos elegido por última vez (`es`), `off` si eligió Desactivados, o null si nunca eligió. */
export function readSubtitlePreference(): string | null {
  const value = read(SUBTITLE_STORAGE_KEY)?.trim();
  return value ? value : null;
}

/** Recuerda el idioma elegido. Null borra la preferencia (por ejemplo, una pista sin idioma). */
export function writeSubtitlePreference(value: string | null): void {
  write(SUBTITLE_STORAGE_KEY, value);
}
