import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  PLAYER_AUDIO,
  PlayerService,
  type QueueTrack,
} from '../../core/playback/player-service';
import { MusicBar } from './music-bar';

describe('MusicBar', () => {
  const tracks: QueueTrack[] = [
    { id: 't1', title: 'So What', artist: 'Miles Davis', album: 'Kind of Blue', durationSeconds: 545, imageId: 'a1' },
    { id: 't2', title: 'Blue in Green', artist: null, album: null, durationSeconds: 337, imageId: null },
  ];

  let audio: HTMLAudioElement;

  function render() {
    audio = document.createElement('audio');
    audio.play = vi.fn().mockResolvedValue(undefined);
    audio.pause = vi.fn();
    TestBed.configureTestingModule({
      imports: [MusicBar],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PLAYER_AUDIO, useValue: audio },
      ],
    });
    const player = TestBed.inject(PlayerService);
    const fixture = TestBed.createComponent(MusicBar);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const button = (label: string) => element.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
    return { fixture, element, player, button };
  }

  it('sin cola no muestra nada', () => {
    const { element } = render();

    expect(element.querySelector('section')).toBeNull();
  });

  it('muestra la pista, el artista y el álbum, y la carátula', () => {
    const { fixture, element, player } = render();

    player.play(tracks);
    fixture.detectChanges();

    expect(element.querySelector('section')!.getAttribute('aria-label')).toBe('Reproductor de música');
    expect(element.querySelector('.title')!.textContent).toContain('So What');
    expect(element.querySelector('.artist')!.textContent).toContain('Miles Davis · Kind of Blue');
    expect(element.querySelector('img')!.getAttribute('src')).toBe('/api/items/a1/image');
  });

  it('sin carátula, o si no carga, muestra el ícono de música', () => {
    const { fixture, element, player } = render();
    player.play(tracks, 1);
    fixture.detectChanges();
    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('.cover mat-icon')!.textContent).toContain('music_note');

    player.playAt(0);
    fixture.detectChanges();
    element.querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('.cover mat-icon')).not.toBeNull();
  });

  it('el botón central cambia entre reproducir y pausar según el estado', () => {
    const { fixture, player, button } = render();
    player.play(tracks);
    fixture.detectChanges();
    expect(button('Reproducir')).not.toBeNull();

    audio.dispatchEvent(new Event('play'));
    fixture.detectChanges();
    expect(button('Pausar')).not.toBeNull();

    button('Pausar').click();
    expect(audio.pause).toHaveBeenCalled();
  });

  it('pasa a la siguiente pista y regresa a la anterior', () => {
    const { fixture, element, player, button } = render();
    player.play(tracks);
    fixture.detectChanges();
    expect(button('Pista anterior').disabled).toBe(true);
    expect(button('Pista siguiente').disabled).toBe(false);

    button('Pista siguiente').click();
    fixture.detectChanges();

    expect(player.current()?.id).toBe('t2');
    expect(element.querySelector('.title')!.textContent).toContain('Blue in Green');
    expect(button('Pista siguiente').disabled).toBe(true);
    expect(button('Pista anterior').disabled).toBe(false);

    button('Pista anterior').click();
    fixture.detectChanges();
    expect(player.current()?.id).toBe('t1');
  });

  it('la barra de avance muestra el tiempo y mueve la pista', () => {
    const { fixture, element, player } = render();
    player.play(tracks);
    audio.currentTime = 65;
    audio.dispatchEvent(new Event('timeupdate'));
    fixture.detectChanges();

    const range = element.querySelector<HTMLInputElement>('input[type="range"]')!;
    expect(range.getAttribute('aria-label')).toBe('Avance de la pista');
    expect(range.max).toBe('545');
    expect(range.value).toBe('65');
    const times = Array.from(element.querySelectorAll('.time')).map((t) => t.textContent?.trim());
    expect(times).toEqual(['1:05', '9:05']);

    range.value = '200';
    range.dispatchEvent(new Event('input'));

    expect(audio.currentTime).toBe(200);
    expect(player.position()).toBe(200);
  });

  it('muestra el aviso cuando la pista no se puede reproducir', () => {
    const { fixture, element, player } = render();
    player.play(tracks);

    audio.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')!.textContent).toContain('No se pudo reproducir «So What»');
  });

  it('se cierra con el botón y vacía la cola', () => {
    const { fixture, element, player, button } = render();
    player.play(tracks);
    fixture.detectChanges();

    button('Cerrar el reproductor').click();
    fixture.detectChanges();

    expect(player.queue()).toEqual([]);
    expect(element.querySelector('section')).toBeNull();
  });
});
