import {
  Component,
  DOCUMENT,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';

import { PlaybackApi, type PlaybackInfo } from '../../core/playback/playback-api';
import { PlayerService } from '../../core/playback/player-service';
import { MIN_RESUME_SECONDS, formatDuration } from '../../shared/item-display';

/** Cada cuántos milisegundos se guarda el progreso mientras se reproduce. */
export const SAVE_INTERVAL_MS = 10_000;

/** Cuánto tiempo se muestra el aviso "Continuando desde…". */
export const RESUME_NOTICE_MS = 10_000;

/** Segundos de la cuenta regresiva antes de pasar solo al siguiente episodio. */
export const AUTO_NEXT_SECONDS = 5;

/** Segundos que mueven las flechas del teclado. */
const SKIP_SECONDS = 10;

/**
 * Reproductor de video (PLAN.md, secciones 9 y 11): `<video>` a todo lo ancho sobre fondo oscuro que
 * retoma donde se quedó el usuario y guarda el progreso cada 10 segundos, al pausar, al terminar y al
 * salir de la página (con `fetch` y `keepalive`, que sobrevive al cierre). Al terminar marca el
 * episodio como visto y ofrece el siguiente de la misma carpeta, con una cuenta regresiva corta que se
 * puede cancelar. Atajos: flechas para saltar 10 s y F para la pantalla completa.
 */
@Component({
  selector: 'app-player-page',
  imports: [MatButton, MatIcon, MatIconButton, MatProgressBar, RouterLink],
  host: {
    '(window:keydown)': 'onKeydown($event)',
    '(window:pagehide)': 'saveOnExit()',
  },
  template: `
    <div class="player">
      <header class="top">
        <a matIconButton class="back" [routerLink]="['/items', id()]" aria-label="Volver al detalle">
          <mat-icon aria-hidden="true">arrow_back</mat-icon>
        </a>
        <h1 class="heading">{{ heading() }}</h1>
        @if (current(); as current) {
          @if (current.previousId || current.nextId) {
            <nav class="steps" aria-label="Episodios de la carpeta">
              <button
                matButton="text"
                type="button"
                [disabled]="!current.previousId"
                (click)="go(current.previousId)"
              >
                <mat-icon aria-hidden="true">skip_previous</mat-icon>
                Anterior
              </button>
              <button
                matButton="text"
                type="button"
                [disabled]="!current.nextId"
                (click)="go(current.nextId)"
              >
                Siguiente
                <mat-icon aria-hidden="true">skip_next</mat-icon>
              </button>
            </nav>
          }
        }
      </header>

      @if (info.isLoading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Cargando" />
      }

      @if (info.error()) {
        <section class="message" role="alert">
          <h2>No se pudo cargar el video</h2>
          <p>Puede que se haya eliminado o que no tengas acceso.</p>
          <a matButton="filled" routerLink="/">Volver al inicio</a>
        </section>
      } @else if (current(); as current) {
        @if (!current.isVideo) {
          <section class="message">
            <h2>Esto es una pista de música</h2>
            <p>Las pistas se reproducen desde la barra de música, en el detalle del álbum.</p>
            <a matButton="filled" [routerLink]="['/items', id()]">Ir al detalle</a>
          </section>
        } @else {
          <div class="stage" #stage>
            <video
              #video
              class="video"
              controls
              autoplay
              playsinline
              preload="auto"
              [src]="current.streamUrl"
              [attr.aria-label]="heading()"
              (loadedmetadata)="onLoadedMetadata(current)"
              (play)="onPlay()"
              (pause)="onPause()"
              (ended)="onEnded(current)"
              (error)="onError()"
            ></video>

            @if (resumedAt() !== null && !ended() && !failed()) {
              <div class="notice" role="status">
                <span>Continuando desde {{ time(resumedAt()) }}</span>
                <button matButton="text" type="button" (click)="startOver()">
                  Empezar desde el inicio
                </button>
              </div>
            }

            @if (failed()) {
              <section class="overlay" role="alert" aria-labelledby="failed-title">
                <h2 id="failed-title">Tu navegador no puede reproducir este archivo</h2>
                <p>
                  El formato o los códecs ({{ current.contentType }}) no son compatibles. Puedes
                  descargarlo y verlo con otro reproductor.
                </p>
                <div class="actions">
                  <a matButton="filled" [href]="current.streamUrl" download>
                    <mat-icon aria-hidden="true">download</mat-icon>
                    Descargar archivo
                  </a>
                  <a matButton="outlined" [routerLink]="['/items', id()]">Volver al detalle</a>
                </div>
              </section>
            } @else if (ended()) {
              <section class="overlay" aria-labelledby="ended-title">
                <h2 id="ended-title">Terminó</h2>
                @if (current.nextId) {
                  <p>
                    Sigue: <strong>{{ current.nextTitle }}</strong>
                    @if (countdown() !== null) {
                      <span role="status"> · empieza en {{ countdown() }} s</span>
                    }
                  </p>
                }
                <div class="actions">
                  @if (current.nextId) {
                    <button matButton="filled" type="button" (click)="go(current.nextId)">
                      <mat-icon aria-hidden="true">skip_next</mat-icon>
                      Siguiente episodio
                    </button>
                  }
                  @if (countdown() !== null) {
                    <button matButton="outlined" type="button" (click)="cancelCountdown()">
                      Cancelar
                    </button>
                  }
                  <button matButton="outlined" type="button" (click)="replay()">
                    <mat-icon aria-hidden="true">replay</mat-icon>
                    Volver a ver
                  </button>
                  <a matButton="outlined" [routerLink]="['/items', id()]">Volver al detalle</a>
                </div>
              </section>
            }
          </div>
        }
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      background: #000;
      color: var(--mat-sys-on-surface);
    }

    .player {
      display: flex;
      flex-direction: column;
      min-height: 100dvh;
    }

    .top {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 16px;
      background: rgb(0 0 0 / 85%);
    }

    .heading {
      flex: 1;
      min-width: 0;
      margin: 0;
      font: var(--mat-sys-title-medium);
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .steps {
      display: flex;
      gap: 4px;
    }

    .stage {
      position: relative;
      display: flex;
      flex: 1;
      min-height: 0;
      background: #000;
    }

    .video {
      width: 100%;
      height: calc(100dvh - 64px);
      object-fit: contain;
      background: #000;
    }

    .video:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: -3px;
    }

    .notice {
      position: absolute;
      bottom: 72px;
      left: 16px;
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 4px 8px 4px 16px;
      border-radius: var(--mat-sys-corner-full);
      background: rgb(0 0 0 / 80%);
      font: var(--mat-sys-body-medium);
    }

    .overlay {
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 8px;
      padding: 24px;
      text-align: center;
      background: rgb(0 0 0 / 80%);
    }

    .overlay h2,
    .message h2 {
      margin: 0;
      font: var(--mat-sys-headline-small);
    }

    .overlay p,
    .message p {
      max-width: 60ch;
      margin: 0 0 8px;
      color: var(--mat-sys-on-surface-variant);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: 12px;
    }

    .message {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 64px 24px;
      text-align: center;
    }

    @media (prefers-reduced-motion: reduce) {
      * {
        transition: none !important;
        animation: none !important;
      }
    }
  `,
})
export class PlayerPage {
  /** Id de la película o el episodio, desde la ruta /reproducir/:id. */
  readonly id = input.required<string>();

  private readonly api = inject(PlaybackApi);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  protected readonly info = this.api.info(() => this.id());

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private readonly stage = viewChild<ElementRef<HTMLElement>>('stage');

  /** Segundos desde los que se retomó el video; null si empezó de cero o el aviso ya se ocultó. */
  protected readonly resumedAt = signal<number | null>(null);
  /** El video llegó al final. */
  protected readonly ended = signal(false);
  /** El navegador no pudo reproducir el archivo. */
  protected readonly failed = signal(false);
  /** Segundos que faltan para pasar solo al siguiente episodio; null si no hay cuenta regresiva. */
  protected readonly countdown = signal<number | null>(null);

  /** Los datos de reproducción, o undefined mientras cargan o si fallaron (en Angular 22, value() con error lanza). */
  protected readonly current = computed(() => (this.info.hasValue() ? this.info.value() : undefined));

  /** "Serie · Lat · Episodio 3". */
  protected readonly heading = computed(() => {
    const current = this.current();
    return current
      ? [current.seriesTitle, current.parentTitle, current.title].filter((part) => !!part).join(' · ')
      : '';
  });

  /**
   * Ya se aplicó la posición guardada: a partir de aquí hay algo que guardar. Antes, guardar un 0
   * borraría el progreso del usuario.
   */
  private ready = false;
  private lastVideo: HTMLVideoElement | undefined;
  /** El episodio terminó y ya se marcó como visto: salir de la página no debe volver a guardar. */
  private finished = false;
  private saveTimer: ReturnType<typeof setInterval> | undefined;
  private noticeTimer: ReturnType<typeof setTimeout> | undefined;
  private countdownTimer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    // La música y el video no suenan a la vez.
    inject(PlayerService).pause();

    // Al pasar a otro episodio la página se reutiliza: se empieza de cero.
    effect(() => {
      this.id();
      untracked(() => this.reset());
    });

    inject(DestroyRef).onDestroy(() => {
      this.saveOnExit();
      this.clearTimers();
    });
  }

  protected time(seconds: number | null): string {
    return formatDuration(seconds) || '0:00';
  }

  /** Al conocer el video: retoma la posición guardada y habilita el guardado. */
  protected onLoadedMetadata(info: PlaybackInfo): void {
    const video = this.video()?.nativeElement;
    if (!video) {
      return;
    }
    const saved = info.positionSeconds;
    const duration = Number.isFinite(video.duration) ? video.duration : (info.durationSeconds ?? 0);
    if (saved >= MIN_RESUME_SECONDS && (duration <= 0 || saved < duration - MIN_RESUME_SECONDS)) {
      video.currentTime = saved;
      this.resumedAt.set(saved);
      clearTimeout(this.noticeTimer);
      this.noticeTimer = setTimeout(() => this.resumedAt.set(null), RESUME_NOTICE_MS);
    }
    this.lastVideo = video;
    this.ready = true;
  }

  protected onPlay(): void {
    this.ended.set(false);
    this.finished = false;
    this.cancelCountdown();
    clearInterval(this.saveTimer);
    this.saveTimer = setInterval(() => this.save(), SAVE_INTERVAL_MS);
  }

  protected onPause(): void {
    clearInterval(this.saveTimer);
    // Al llegar al final el video se pausa antes de avisar que terminó: eso lo guarda onEnded.
    if (!this.activeVideo()?.ended) {
      this.save();
    }
  }

  protected onEnded(info: PlaybackInfo): void {
    clearInterval(this.saveTimer);
    this.finished = true;
    this.ended.set(true);
    this.api.markPlayed(this.id()).catch(() => undefined);
    if (info.nextId) {
      this.startCountdown(info.nextId);
    }
  }

  protected onError(): void {
    clearInterval(this.saveTimer);
    this.ready = false;
    this.failed.set(true);
  }

  /** Vuelve al principio y oculta el aviso de que se retomó. */
  protected startOver(): void {
    const video = this.video()?.nativeElement;
    if (video) {
      video.currentTime = 0;
    }
    clearTimeout(this.noticeTimer);
    this.resumedAt.set(null);
  }

  protected replay(): void {
    const video = this.video()?.nativeElement;
    if (!video) {
      return;
    }
    this.cancelCountdown();
    video.currentTime = 0;
    Promise.resolve(video.play()).catch(() => undefined);
  }

  /** Guarda el progreso y pasa a otro episodio. */
  protected go(targetId: string | null): void {
    if (!targetId) {
      return;
    }
    this.cancelCountdown();
    this.saveOnExit();
    void this.router.navigate(['/reproducir', targetId]);
  }

  protected cancelCountdown(): void {
    clearInterval(this.countdownTimer);
    this.countdown.set(null);
  }

  /** Guarda la posición con `fetch` y `keepalive`: sobrevive al cierre de la pestaña. */
  saveOnExit(): void {
    const video = this.activeVideo();
    if (!this.ready || this.finished || !video || !Number.isFinite(video.currentTime)) {
      return;
    }
    this.api.saveProgressOnExit(this.id(), video.currentTime);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }
    const target = event.target as HTMLElement | null;
    if (target && (['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) || target.isContentEditable)) {
      return;
    }
    const video = this.video()?.nativeElement;
    if (!video) {
      return;
    }
    switch (event.key) {
      case 'ArrowRight':
        event.preventDefault();
        this.skip(video, SKIP_SECONDS);
        break;
      case 'ArrowLeft':
        event.preventDefault();
        this.skip(video, -SKIP_SECONDS);
        break;
      case 'f':
      case 'F':
        event.preventDefault();
        this.toggleFullscreen();
        break;
    }
  }

  private skip(video: HTMLVideoElement, seconds: number): void {
    const target = video.currentTime + seconds;
    video.currentTime = Number.isFinite(video.duration)
      ? Math.min(Math.max(target, 0), video.duration)
      : Math.max(target, 0);
  }

  private toggleFullscreen(): void {
    if (this.document.fullscreenElement) {
      this.document.exitFullscreen?.()?.catch(() => undefined);
    } else {
      this.stage()?.nativeElement.requestFullscreen?.()?.catch(() => undefined);
    }
  }

  /**
   * El elemento de video. Al salir de la página, la vista ya se está destruyendo y la consulta puede
   * estar vacía: por eso se guarda también el último que se vio (su posición sigue ahí).
   */
  private activeVideo(): HTMLVideoElement | undefined {
    return this.video()?.nativeElement ?? this.lastVideo;
  }

  /** Guardado periódico: solo mientras se reproduce, con la API normal (con respuesta y errores ignorados). */
  private save(): void {
    const video = this.activeVideo();
    if (!this.ready || this.finished || !video || !Number.isFinite(video.currentTime)) {
      return;
    }
    this.api.saveProgress(this.id(), video.currentTime).catch(() => undefined);
  }

  private startCountdown(nextId: string): void {
    this.cancelCountdown();
    this.countdown.set(AUTO_NEXT_SECONDS);
    this.countdownTimer = setInterval(() => {
      const left = (this.countdown() ?? 0) - 1;
      if (left <= 0) {
        this.go(nextId);
      } else {
        this.countdown.set(left);
      }
    }, 1000);
  }

  private reset(): void {
    this.clearTimers();
    this.ready = false;
    this.finished = false;
    this.lastVideo = undefined;
    this.resumedAt.set(null);
    this.ended.set(false);
    this.failed.set(false);
    this.countdown.set(null);
  }

  private clearTimers(): void {
    clearInterval(this.saveTimer);
    clearInterval(this.countdownTimer);
    clearTimeout(this.noticeTimer);
  }
}
