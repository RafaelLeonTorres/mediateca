import { InjectionToken, Injectable, computed, inject, signal } from '@angular/core';

import { PlaybackApi, streamUrl } from './playback-api';

/**
 * Elemento de audio de la barra de música. Es un token para que las pruebas pongan uno falso: en jsdom
 * `play()` y `pause()` no hacen nada. Un solo elemento para toda la app, así la música sigue sonando
 * al cambiar de pantalla.
 */
export const PLAYER_AUDIO = new InjectionToken<HTMLAudioElement>('PLAYER_AUDIO', {
  providedIn: 'root',
  factory: () => new Audio(),
});

/** Una pista de la cola. */
export interface QueueTrack {
  id: string;
  title: string;
  artist: string | null;
  /** Título del álbum. */
  album: string | null;
  durationSeconds: number | null;
  /** Elemento cuya imagen es la carátula (la pista o su álbum); null si ninguno tiene. */
  imageId: string | null;
}

/** Si al volver a la pista anterior ya pasaron más segundos que estos, se reinicia la pista en vez de retroceder. */
export const RESTART_AFTER_SECONDS = 3;

/**
 * Reproductor de música (PLAN.md, sección 9): la cola, la pista actual y el estado de la reproducción
 * como signals, para que la barra fija de abajo y el detalle del álbum lean lo mismo. Al terminar una
 * pista la marca como vista y sigue con la siguiente.
 */
@Injectable({ providedIn: 'root' })
export class PlayerService {
  private readonly audio = inject(PLAYER_AUDIO);
  private readonly api = inject(PlaybackApi);

  /** Pistas en cola, en orden. */
  readonly queue = signal<readonly QueueTrack[]>([]);
  /** Posición de la pista actual en la cola; -1 si no hay cola. */
  readonly index = signal(-1);
  /** Está sonando (no en pausa). */
  readonly playing = signal(false);
  /** Segundos que lleva la pista actual. */
  readonly position = signal(0);
  /** Duración de la pista actual en segundos; 0 si no se conoce. */
  readonly duration = signal(0);
  /** Mensaje cuando la pista actual no se pudo reproducir. */
  readonly error = signal<string | null>(null);

  readonly current = computed(() => this.queue()[this.index()] ?? null);
  readonly hasPrevious = computed(() => this.index() > 0);
  readonly hasNext = computed(() => this.index() >= 0 && this.index() < this.queue().length - 1);

  constructor() {
    const audio = this.audio;
    audio.addEventListener('play', () => this.playing.set(true));
    audio.addEventListener('pause', () => this.playing.set(false));
    audio.addEventListener('timeupdate', () => this.position.set(audio.currentTime));
    audio.addEventListener('loadedmetadata', () => this.syncDuration());
    audio.addEventListener('durationchange', () => this.syncDuration());
    audio.addEventListener('ended', () => this.onEnded());
    audio.addEventListener('error', () => this.onError());
  }

  /** Reemplaza la cola y empieza por la pista indicada. */
  play(tracks: readonly QueueTrack[], startIndex = 0): void {
    if (tracks.length === 0) {
      return;
    }
    this.queue.set([...tracks]);
    this.load(Math.min(Math.max(startIndex, 0), tracks.length - 1));
  }

  /** Salta a una pista de la cola. */
  playAt(index: number): void {
    if (index >= 0 && index < this.queue().length) {
      this.load(index);
    }
  }

  /** Pausa si está sonando; si no, sigue donde iba. */
  toggle(): void {
    if (this.current() === null) {
      return;
    }
    if (this.playing()) {
      this.audio.pause();
    } else {
      this.start();
    }
  }

  /** Pausa (por ejemplo, al abrir un video). No hace nada si no está sonando. */
  pause(): void {
    if (this.playing()) {
      this.audio.pause();
    }
  }

  next(): void {
    if (this.hasNext()) {
      this.load(this.index() + 1);
    }
  }

  /** Vuelve a la pista anterior; si la actual ya lleva unos segundos, la reinicia. */
  previous(): void {
    if (this.hasPrevious() && this.position() <= RESTART_AFTER_SECONDS) {
      this.load(this.index() - 1);
    } else {
      this.seek(0);
    }
  }

  /** Mueve la pista actual a esa posición, en segundos. */
  seek(seconds: number): void {
    if (this.current() === null || !Number.isFinite(seconds)) {
      return;
    }
    const limit = this.duration() > 0 ? this.duration() : Number.POSITIVE_INFINITY;
    const target = Math.min(Math.max(seconds, 0), limit);
    this.audio.currentTime = target;
    this.position.set(target);
  }

  /** Detiene todo y vacía la cola; la barra desaparece. */
  stop(): void {
    this.pause();
    this.audio.removeAttribute('src');
    this.queue.set([]);
    this.index.set(-1);
    this.position.set(0);
    this.duration.set(0);
    this.error.set(null);
    this.playing.set(false);
  }

  private load(index: number): void {
    const track = this.queue()[index];
    if (!track) {
      return;
    }
    this.index.set(index);
    this.error.set(null);
    this.position.set(0);
    this.duration.set(track.durationSeconds ?? 0);
    this.audio.src = streamUrl(track.id);
    this.start();
  }

  private start(): void {
    // En los navegadores play() devuelve una promesa que falla si otra pista toma su lugar (AbortError)
    // o si el navegador no deja empezar sin una acción del usuario.
    Promise.resolve(this.audio.play()).catch((error: unknown) => {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return;
      }
      this.playing.set(false);
    });
  }

  private syncDuration(): void {
    const duration = this.audio.duration;
    if (Number.isFinite(duration) && duration > 0) {
      this.duration.set(duration);
    }
  }

  private onEnded(): void {
    const track = this.current();
    if (track) {
      // Una pista que llegó al final está vista; si falla el guardado, la música no se detiene.
      this.api.markPlayed(track.id).catch(() => undefined);
    }
    if (this.hasNext()) {
      this.load(this.index() + 1);
    } else {
      this.playing.set(false);
    }
  }

  private onError(): void {
    const track = this.current();
    // Al vaciar la cola se quita el src, y eso también dispara "error": no es un fallo.
    if (!track || !this.audio.getAttribute('src')) {
      return;
    }
    this.playing.set(false);
    this.error.set(`No se pudo reproducir «${track.title}». Prueba con la siguiente pista.`);
  }
}
