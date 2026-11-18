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
import {
  SUBTITLES_OFF,
  SubtitlesApi,
  type SubtitleTrack,
  pickInitialTrack,
  pickToggleTrack,
} from '../../core/playback/subtitles-api';
import { MIN_RESUME_SECONDS, formatDuration } from '../../shared/item-display';
import { CompareDivider, type VideoCover } from './compare-divider';
import type { EnhancerStats, GpuInfo, OutputRect, Size } from './enhancer/enhancer-params';
import { EnhancerSession } from './enhancer-session';
import { PlayerControls } from './player-controls';
import {
  readRescaleSettings,
  readSubtitlePreference,
  writeRescaleSettings,
  writeSubtitlePreference,
} from './player-preferences';
import { RescalePanel } from './rescale-panel';
import {
  DEFAULT_RESCALE,
  DEFAULT_SPLIT,
  type RescaleSettings,
  toEnhancerParams,
} from './rescale-settings';
import { SubtitleLayer } from './subtitle-layer';
import { SubtitleMenu } from './subtitle-menu';
import { type ActiveCue, SubtitleTrackBinder } from './subtitle-track-binder';
import { VIDEO_ENHANCER_FACTORY, WEBGPU_SUPPORT } from './video-enhancer';

/** Cada cuántos milisegundos se guarda el progreso mientras se reproduce. */
export const SAVE_INTERVAL_MS = 10_000;

/** Cuánto tiempo se muestra el aviso "Continuando desde…". */
export const RESUME_NOTICE_MS = 10_000;

/** Segundos de la cuenta regresiva antes de pasar solo al siguiente episodio. */
export const AUTO_NEXT_SECONDS = 5;

/** Segundos que mueven las flechas del teclado. */
const SKIP_SECONDS = 10;

/** Sin mover el ratón durante este tiempo, mientras se reproduce, la barra de controles se oculta. */
export const CONTROLS_HIDE_MS = 3000;

/**
 * Reproductor de video (PLAN.md, secciones 9 y 11): el escenario (video, lienzo de WebGPU, subtítulos y barra de
 * controles) a pantalla completa sobre fondo oscuro. Retoma donde se quedó el usuario y guarda el progreso cada 10
 * segundos, al pausar, al terminar y al salir de la página (con `fetch` y `keepalive`, que sobrevive al cierre).
 * Al terminar marca el episodio como visto y ofrece el siguiente de la misma carpeta, con una cuenta regresiva
 * corta que se puede cancelar.
 *
 * Los controles son propios (la barra nativa quedaría tapada por el lienzo): se ocultan solos a los 3 s sin mover
 * el ratón. Con "Reescalar" el video pasa por la GPU (WebGPU); empieza en Original, sin trabajo de GPU. Los
 * subtítulos se dibujan en una capa HTML encima de todo, así se ven con o sin reescalado. El botón CC siempre está:
 * también sirve para subir un archivo de subtítulos cuando el video no trae.
 *
 * Atajos: espacio o K (reproducir y pausar), flechas (±10 s), M (silencio), C (subtítulos) y F (pantalla completa).
 */
