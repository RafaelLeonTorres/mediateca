import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PLAYER_AUDIO, PlayerService, type QueueTrack } from './player-service';

describe('PlayerService', () => {
  let audio: HTMLAudioElement;
  let player: PlayerService;
  let http: HttpTestingController;

  const tracks: QueueTrack[] = [
    { id: 't1', title: 'So What', artist: 'Miles Davis', album: 'Kind of Blue', durationSeconds: 545, imageId: 'a1' },
    { id: 't2', title: 'Freddie Freeloader', artist: 'Miles Davis', album: 'Kind of Blue', durationSeconds: 590, imageId: 'a1' },
    { id: 't3', title: 'Blue in Green', artist: null, album: 'Kind of Blue', durationSeconds: null, imageId: null },
  ];

  const fire = (type: string) => audio.dispatchEvent(new Event(type));

  beforeEach(() => {
    // En jsdom play() y pause() no hacen nada: un elemento real con esos dos métodos espiados.
    audio = document.createElement('audio');
    audio.play = vi.fn().mockResolvedValue(undefined);
    audio.pause = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PLAYER_AUDIO, useValue: audio },
      ],
    });
    player = TestBed.inject(PlayerService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('empieza sin cola ni pista actual', () => {
    expect(player.queue()).toEqual([]);
    expect(player.current()).toBeNull();
    expect(player.playing()).toBe(false);
    expect(player.hasNext()).toBe(false);
    expect(player.hasPrevious()).toBe(false);
  });

  it('al reproducir carga la cola y empieza por la pista indicada', () => {
    player.play(tracks, 1);

    expect(player.queue()).toHaveLength(3);
    expect(player.index()).toBe(1);
    expect(player.current()?.id).toBe('t2');
    expect(audio.getAttribute('src')).toBe('/api/items/t2/stream');
    expect(audio.play).toHaveBeenCalledTimes(1);
    expect(player.duration()).toBe(590);
    expect(player.hasPrevious()).toBe(true);
    expect(player.hasNext()).toBe(true);
  });

  it('un índice fuera de la cola se ajusta y una cola vacía no hace nada', () => {
    player.play(tracks, 99);
    expect(player.index()).toBe(2);

    player.play([]);
    expect(player.queue()).toHaveLength(3);
  });

  it('"sonando" sigue los eventos del audio', () => {
    player.play(tracks);

    fire('play');
    expect(player.playing()).toBe(true);

    fire('pause');
    expect(player.playing()).toBe(false);
  });

  it('alterna entre pausa y reproducción', () => {
    player.play(tracks);
    fire('play');

    player.toggle();
    expect(audio.pause).toHaveBeenCalledTimes(1);

    fire('pause');
    (audio.play as ReturnType<typeof vi.fn>).mockClear();
    player.toggle();
    expect(audio.play).toHaveBeenCalledTimes(1);
  });

  it('pause() solo pausa si está sonando', () => {
    player.play(tracks);

    player.pause();
    expect(audio.pause).not.toHaveBeenCalled();

    fire('play');
    player.pause();
    expect(audio.pause).toHaveBeenCalledTimes(1);
  });

  it('pasa a la siguiente pista y regresa a la anterior', () => {
    player.play(tracks);

    player.next();
    expect(player.current()?.id).toBe('t2');
    expect(audio.getAttribute('src')).toBe('/api/items/t2/stream');

    player.previous();
    expect(player.current()?.id).toBe('t1');
  });

  it('no pasa de los extremos de la cola', () => {
    player.play(tracks, 2);
    player.next();
    expect(player.index()).toBe(2);

    player.playAt(0);
    player.previous();
    expect(player.index()).toBe(0);
  });

  it('"anterior" reinicia la pista si ya lleva unos segundos', () => {
    player.play(tracks, 1);
    audio.currentTime = 30;
    fire('timeupdate');

    player.previous();

    expect(player.index()).toBe(1);
    expect(audio.currentTime).toBe(0);
    expect(player.position()).toBe(0);
  });

  it('al terminar una pista la marca como vista y sigue con la siguiente', () => {
    player.play(tracks);

    fire('ended');

    const request = http.expectOne('/api/items/t1/played');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(player.current()?.id).toBe('t2');
    expect(audio.getAttribute('src')).toBe('/api/items/t2/stream');
  });

  it('al terminar la última pista la marca como vista y se detiene', () => {
    player.play(tracks, 2);
    fire('play');

    fire('ended');

    http.expectOne('/api/items/t3/played').flush(null, { status: 204, statusText: 'No Content' });
    expect(player.index()).toBe(2);
    expect(player.playing()).toBe(false);
  });

  it('si no se puede marcar como vista, la música sigue', () => {
    player.play(tracks);

    fire('ended');

    http.expectOne('/api/items/t1/played').flush(null, { status: 500, statusText: 'Error' });
    expect(player.current()?.id).toBe('t2');
  });

  it('mueve la posición sin salirse de la pista', () => {
    player.play(tracks);

    player.seek(100);
    expect(audio.currentTime).toBe(100);
    expect(player.position()).toBe(100);

    player.seek(-5);
    expect(player.position()).toBe(0);

    player.seek(99999);
    expect(player.position()).toBe(545);
  });

  it('toma la duración real del archivo cuando la conoce', () => {
    player.play(tracks, 2);
    expect(player.duration()).toBe(0);

    Object.defineProperty(audio, 'duration', { value: 241, configurable: true });
    fire('loadedmetadata');

    expect(player.duration()).toBe(241);
  });

  it('si la pista no se puede reproducir lo dice y no avanza sola', () => {
    player.play(tracks);

    fire('error');

    expect(player.error()).toContain('No se pudo reproducir «So What»');
    expect(player.current()?.id).toBe('t1');
    expect(player.playing()).toBe(false);

    player.next();
    expect(player.error()).toBeNull();
  });

  it('si el navegador no deja empezar, no queda "sonando"', async () => {
    (audio.play as ReturnType<typeof vi.fn>).mockRejectedValue(new DOMException('', 'NotAllowedError'));
    player.play(tracks);
    fire('play');

    await Promise.resolve();
    await Promise.resolve();

    expect(player.playing()).toBe(false);
  });

  it('detener vacía la cola y quita la pista actual', () => {
    player.play(tracks);
    fire('play');

    player.stop();

    expect(audio.pause).toHaveBeenCalled();
    expect(audio.hasAttribute('src')).toBe(false);
    expect(player.queue()).toEqual([]);
    expect(player.current()).toBeNull();
    expect(player.playing()).toBe(false);
    // Quitar el src dispara un "error" del navegador: no es un fallo de la pista.
    fire('error');
    expect(player.error()).toBeNull();
  });
});
