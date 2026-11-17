import type { SubtitleTrack } from '../../core/playback/subtitles-api';

/**
 * Un cue activo. `getCueAsHTML()` (de `VTTCue`) entrega el texto con sus etiquetas de WebVTT (cursiva, negrita)
 * ya convertidas en elementos del DOM; `text` es el respaldo.
 */
export type ActiveCue = TextTrackCue & { text?: string; getCueAsHTML?: () => DocumentFragment };

/** Un código de idioma que el navegador acepta en `srclang`. */
const LANGUAGE_PATTERN = /^[a-z]{2,3}(-[a-z0-9]+)*$/i;

/**
 * Carga una pista de subtítulos en un `<video>` como `<track kind="subtitles">` en modo `hidden`: el navegador
 * la descarga y lleva la cuenta de los cues activos, pero no los dibuja. Quien lo usa los dibuja en su propia
 * capa HTML (encima del video y del lienzo de WebGPU) con lo que llega por `onCues`.
 */
export class SubtitleTrackBinder {
  private element?: HTMLTrackElement;
  private textTrack?: TextTrack;
  private readonly onCueChange = () => this.emit();

  constructor(
    private readonly video: HTMLVideoElement,
    private readonly onCues: (cues: readonly ActiveCue[]) => void,
    private readonly document: Document = video.ownerDocument,
  ) {}

  /** Activa una pista. Reemplaza la que hubiera. */
  attach(track: SubtitleTrack): void {
    this.detach();
    const element = this.document.createElement('track');
    element.kind = 'subtitles';
    element.label = track.label;
    if (track.language && LANGUAGE_PATTERN.test(track.language)) {
      element.srclang = track.language;
    }
    element.src = track.url;
    this.element = element;
    this.video.appendChild(element);

    // `track` solo existe en un navegador de verdad (jsdom no lo implementa).
    const textTrack = element.track as TextTrack | undefined;
    if (!textTrack) {
      return;
    }
    textTrack.mode = 'hidden';
    textTrack.addEventListener('cuechange', this.onCueChange);
    this.textTrack = textTrack;
    this.emit();
  }

  /** Quita la pista activa y vacía los cues. */
  detach(): void {
    this.textTrack?.removeEventListener('cuechange', this.onCueChange);
    if (this.textTrack) {
      this.textTrack.mode = 'disabled';
    }
    this.textTrack = undefined;
    this.element?.remove();
    const hadTrack = this.element !== undefined;
    this.element = undefined;
    if (hadTrack) {
      this.onCues([]);
    }
  }

  private emit(): void {
    const list = this.textTrack?.activeCues;
    const cues: ActiveCue[] = [];
    if (list) {
      for (let i = 0; i < list.length; i++) {
        const cue = list[i];
        if (cue) {
          cues.push(cue as ActiveCue);
        }
      }
    }
    this.onCues(cues);
  }
}