@Component({
  selector: 'app-player-page',
  imports: [
    CompareDivider,
    MatButton,
    MatIcon,
    MatIconButton,
    MatProgressBar,
    PlayerControls,
    RescalePanel,
    RouterLink,
    SubtitleLayer,
    SubtitleMenu,
  ],
  host: {
    '(window:keydown)': 'onKeydown($event)',
    '(window:pagehide)': 'saveOnExit()',
    '(document:fullscreenchange)': 'onFullscreenChange()',
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
          <div
            class="stage"
            #stage
            [class.idle]="playing() && !controlsShown()"
            (pointermove)="onPointer()"
            (pointerdown)="onPointer()"
          >
            <video
              #video
              class="video"
              autoplay
              playsinline
              preload="auto"
              [src]="current.streamUrl"
              [attr.aria-label]="heading()"
              (loadedmetadata)="onLoadedMetadata(current)"
              (durationchange)="onDurationChange()"
              (timeupdate)="onTimeUpdate()"
              (volumechange)="onVolumeChange()"
              (play)="onPlay()"
              (pause)="onPause()"
              (ended)="onEnded(current)"
              (error)="onError()"
              (click)="togglePlay()"
              (dblclick)="toggleFullscreen()"
            ></video>
            <canvas
              #canvas
              class="canvas"
              [class.visible]="enhancing()"
              aria-hidden="true"
              (click)="togglePlay()"
              (dblclick)="toggleFullscreen()"
            ></canvas>

            @if (view() === 'compare') {
              <app-compare-divider
                [cover]="cover()"
                [split]="split()"
                (splitChange)="split.set($event)"
              />
            }

            <app-subtitle-layer [cues]="cues()" [raised]="barVisible()" />

            @if (rescaleError(); as message) {
              <div class="notice alert" role="alert">
                <span>La GPU falló: {{ message }} Se volvió al video original.</span>
                <button matButton="text" type="button" (click)="rescaleError.set(null)">Cerrar</button>
              </div>
            }

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

            <app-player-controls
              [visible]="barVisible()"
              [playing]="playing()"
              [currentTime]="currentTime()"
              [duration]="duration()"
              [volume]="volume()"
              [muted]="muted()"
              [fullscreen]="fullscreen()"
              [hasPrevious]="!!current.previousId"
              [hasNext]="!!current.nextId"
              (togglePlay)="togglePlay()"
              (seek)="seekTo($event)"
              (skip)="skipBy($event)"
              (volumeChange)="setVolume($event)"
              (toggleMute)="toggleMute()"
              (goPrevious)="go(current.previousId)"
              (goNext)="go(current.nextId)"
              (toggleFullscreen)="toggleFullscreen()"
              (focusin)="barFocused.set(true)"
              (focusout)="onBarFocusOut($event)"
            >
              <app-subtitle-menu
                [itemId]="id()"
                [tracks]="tracks()"
                [activeId]="activeTrackId()"
                (choose)="chooseSubtitle($event)"
                (added)="onSubtitleAdded($event)"
                (removed)="onSubtitleRemoved($event)"
                (openChange)="subtitleMenuOpen.set($event)"
              />
              <app-rescale-panel
                [settings]="panelSettings()"
                [supported]="webgpu"
                [info]="rescaleInfo()"
                (settingsChange)="setRescale($event)"
                (reset)="resetRescale()"
                (openChange)="rescaleOpen.set($event)"
              />
            </app-player-controls>
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
      flex: 1;
      min-height: calc(100dvh - 64px);
      overflow: hidden;
      background: #000;
    }

    .stage.idle {
      cursor: none;
    }

    .video,
    .canvas {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      background: #000;
    }

    .video {
      object-fit: contain;
    }

    .canvas {
      display: none;
    }

    .canvas.visible {
      display: block;
    }

    .notice {
      position: absolute;
      bottom: 96px;
      left: 16px;
      z-index: 3;
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 4px 8px 4px 16px;
      border-radius: var(--mat-sys-corner-full);
      background: rgb(0 0 0 / 80%);
      font: var(--mat-sys-body-medium);
    }

    .notice.alert {
      top: 16px;
      bottom: auto;
      left: 50%;
      max-width: calc(100% - 32px);
      transform: translateX(-50%);
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }

    .overlay {
      position: absolute;
      inset: 0;
      z-index: 3;
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
  private readonly subtitlesApi = inject(SubtitlesApi);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly enhancerFactory = inject(VIDEO_ENHANCER_FACTORY);

  /** El navegador tiene WebGPU; sin él, Reescalar queda deshabilitado. */
  protected readonly webgpu = inject(WEBGPU_SUPPORT);

  protected readonly info = this.api.info(() => this.id());
  private readonly subtitleList = this.subtitlesApi.tracks(() => this.id());

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly stage = viewChild<ElementRef<HTMLElement>>('stage');

  /** Segundos desde los que se retomó el video; null si empezó de cero o el aviso ya se ocultó. */
  protected readonly resumedAt = signal<number | null>(null);
  /** El video llegó al final. */
  protected readonly ended = signal(false);
  /** El navegador no pudo reproducir el archivo. */
  protected readonly failed = signal(false);
  /** Segundos que faltan para pasar solo al siguiente episodio; null si no hay cuenta regresiva. */
  protected readonly countdown = signal<number | null>(null);

  // ── Estado del <video>, para la barra de controles ──
  protected readonly playing = signal(false);
  protected readonly currentTime = signal(0);
  protected readonly duration = signal(0);
  protected readonly volume = signal(1);
  protected readonly muted = signal(false);
  protected readonly fullscreen = signal(false);

  // ── Barra de controles: se oculta sola ──
  protected readonly controlsShown = signal(true);
  protected readonly barFocused = signal(false);
  protected readonly subtitleMenuOpen = signal(false);
  protected readonly rescaleOpen = signal(false);
  /** La barra se ve: no está oculta por la inactividad y nada la tapa (fin del video o un error). */
  protected readonly barVisible = computed(
    () => this.controlsShown() && !this.ended() && !this.failed(),
  );

  // ── Reescalado con la GPU ──
  protected readonly rescale = signal<RescaleSettings>(readRescaleSettings());
  /** Fracción del video que se ve sin mejorar en la vista Comparar. */
  protected readonly split = signal(DEFAULT_SPLIT);
  protected readonly cover = signal<VideoCover | null>(null);
  protected readonly gpu = signal<GpuInfo | null>(null);
  protected readonly fps = signal<number | null>(null);
  protected readonly rescaleError = signal<string | null>(null);
  /** Sin WebGPU, todo es Original aunque lo guardado diga otra cosa. */
  protected readonly view = computed(() => (this.webgpu ? this.rescale().view : 'original'));
  protected readonly panelSettings = computed<RescaleSettings>(() => ({
    ...this.rescale(),
    view: this.view(),
  }));
  /** El lienzo se ve: se pidió Mejorado o Comparar y ya dibujó el primer cuadro. */
  protected readonly enhancing = computed(() => this.view() !== 'original' && this.cover() !== null);
  protected readonly rescaleInfo = computed(() => {
    const gpu = this.gpu();
    if (!gpu || this.view() === 'original') {
      return null;
    }
    const name = [gpu.vendor, gpu.architecture].filter(Boolean).join(' ') || gpu.description || 'GPU';
    const label = gpu.isFallback ? `${name} (por software: lento)` : name;
    const fps = this.fps();
    return fps ? `${label} · ${fps} cuadros/s` : label;
  });

  // ── Subtítulos ──
  protected readonly tracks = computed(() =>
    this.subtitleList.hasValue() ? (this.subtitleList.value() ?? []) : [],
  );
  protected readonly activeTrackId = signal<string | null>(null);
  protected readonly cues = signal<readonly ActiveCue[]>([]);

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
  private hideTimer: ReturnType<typeof setTimeout> | undefined;
  /** La última interacción fue con el teclado: el foco en la barra la mantiene a la vista. */
  private keyboardMode = false;

  private session: EnhancerSession | undefined;
  private sessionVideo: HTMLVideoElement | undefined;
  private binder: SubtitleTrackBinder | undefined;
  private binderVideo: HTMLVideoElement | undefined;
  /** Última pista que el usuario eligió en este video: la que vuelve a encender la tecla C. */
  private lastTrackId: string | null = null;
  /** El episodio cuya pista inicial ya se eligió: subir o borrar un archivo no la vuelve a elegir. */
  private tracksPickedFor: string | null = null;
  /** La pista que se acaba de subir: se activa en cuanto aparece en la lista. */
  private pendingTrackId: string | null = null;

  constructor() {
    // La música y el video no suenan a la vez.
    inject(PlayerService).pause();

    // Al pasar a otro episodio la página se reutiliza: se empieza de cero.
    effect(() => {
      this.id();
      untracked(() => this.reset());
    });

    // El enhancer sigue al <video> y a los ajustes. En Original no crea nada.
    effect(() => {
      const video = this.video()?.nativeElement;
      const canvas = this.canvas()?.nativeElement;
      const stage = this.stage()?.nativeElement;
      const settings = this.panelSettings();
      const split = this.split();
      untracked(() => this.syncEnhancer(video, canvas, stage, settings, split));
    });

    // Al conocer las pistas de un episodio se activa la que corresponde (idioma recordado, forzada o predeterminada).
    // Después, la lista solo cambia por un archivo subido o borrado.
    effect(() => {
      const tracks = this.tracks();
      const loaded = this.subtitleList.hasValue();
      untracked(() => this.onTracks(tracks, loaded));
    });

    // La pista activa se carga en el <video> como <track> oculto.
    effect(() => {
      const video = this.video()?.nativeElement;
      const track = this.tracks().find((candidate) => candidate.id === this.activeTrackId()) ?? null;
      untracked(() => this.syncSubtitles(video, track));
    });

    inject(DestroyRef).onDestroy(() => {
      this.saveOnExit();
      this.clearTimers();
      this.disposeSession();
      this.binder?.detach();
    });
  }

  protected time(seconds: number | null): string {
    return formatDuration(seconds) || '0:00';
  }

  // ───────────────────────── eventos del <video> ─────────────────────────

  /** Al conocer el video: retoma la posición guardada y habilita el guardado. */
  protected onLoadedMetadata(info: PlaybackInfo): void {
    const video = this.video()?.nativeElement;
    if (!video) {
      return;
    }
    const saved = info.positionSeconds;
    const duration = Number.isFinite(video.duration) ? video.duration : (info.durationSeconds ?? 0);
    this.duration.set(duration);
    if (saved >= MIN_RESUME_SECONDS && (duration <= 0 || saved < duration - MIN_RESUME_SECONDS)) {
      video.currentTime = saved;
      this.currentTime.set(saved);
      this.resumedAt.set(saved);
      clearTimeout(this.noticeTimer);
      this.noticeTimer = setTimeout(() => this.resumedAt.set(null), RESUME_NOTICE_MS);
    }
    this.lastVideo = video;
    this.ready = true;
    this.onVolumeChange();
  }

  protected onDurationChange(): void {
    const video = this.video()?.nativeElement;
    if (video && Number.isFinite(video.duration)) {
      this.duration.set(video.duration);
    }
  }

  protected onTimeUpdate(): void {
    const video = this.video()?.nativeElement;
    if (video) {
      this.currentTime.set(video.currentTime);
    }
  }

  protected onVolumeChange(): void {
    const video = this.video()?.nativeElement;
    if (video) {
      this.volume.set(video.volume);
      this.muted.set(video.muted);
    }
  }

  protected onPlay(): void {
    this.playing.set(true);
    this.ended.set(false);
    this.finished = false;
    this.cancelCountdown();
    clearInterval(this.saveTimer);
    this.saveTimer = setInterval(() => this.save(), SAVE_INTERVAL_MS);
    this.wake();
  }

  protected onPause(): void {
    this.playing.set(false);
    this.controlsShown.set(true);
    clearInterval(this.saveTimer);
    // Al llegar al final el video se pausa antes de avisar que terminó: eso lo guarda onEnded.
    if (!this.activeVideo()?.ended) {
      this.save();
    }
  }

  protected onEnded(info: PlaybackInfo): void {
    clearInterval(this.saveTimer);
    this.playing.set(false);
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

  // ───────────────────────── controles ─────────────────────────

  protected togglePlay(): void {
    const video = this.video()?.nativeElement;
    if (!video) {
      return;
    }
    if (video.paused || video.ended) {
      Promise.resolve(video.play()).catch(() => undefined);
    } else {
      video.pause();
    }
  }

  protected seekTo(seconds: number): void {
    const video = this.video()?.nativeElement;
    if (!video || !Number.isFinite(seconds)) {
      return;
    }
    const target = Number.isFinite(video.duration)
      ? Math.min(Math.max(seconds, 0), video.duration)
      : Math.max(seconds, 0);
    video.currentTime = target;
    this.currentTime.set(target);
  }

  protected skipBy(seconds: number): void {
    const video = this.video()?.nativeElement;
    if (video) {
      this.seekTo(video.currentTime + seconds);
    }
  }

  protected setVolume(value: number): void {
    const video = this.video()?.nativeElement;
    if (!video || !Number.isFinite(value)) {
      return;
    }
    video.volume = Math.min(1, Math.max(0, value));
    video.muted = value <= 0;
    this.onVolumeChange();
  }

  protected toggleMute(): void {
    const video = this.video()?.nativeElement;
    if (video) {
      video.muted = !video.muted;
      this.onVolumeChange();
    }
  }

  /** La pantalla completa es el escenario entero: video, lienzo, subtítulos y barra. */
  protected toggleFullscreen(): void {
    if (this.document.fullscreenElement) {
      this.document.exitFullscreen?.()?.catch(() => undefined);
    } else {
      this.stage()?.nativeElement.requestFullscreen?.()?.catch(() => undefined);
    }
  }

  onFullscreenChange(): void {
    const stage = this.stage()?.nativeElement;
    this.fullscreen.set(!!stage && this.document.fullscreenElement === stage);
    this.wake();
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

  // ───────────────────────── barra de controles que se oculta sola ─────────────────────────

  /** Mostrar la barra y volver a contar los 3 s: al mover el ratón, tocar, teclear o cambiar de pantalla. */
  protected wake(): void {
    this.controlsShown.set(true);
    clearTimeout(this.hideTimer);
    this.hideTimer = setTimeout(() => this.hideIfIdle(), CONTROLS_HIDE_MS);
  }

  /** Con el ratón o el dedo la barra se oculta aunque conserve el foco. */
  protected onPointer(): void {
    this.keyboardMode = false;
    this.wake();
  }

  protected onBarFocusOut(event: FocusEvent): void {
    const bar = event.currentTarget as HTMLElement | null;
    if (!bar?.contains(event.relatedTarget as Node | null)) {
      this.barFocused.set(false);
    }
  }

  private hideIfIdle(): void {
    // Solo se oculta mientras se reproduce y nada la necesita: ni un menú abierto, ni el foco del teclado en ella.
    const busy =
      !this.playing() ||
      this.subtitleMenuOpen() ||
      this.rescaleOpen() ||
      (this.keyboardMode && this.barFocused());
    if (!busy) {
      this.controlsShown.set(false);
    }
  }

  // ───────────────────────── reescalado ─────────────────────────

  protected setRescale(next: RescaleSettings): void {
    this.rescale.set(next);
    writeRescaleSettings(next);
    if (next.view !== 'original') {
      this.rescaleError.set(null);
    }
  }

  /** Restablecer vuelve las opciones a su valor por omisión; la vista se queda como está. */
  protected resetRescale(): void {
    this.split.set(DEFAULT_SPLIT);
    this.setRescale({ ...DEFAULT_RESCALE, view: this.rescale().view });
  }

  private syncEnhancer(
    video: HTMLVideoElement | undefined,
    canvas: HTMLCanvasElement | undefined,
    stage: HTMLElement | undefined,
    settings: RescaleSettings,
    split: number,
  ): void {
    if (!video || !canvas || !stage || !this.webgpu) {
      this.disposeSession();
      return;
    }
    if (this.sessionVideo !== video) {
      // Un <video> nuevo (otro episodio): sesión nueva. No crea nada de GPU hasta que se pida Mejorado o Comparar.
      this.disposeSession();
      this.sessionVideo = video;
      this.session = new EnhancerSession(canvas, video, stage, this.enhancerFactory, {
        onReady: (gpu) => this.gpu.set(gpu),
        onStats: (stats: EnhancerStats) => this.fps.set(stats.fps),
        onLayout: (rect: OutputRect, size: Size) => this.cover.set(coverOf(rect, size)),
        onError: (message) => this.failRescale(message),
      });
    }
    void this.session?.apply(settings.view, toEnhancerParams(settings, split));
  }

  /** La GPU falló: se vuelve a Original (y así queda recordado) con un aviso. */
  private failRescale(message: string): void {
    this.rescaleError.set(message);
    if (this.rescale().view !== 'original') {
      this.setRescale({ ...this.rescale(), view: 'original' });
      this.rescaleError.set(message);
    }
  }

  private disposeSession(): void {
    this.session?.destroy();
    this.session = undefined;
    this.sessionVideo = undefined;
    this.gpu.set(null);
    this.fps.set(null);
    this.cover.set(null);
  }

  // ───────────────────────── subtítulos ─────────────────────────

  /** El usuario eligió una pista (o Desactivados) y se recuerda el idioma para el siguiente episodio. */
  protected chooseSubtitle(id: string | null): void {
    this.activeTrackId.set(id);
    if (id === null) {
      writeSubtitlePreference(SUBTITLES_OFF);
      return;
    }
    this.lastTrackId = id;
    writeSubtitlePreference(this.tracks().find((track) => track.id === id)?.language ?? null);
  }

  /** Se subió un archivo: se vuelve a pedir la lista (las etiquetas pueden cambiar) y se activa la pista nueva. */
  protected onSubtitleAdded(track: SubtitleTrack): void {
    this.pendingTrackId = track.id;
    this.subtitleList.reload();
  }

  /** Se borró una pista subida: si era la activa se apagan, sin cambiar el idioma recordado para otros episodios. */
  protected onSubtitleRemoved(trackId: string): void {
    if (this.activeTrackId() === trackId) {
      this.activeTrackId.set(null);
    }
    if (this.lastTrackId === trackId) {
      this.lastTrackId = null;
    }
    this.subtitleList.reload();
  }

  private onTracks(tracks: readonly SubtitleTrack[], loaded: boolean): void {
    const id = this.id();
    if (this.tracksPickedFor !== id) {
      if (!loaded) {
        // Todavía no llega la lista de este episodio: nada de la pista del anterior.
        this.activeTrackId.set(null);
        return;
      }
      this.tracksPickedFor = id;
      const pick = pickInitialTrack(tracks, readSubtitlePreference());
      this.lastTrackId = pick?.id ?? null;
      this.activeTrackId.set(pick?.id ?? null);
      return;
    }
    const pending = this.pendingTrackId;
    if (pending && tracks.some((track) => track.id === pending)) {
      this.pendingTrackId = null;
      this.chooseSubtitle(pending);
      return;
    }
    const active = this.activeTrackId();
    if (loaded && active && !tracks.some((track) => track.id === active)) {
      this.activeTrackId.set(null);
    }
  }

  /** La tecla C: quita los subtítulos o vuelve a poner la última pista (o la del idioma recordado). */
  protected toggleSubtitles(): void {
    const tracks = this.tracks();
    if (tracks.length === 0) {
      return;
    }
    if (this.activeTrackId() !== null) {
      this.chooseSubtitle(null);
      return;
    }
    const track =
      tracks.find((candidate) => candidate.id === this.lastTrackId) ??
      pickToggleTrack(tracks, readSubtitlePreference());
    if (track) {
      this.chooseSubtitle(track.id);
    }
  }

  private syncSubtitles(
    video: HTMLVideoElement | undefined,
    track: { id: string } | null,
  ): void {
    if (!video) {
      this.binder?.detach();
      this.binder = undefined;
      this.binderVideo = undefined;
      return;
    }
    let binder = this.binder;
    if (!binder || this.binderVideo !== video) {
      binder?.detach();
      binder = new SubtitleTrackBinder(video, (cues) => this.cues.set(cues), this.document);
      this.binder = binder;
      this.binderVideo = video;
    }
    const full = this.tracks().find((candidate) => candidate.id === track?.id);
    if (full) {
      binder.attach(full);
    } else {
      binder.detach();
    }
  }

  // ───────────────────────── teclado ─────────────────────────

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
    this.keyboardMode = true;
    this.wake();
    const target = event.target as HTMLElement | null;
    const tag = target?.tagName ?? '';
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) || target?.isContentEditable) {
      return;
    }
    const video = this.video()?.nativeElement;
    if (!video) {
      return;
    }
    const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
    switch (key) {
      case ' ':
      case 'k':
        // Sobre un botón o un enlace el espacio ya lo activa el navegador: no se duplica.
        if (key === ' ' && (tag === 'BUTTON' || tag === 'A')) {
          return;
        }
        event.preventDefault();
        this.togglePlay();
        break;
      case 'ArrowRight':
        event.preventDefault();
        this.skipBy(SKIP_SECONDS);
        break;
      case 'ArrowLeft':
        event.preventDefault();
        this.skipBy(-SKIP_SECONDS);
        break;
      case 'm':
        event.preventDefault();
        this.toggleMute();
        break;
      case 'c':
        event.preventDefault();
        this.toggleSubtitles();
        break;
      case 'f':
        event.preventDefault();
        this.toggleFullscreen();
        break;
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
    this.playing.set(false);
    this.currentTime.set(0);
    this.duration.set(0);
    this.controlsShown.set(true);
    this.rescaleError.set(null);
    this.pendingTrackId = null;
  }

  private clearTimers(): void {
    clearInterval(this.saveTimer);
    clearInterval(this.countdownTimer);
    clearTimeout(this.noticeTimer);
    clearTimeout(this.hideTimer);
  }
}

/** Dónde queda el video dentro del lienzo, en porcentajes del escenario (el comparador se coloca encima). */
function coverOf(rect: OutputRect, canvas: Size): VideoCover {
  return {
    left: (rect.x / canvas.width) * 100,
    top: (rect.y / canvas.height) * 100,
    width: (rect.width / canvas.width) * 100,
    height: (rect.height / canvas.height) * 100,
  };
}
