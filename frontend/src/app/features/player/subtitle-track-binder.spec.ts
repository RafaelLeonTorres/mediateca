import type { SubtitleTrack } from '../../core/playback/subtitles-api';
import { type ActiveCue, SubtitleTrackBinder } from './subtitle-track-binder';

describe('SubtitleTrackBinder', () => {
  const track: SubtitleTrack = {
    id: 'e2',
    label: 'Español',
    language: 'es',
    codec: 'subrip',
    source: 'embedded',
    isDefault: false,
    isForced: false,
    url: '/api/items/v1/subtitles/e2.vtt',
    canDelete: false,
  };

  /** Una pista de texto de mentira: jsdom no implementa `HTMLTrackElement.track`. */
  class FakeTextTrack extends EventTarget {
    mode: TextTrackMode = 'disabled';
    activeCues: ActiveCue[] = [];
    emit(...cues: ActiveCue[]) {
      this.activeCues = cues;
      this.dispatchEvent(new Event('cuechange'));
    }
  }

  const original = Object.getOwnPropertyDescriptor(HTMLTrackElement.prototype, 'track');
  let fake: FakeTextTrack | undefined;

  function provideFakeTrack() {
    fake = new FakeTextTrack();
    Object.defineProperty(HTMLTrackElement.prototype, 'track', { configurable: true, get: () => fake });
  }

  afterEach(() => {
    if (original) {
      Object.defineProperty(HTMLTrackElement.prototype, 'track', original);
    } else {
      delete (HTMLTrackElement.prototype as { track?: unknown }).track;
    }
    fake = undefined;
  });

  function setup() {
    const video = document.createElement('video');
    const onCues = vi.fn<(cues: readonly ActiveCue[]) => void>();
    return { video, onCues, binder: new SubtitleTrackBinder(video, onCues) };
  }

  it('agrega un <track kind="subtitles"> con la dirección del WebVTT, su etiqueta y su idioma', () => {
    const { video, binder } = setup();

    binder.attach(track);

    const element = video.querySelector('track')!;
    expect(element.getAttribute('kind')).toBe('subtitles');
    expect(element.getAttribute('src')).toBe('/api/items/v1/subtitles/e2.vtt');
    expect(element.getAttribute('label')).toBe('Español');
    expect(element.getAttribute('srclang')).toBe('es');
  });

  it('no pone srclang si la pista no dice su idioma', () => {
    const { video, binder } = setup();

    binder.attach({ ...track, language: null });

    expect(video.querySelector('track')!.hasAttribute('srclang')).toBe(false);
  });

  it('la pista queda en modo oculto: el navegador lleva los cues, pero no los dibuja', () => {
    provideFakeTrack();
    const { binder } = setup();

    binder.attach(track);

    expect(fake!.mode).toBe('hidden');
  });

  it('avisa de los cues activos en cada cuechange', () => {
    provideFakeTrack();
    const { binder, onCues } = setup();
    binder.attach(track);
    const cue = { text: 'Hola' } as ActiveCue;

    fake!.emit(cue);
    fake!.emit();

    expect(onCues).toHaveBeenNthCalledWith(2, [cue]);
    expect(onCues).toHaveBeenLastCalledWith([]);
  });

  it('al cambiar de pista quita la anterior', () => {
    const { video, binder } = setup();
    binder.attach(track);

    binder.attach({ ...track, id: 'e3', url: '/api/items/v1/subtitles/e3.vtt' });

    const elements = video.querySelectorAll('track');
    expect(elements).toHaveLength(1);
    expect(elements[0].getAttribute('src')).toBe('/api/items/v1/subtitles/e3.vtt');
  });

  it('al desactivar quita el <track>, vacía los cues y deja de escuchar', () => {
    provideFakeTrack();
    const { video, binder, onCues } = setup();
    binder.attach(track);
    const textTrack = fake!;

    binder.detach();

    expect(video.querySelector('track')).toBeNull();
    expect(textTrack.mode).toBe('disabled');
    expect(onCues).toHaveBeenLastCalledWith([]);
    const calls = onCues.mock.calls.length;
    textTrack.emit({ text: 'tarde' } as ActiveCue);
    expect(onCues.mock.calls.length).toBe(calls);
  });

  it('desactivar sin pista activa no avisa de nada', () => {
    const { binder, onCues } = setup();

    binder.detach();

    expect(onCues).not.toHaveBeenCalled();
  });
});
