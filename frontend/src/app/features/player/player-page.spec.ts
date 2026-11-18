import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, type Routes, provideRouter } from '@angular/router';

import type { PlaybackInfo } from '../../core/playback/playback-api';
import type { SubtitleTrack } from '../../core/playback/subtitles-api';
import {
  AUTO_NEXT_SECONDS,
  PlayerPage,
  RESUME_NOTICE_MS,
  CONTROLS_HIDE_MS,
  SAVE_INTERVAL_MS,
} from './player-page';
import { RESCALE_STORAGE_KEY, SUBTITLE_STORAGE_KEY } from './player-preferences';
import {
  VIDEO_ENHANCER_FACTORY,
  type VideoEnhancer,
  type VideoEnhancerFactory,
  WEBGPU_SUPPORT,
} from './video-enhancer';

@Component({ template: '' })
class Blank {}

/** Rutas a las que navega el reproductor: sin ellas la navegación falla y deja una promesa rechazada. */
const ROUTES: Routes = [
  { path: 'reproducir/:id', component: Blank },
  { path: 'items/:id', component: Blank },
  { path: '', component: Blank },
];

describe('PlayerPage', () => {
  const noContent = { status: 204, statusText: 'No Content' };

  function info(extra: Partial<PlaybackInfo> = {}): PlaybackInfo {
    return {
      id: 'e2',
      kind: 'Episode',
      title: 'Episodio 3',
      seriesTitle: 'Mi Anime',
      parentTitle: 'Lat',
      artist: null,
      parentId: 'lat',
      streamUrl: '/api/items/e2/stream',
      contentType: 'video/mp4',
      isVideo: true,
      durationSeconds: 1200,
      positionSeconds: 0,
      played: false,
      previousId: 'e1',
      nextId: 'e3',
      nextTitle: 'Episodio 4',
      ...extra,
    };
  }

  function subtitle(extra: Partial<SubtitleTrack> = {}): SubtitleTrack {
    return {
      id: 'e2',
      label: 'Español',
      language: 'es',
      codec: 'subrip',
      source: 'embedded',
      isDefault: false,
      isForced: false,
      url: '/api/items/e2/subtitles/e2.vtt',
      canDelete: false,
      ...extra,
    };
  }

  interface RenderOptions {
    playback?: PlaybackInfo;
    tracks?: SubtitleTrack[];
    webgpu?: boolean;
    factory?: VideoEnhancerFactory;
  }

  /** Muestra /reproducir/e2 y responde sus datos antes de esperar (`whenStable` espera a lo pendiente). */
  async function render(playback: PlaybackInfo = info(), options: Omit<RenderOptions, 'playback'> = {}) {
    TestBed.configureTestingModule({
      imports: [PlayerPage],
      providers: [
        provideRouter(ROUTES),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: WEBGPU_SUPPORT, useValue: options.webgpu ?? false },
        ...(options.factory ? [{ provide: VIDEO_ENHANCER_FACTORY, useValue: options.factory }] : []),
      ],
    });
    const fixture = TestBed.createComponent(PlayerPage);
    fixture.componentRef.setInput('id', 'e2');
    fixture.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/items/e2/playback').flush(playback);
    http.expectOne('/api/items/e2/subtitles').flush(options.tracks ?? []);
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const video = element.querySelector<HTMLVideoElement>('video');
    if (video) {
      // En jsdom el tiempo del video no avanza solo: se maneja a mano.
      Object.defineProperty(video, 'currentTime', { value: 0, writable: true, configurable: true });
    }
    return { fixture, http, element, video: video! };
  }

  const buttonWithText = (element: HTMLElement, text: string) =>
    Array.from(element.querySelectorAll<HTMLElement>('a, button')).find((el) =>
      el.textContent?.includes(text),
    );

  const clearCookie = () => {
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  };

  beforeEach(() => localStorage.clear());

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
    vi.unstubAllGlobals();
    clearCookie();
  });

  it('muestra el título con su contexto, el video a todo lo ancho y el botón para volver al detalle', async () => {
    const { element, video } = await render();

    expect(element.querySelector('h1')!.textContent).toContain('Mi Anime · Lat · Episodio 3');
    expect(video.getAttribute('src')).toBe('/api/items/e2/stream');
    // Los controles son propios: la barra nativa quedaría tapada por el lienzo.
    expect(video.controls).toBe(false);
    expect(video.autoplay).toBe(true);
    const back = element.querySelector('a[aria-label="Volver al detalle"]')!;
    expect(back.getAttribute('href')).toBe('/items/e2');
  });

  it('una película solo muestra su título y no tiene anterior ni siguiente', async () => {
    const { element } = await render(
      info({ kind: 'Movie', title: 'Heat', seriesTitle: null, parentTitle: null, previousId: null, nextId: null }),
    );

    expect(element.querySelector('h1')!.textContent?.trim()).toBe('Heat');
    expect(element.querySelector('nav.steps')).toBeNull();
  });

  describe('posición guardada', () => {
    it('retoma donde se quedó y ofrece empezar desde el inicio', async () => {
      const { fixture, element, video } = await render(info({ positionSeconds: 125 }));

      video.dispatchEvent(new Event('loadedmetadata'));
      fixture.detectChanges();

      expect(video.currentTime).toBe(125);
      expect(element.querySelector('[role="status"]')!.textContent).toContain('Continuando desde 2:05');

      buttonWithText(element, 'Empezar desde el inicio')!.click();
      fixture.detectChanges();

      expect(video.currentTime).toBe(0);
      expect(element.querySelector('[role="status"]')).toBeNull();
    });

    it('el aviso se oculta solo', async () => {
      const { fixture, element, video } = await render(info({ positionSeconds: 125 }));
      vi.useFakeTimers();

      video.dispatchEvent(new Event('loadedmetadata'));
      fixture.detectChanges();
      expect(element.querySelector('[role="status"]')).not.toBeNull();

      vi.advanceTimersByTime(RESUME_NOTICE_MS);
      fixture.detectChanges();

      expect(element.querySelector('[role="status"]')).toBeNull();
    });

    it('con unos segundos o sin posición empieza de cero y sin aviso', async () => {
      const { fixture, element, video } = await render(info({ positionSeconds: 2 }));

      video.dispatchEvent(new Event('loadedmetadata'));
      fixture.detectChanges();

      expect(video.currentTime).toBe(0);
      expect(element.querySelector('[role="status"]')).toBeNull();
    });

    it('si ya estaba casi al final no lo retoma en el último segundo', async () => {
      const { video } = await render(info({ positionSeconds: 1198, durationSeconds: 1200 }));

      video.dispatchEvent(new Event('loadedmetadata'));

      expect(video.currentTime).toBe(0);
    });
  });

  describe('guardado del progreso', () => {
    it('guarda cada 10 segundos mientras se reproduce', async () => {
      const { http, video } = await render(info({ positionSeconds: 100 }));
      vi.useFakeTimers();
      video.dispatchEvent(new Event('loadedmetadata'));
      video.dispatchEvent(new Event('play'));

      video.currentTime = 142;
      vi.advanceTimersByTime(SAVE_INTERVAL_MS);

      const request = http.expectOne('/api/items/e2/progress');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ positionSeconds: 142 });
      request.flush(null, noContent);

      video.currentTime = 152;
      vi.advanceTimersByTime(SAVE_INTERVAL_MS);
      expect(http.expectOne('/api/items/e2/progress').request.body).toEqual({ positionSeconds: 152 });
    });

    it('no guarda nada antes de aplicar la posición guardada', async () => {
      const { http, video } = await render(info({ positionSeconds: 100 }));
      vi.useFakeTimers();

      video.dispatchEvent(new Event('play'));
      vi.advanceTimersByTime(SAVE_INTERVAL_MS);
      video.dispatchEvent(new Event('pause'));

      http.expectNone('/api/items/e2/progress');
    });

    it('guarda al pausar y deja de guardar cada 10 segundos', async () => {
      const { http, video } = await render();
      vi.useFakeTimers();
      video.dispatchEvent(new Event('loadedmetadata'));
      video.dispatchEvent(new Event('play'));
      video.currentTime = 33;

      video.dispatchEvent(new Event('pause'));

      http.expectOne('/api/items/e2/progress').flush(null, noContent);
      vi.advanceTimersByTime(SAVE_INTERVAL_MS * 2);
      http.expectNone('/api/items/e2/progress');
    });

    it('al salir de la página guarda con fetch y keepalive, con la cabecera XSRF de la cookie', async () => {
      document.cookie = 'XSRF-TOKEN=tok%3D1; path=/';
      const fetchMock = vi.fn().mockResolvedValue({});
      vi.stubGlobal('fetch', fetchMock);
      const { fixture, video } = await render();
      video.dispatchEvent(new Event('loadedmetadata'));
      video.currentTime = 77;

      fixture.destroy();

      expect(fetchMock).toHaveBeenCalledTimes(1);
      const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
      expect(url).toBe('/api/items/e2/progress');
      expect(init.method).toBe('PUT');
      expect(init.keepalive).toBe(true);
      expect((init.headers as Record<string, string>)['X-XSRF-TOKEN']).toBe('tok=1');
      expect(JSON.parse(init.body as string)).toEqual({ positionSeconds: 77 });
    });

    it('al ocultar la página (pagehide) también guarda con fetch', async () => {
      const fetchMock = vi.fn().mockResolvedValue({});
      vi.stubGlobal('fetch', fetchMock);
      const { video } = await render();
      video.dispatchEvent(new Event('loadedmetadata'));
      video.currentTime = 12;

      window.dispatchEvent(new Event('pagehide'));

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string)).toEqual({
        positionSeconds: 12,
      });
    });

    it('si el archivo no se pudo reproducir no guarda al salir', async () => {
      const fetchMock = vi.fn().mockResolvedValue({});
      vi.stubGlobal('fetch', fetchMock);
      const { fixture, video } = await render();
      video.dispatchEvent(new Event('loadedmetadata'));
      video.dispatchEvent(new Event('error'));

      fixture.destroy();

      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  describe('al terminar', () => {
    it('lo marca como visto y ofrece el siguiente episodio de la misma carpeta', async () => {
      const { fixture, http, element, video } = await render();
      vi.useFakeTimers();
      video.dispatchEvent(new Event('loadedmetadata'));
      video.dispatchEvent(new Event('play'));

      video.dispatchEvent(new Event('ended'));
      fixture.detectChanges();

      const request = http.expectOne('/api/items/e2/played');
      expect(request.request.method).toBe('POST');
      request.flush(null, noContent);
      expect(element.querySelector('#ended-title')!.textContent).toContain('Terminó');
      expect(element.textContent).toContain('Episodio 4');
      expect(buttonWithText(element, 'Siguiente episodio')).toBeDefined();
      expect(element.textContent).toContain(`empieza en ${AUTO_NEXT_SECONDS} s`);

      // Ya está marcado como visto: ni el guardado periódico ni salir lo vuelven a guardar.
      vi.advanceTimersByTime(SAVE_INTERVAL_MS);
      http.expectNone('/api/items/e2/progress');
    });

    it('el botón pasa al siguiente episodio', async () => {
      const { fixture, http, element, video } = await render();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      video.dispatchEvent(new Event('ended'));
      http.expectOne('/api/items/e2/played').flush(null, noContent);
      fixture.detectChanges();

      buttonWithText(element, 'Siguiente episodio')!.click();

      expect(navigate).toHaveBeenCalledWith(['/reproducir', 'e3']);
    });

    it('la cuenta regresiva pasa sola al siguiente y se puede cancelar', async () => {
      const { fixture, http, element, video } = await render();
      vi.useFakeTimers();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      video.dispatchEvent(new Event('ended'));
      http.expectOne('/api/items/e2/played').flush(null, noContent);
      fixture.detectChanges();

      vi.advanceTimersByTime(2000);
      fixture.detectChanges();
      expect(element.textContent).toContain(`empieza en ${AUTO_NEXT_SECONDS - 2} s`);
      expect(navigate).not.toHaveBeenCalled();

      buttonWithText(element, 'Cancelar')!.click();
      fixture.detectChanges();
      vi.advanceTimersByTime(AUTO_NEXT_SECONDS * 1000);
      expect(navigate).not.toHaveBeenCalled();
      expect(element.textContent).not.toContain('empieza en');
    });

    it('sin cancelar, al llegar a cero navega al siguiente', async () => {
      const { http, video } = await render();
      vi.useFakeTimers();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      video.dispatchEvent(new Event('ended'));
      http.expectOne('/api/items/e2/played').flush(null, noContent);

      vi.advanceTimersByTime(AUTO_NEXT_SECONDS * 1000);

      expect(navigate).toHaveBeenCalledWith(['/reproducir', 'e3']);
    });

    it('el último episodio de la carpeta no ofrece siguiente', async () => {
      const { fixture, http, element, video } = await render(info({ nextId: null, nextTitle: null }));
      video.dispatchEvent(new Event('ended'));
      http.expectOne('/api/items/e2/played').flush(null, noContent);
      fixture.detectChanges();

      expect(buttonWithText(element, 'Siguiente episodio')).toBeUndefined();
      expect(buttonWithText(element, 'Volver a ver')).toBeDefined();
      expect(element.textContent).not.toContain('empieza en');
    });
  });

  describe('anterior y siguiente', () => {
    it('guardan el progreso y navegan al episodio de la misma carpeta', async () => {
      const fetchMock = vi.fn().mockResolvedValue({});
      vi.stubGlobal('fetch', fetchMock);
      const { element, video } = await render();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      video.dispatchEvent(new Event('loadedmetadata'));
      video.currentTime = 300;

      buttonWithText(element, 'Anterior')!.click();
      expect(navigate).toHaveBeenLastCalledWith(['/reproducir', 'e1']);
      expect(fetchMock).toHaveBeenCalledTimes(1);

      buttonWithText(element, 'Siguiente')!.click();
      expect(navigate).toHaveBeenLastCalledWith(['/reproducir', 'e3']);
    });

    it('se desactivan en los extremos de la carpeta', async () => {
      const { element } = await render(info({ previousId: null }));

      expect((buttonWithText(element, 'Anterior') as HTMLButtonElement).disabled).toBe(true);
      expect((buttonWithText(element, 'Siguiente') as HTMLButtonElement).disabled).toBe(false);
    });
  });

  describe('archivo que el navegador no puede reproducir', () => {
    it('lo explica y ofrece descargarlo', async () => {
      const { fixture, element, video } = await render(info({ contentType: 'video/x-matroska' }));

      video.dispatchEvent(new Event('error'));
      fixture.detectChanges();

      const alert = element.querySelector('[role="alert"]')!;
      expect(alert.textContent).toContain('no puede reproducir este archivo');
      expect(alert.textContent).toContain('video/x-matroska');
      const download = alert.querySelector<HTMLAnchorElement>('a[download]')!;
      expect(download.getAttribute('href')).toBe('/api/items/e2/stream');
      expect(download.textContent).toContain('Descargar archivo');
    });
  });

  describe('atajos de teclado', () => {
    it('las flechas saltan 10 segundos y no pasan de cero', async () => {
      const { video } = await render();
      video.currentTime = 100;

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
      expect(video.currentTime).toBe(110);

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
      expect(video.currentTime).toBe(90);

      video.currentTime = 4;
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
      expect(video.currentTime).toBe(0);
    });

    it('F pide la pantalla completa del reproductor', async () => {
      const { element } = await render();
      const stage = element.querySelector<HTMLElement>('.stage')!;
      const requestFullscreen = vi.fn().mockResolvedValue(undefined);
      stage.requestFullscreen = requestFullscreen;

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'f' }));

      expect(requestFullscreen).toHaveBeenCalledTimes(1);
    });

    it('no hace nada si se escribe en un campo o con Ctrl', async () => {
      const { element, video } = await render();
      video.currentTime = 100;
      const input = document.createElement('input');
      element.appendChild(input);

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', ctrlKey: true }));

      expect(video.currentTime).toBe(100);
    });
  });

  describe('lo que no es un video', () => {
    it('una pista de música manda a la barra de música', async () => {
      const { element, video } = await render(info({ kind: 'Track', isVideo: false, contentType: 'audio/flac' }));

      expect(video).toBeNull();
      expect(element.textContent).toContain('Las pistas se reproducen desde la barra de música');
    });

    it('si no se encuentra o no hay acceso lo dice y ofrece volver al inicio', async () => {
      TestBed.configureTestingModule({
        imports: [PlayerPage],
        providers: [provideRouter(ROUTES), provideHttpClient(), provideHttpClientTesting()],
      });
      const fixture = TestBed.createComponent(PlayerPage);
      fixture.componentRef.setInput('id', 'e2');
      fixture.detectChanges();
      TestBed.tick();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/items/e2/playback').flush(null, { status: 404, statusText: 'No encontrado' });
      http.expectOne('/api/items/e2/subtitles').flush([]);
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;

      expect(element.querySelector('[role="alert"]')!.textContent).toContain('No se pudo cargar el video');
      expect(element.querySelector('video')).toBeNull();
      expect(element.querySelector('a[href="/"]')).not.toBeNull();
    });
  });

  /** Deja que corran los efectos (pistas elegidas, enhancer) y se pinte lo que cambió. */
  function settle(fixture: { detectChanges(): void }) {
    fixture.detectChanges();
    TestBed.tick();
    fixture.detectChanges();
  }

  const query = <T extends Element>(element: HTMLElement, selector: string) =>
    element.querySelector<T>(selector)!;

  describe('barra de controles', () => {
    it('el botón reproduce y pausa el video', async () => {
      const { element, video } = await render();
      video.play = vi.fn().mockResolvedValue(undefined);
      video.pause = vi.fn();

      query<HTMLButtonElement>(element, 'button[aria-label="Reproducir"]').click();
      expect(video.play).toHaveBeenCalledTimes(1);
    });

    it('espacio y K alternan la reproducción, pero espacio sobre un botón lo deja al navegador', async () => {
      const { element, video } = await render();
      video.play = vi.fn().mockResolvedValue(undefined);

      window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }));
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k' }));
      expect(video.play).toHaveBeenCalledTimes(2);

      query<HTMLButtonElement>(element, 'button[aria-label="Reproducir"]').dispatchEvent(
        new KeyboardEvent('keydown', { key: ' ', bubbles: true }),
      );
      expect(video.play).toHaveBeenCalledTimes(2);
    });

    it('M silencia y vuelve a activar el sonido', async () => {
      const { video } = await render();

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'm' }));
      expect(video.muted).toBe(true);

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'M' }));
      expect(video.muted).toBe(false);
    });

    it('se oculta a los 3 segundos mientras se reproduce y vuelve al mover el ratón', async () => {
      const { fixture, element, video } = await render();
      vi.useFakeTimers();
      video.dispatchEvent(new Event('play'));
      fixture.detectChanges();
      expect(query(element, '.bar').classList).not.toContain('hidden');

      vi.advanceTimersByTime(CONTROLS_HIDE_MS);
      fixture.detectChanges();
      expect(query(element, '.bar').classList).toContain('hidden');
      expect(query(element, '.stage').classList).toContain('idle');

      query(element, '.stage').dispatchEvent(new Event('pointermove'));
      fixture.detectChanges();
      expect(query(element, '.bar').classList).not.toContain('hidden');
    });

    it('en pausa la barra se queda a la vista', async () => {
      const { fixture, element, video } = await render();
      vi.useFakeTimers();
      video.dispatchEvent(new Event('pause'));

      vi.advanceTimersByTime(CONTROLS_HIDE_MS * 3);
      fixture.detectChanges();

      expect(query(element, '.bar').classList).not.toContain('hidden');
    });

    it('con un menú abierto la barra no se oculta', async () => {
      const { fixture, element, video } = await render(info(), { tracks: [subtitle()] });
      vi.useFakeTimers();
      video.dispatchEvent(new Event('play'));
      query<HTMLButtonElement>(element, 'button[aria-haspopup="menu"]').click();
      fixture.detectChanges();

      vi.advanceTimersByTime(CONTROLS_HIDE_MS * 2);
      fixture.detectChanges();

      expect(query(element, '.bar').classList).not.toContain('hidden');
    });

    it('al terminar el video no muestra la barra encima de la pantalla final', async () => {
      const { fixture, http, element, video } = await render();
      video.dispatchEvent(new Event('ended'));
      http.expectOne('/api/items/e2/played').flush(null, noContent);
      fixture.detectChanges();

      expect(query(element, '.bar').classList).toContain('hidden');
    });
  });

  describe('subtítulos', () => {
    const spanish = subtitle({ id: 'e2', label: 'Español', language: 'es' });
    const english = subtitle({
      id: 'x0',
      label: 'Inglés',
      language: 'en',
      source: 'external',
      url: '/api/items/e2/subtitles/x0.vtt',
    });

    const uploaded = subtitle({
      id: 'u1',
      label: 'Inglés (subido)',
      language: 'en',
      source: 'uploaded',
      canDelete: true,
      url: '/api/items/e2/subtitles/u1.vtt',
    });
    const ccLabel = (element: HTMLElement) =>
      query(element, 'app-subtitle-menu button').getAttribute('aria-label');

    /** La respuesta llega, corren los efectos (pista elegida, <track>) y se pinta. */
    async function afterResponse(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
      await new Promise<void>((resolve) => setTimeout(resolve));
      TestBed.tick();
      await fixture.whenStable();
      settle(fixture);
    }

    it('sin pistas el botón CC sigue a la vista y la tecla C no hace nada', async () => {
      const { fixture, element } = await render();
      settle(fixture);

      expect(ccLabel(element)).toBe('Subtítulos: desactivados');
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'c' }));
      settle(fixture);
      expect(element.querySelector('video track')).toBeNull();
    });

    it('al subir un archivo vuelve a pedir la lista y activa la pista nueva', async () => {
      const { fixture, http, element, video } = await render(info(), { tracks: [spanish] });
      settle(fixture);
      query<HTMLButtonElement>(element, 'app-subtitle-menu button').click();
      fixture.detectChanges();
      const input = query<HTMLInputElement>(element, 'app-subtitle-menu input[type="file"]');
      const file = new File(['1\n00:00:01,000 --> 00:00:02,000\nHola\n'], 'Mi episodio.en.srt');
      Object.defineProperty(input, 'files', { value: [file], configurable: true });
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      query(element, 'app-subtitle-menu form').dispatchEvent(new Event('submit', { cancelable: true }));
      const upload = http.expectOne(
        (request) => request.method === 'POST' && request.url === '/api/items/e2/subtitles',
      );
      expect(upload.request.params.get('language')).toBe('en');
      upload.flush(uploaded);
      TestBed.tick();
      http.expectOne('/api/items/e2/subtitles').flush([spanish, uploaded]);
      await afterResponse(fixture);

      expect(ccLabel(element)).toBe('Subtítulos: Inglés (subido)');
      expect(video.querySelector('track')!.getAttribute('src')).toBe('/api/items/e2/subtitles/u1.vtt');
      expect(localStorage.getItem(SUBTITLE_STORAGE_KEY)).toBe('en');
    });

    it('al borrar la pista subida que estaba activa se apagan, sin cambiar el idioma recordado', async () => {
      localStorage.setItem(SUBTITLE_STORAGE_KEY, 'en');
      const { fixture, http, element, video } = await render(info(), { tracks: [spanish, uploaded] });
      settle(fixture);
      expect(ccLabel(element)).toBe('Subtítulos: Inglés (subido)');

      query<HTMLButtonElement>(element, 'app-subtitle-menu button').click();
      fixture.detectChanges();
      buttonWithText(element, 'Borrar el archivo subido')!.click();
      const request = http.expectOne('/api/items/e2/subtitles/u1');
      expect(request.request.method).toBe('DELETE');
      request.flush(null, noContent);
      TestBed.tick();
      http.expectOne('/api/items/e2/subtitles').flush([spanish]);
      await afterResponse(fixture);

      expect(ccLabel(element)).toBe('Subtítulos: desactivados');
      expect(video.querySelector('track')).toBeNull();
      expect(localStorage.getItem(SUBTITLE_STORAGE_KEY)).toBe('en');
    });

    it('por omisión están desactivados aunque haya pistas', async () => {
      const { fixture, element, video } = await render(info(), { tracks: [spanish, english] });
      settle(fixture);

      expect(query(element, 'app-subtitle-menu button').getAttribute('aria-label')).toBe(
        'Subtítulos: desactivados',
      );
      expect(video.querySelector('track')).toBeNull();
    });

    it('una pista forzada o predeterminada se activa sola', async () => {
      const { fixture, element, video } = await render(info(), {
        tracks: [spanish, { ...english, isDefault: true }],
      });
      settle(fixture);

      expect(query(element, 'app-subtitle-menu button').getAttribute('aria-label')).toBe('Subtítulos: Inglés');
      const track = video.querySelector('track')!;
      expect(track.getAttribute('kind')).toBe('subtitles');
      expect(track.getAttribute('src')).toBe('/api/items/e2/subtitles/x0.vtt');
    });

    it('elegir una pista la carga y recuerda su idioma; Desactivados lo recuerda como apagado', async () => {
      const { fixture, element, video } = await render(info(), { tracks: [spanish, english] });
      settle(fixture);

      query<HTMLButtonElement>(element, 'app-subtitle-menu button').click();
      fixture.detectChanges();
      const items = Array.from(element.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]'));
      // El ícono de la opción marcada también aporta texto ("check"): por eso se compara con `includes`.
      expect(items).toHaveLength(3);
      ['Desactivados', 'Español', 'Inglés'].forEach((label, index) =>
        expect(items[index].textContent).toContain(label),
      );

      items[2].click();
      settle(fixture);
      expect(localStorage.getItem(SUBTITLE_STORAGE_KEY)).toBe('en');
      expect(video.querySelector('track')!.getAttribute('src')).toBe('/api/items/e2/subtitles/x0.vtt');

      query<HTMLButtonElement>(element, 'app-subtitle-menu button').click();
      fixture.detectChanges();
      element.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')[0].click();
      settle(fixture);
      expect(localStorage.getItem(SUBTITLE_STORAGE_KEY)).toBe('off');
      expect(video.querySelector('track')).toBeNull();
    });

    it('con un idioma recordado se activa la pista de ese idioma', async () => {
      localStorage.setItem(SUBTITLE_STORAGE_KEY, 'en');
      const { fixture, element } = await render(info(), { tracks: [spanish, english] });
      settle(fixture);

      expect(query(element, 'app-subtitle-menu button').getAttribute('aria-label')).toBe('Subtítulos: Inglés');
    });

    it('si el usuario los había desactivado, no se activa ni la pista predeterminada', async () => {
      localStorage.setItem(SUBTITLE_STORAGE_KEY, 'off');
      const { fixture, element } = await render(info(), { tracks: [{ ...spanish, isDefault: true }] });
      settle(fixture);

      expect(query(element, 'app-subtitle-menu button').getAttribute('aria-label')).toBe(
        'Subtítulos: desactivados',
      );
    });

    it('la tecla C los apaga y vuelve a poner la misma pista', async () => {
      localStorage.setItem(SUBTITLE_STORAGE_KEY, 'en');
      const { fixture, element } = await render(info(), { tracks: [spanish, english] });
      settle(fixture);
      const label = () => query(element, 'app-subtitle-menu button').getAttribute('aria-label');
      expect(label()).toBe('Subtítulos: Inglés');

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'c' }));
      settle(fixture);
      expect(label()).toBe('Subtítulos: desactivados');
      expect(localStorage.getItem(SUBTITLE_STORAGE_KEY)).toBe('off');

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'c' }));
      settle(fixture);
      expect(label()).toBe('Subtítulos: Inglés');
      expect(localStorage.getItem(SUBTITLE_STORAGE_KEY)).toBe('en');
    });
  });

  describe('reescalado con la GPU', () => {
    function enhancerDouble() {
      let events: { onError?: (message: string) => void } | undefined;
      const enhancer: VideoEnhancer = {
        gpuInfo: {
          vendor: 'nvidia',
          architecture: 'ampere',
          device: '',
          description: '',
          isFallback: false,
          timestampQuery: false,
          maxTextureSize: 16384,
        },
        setParams: vi.fn(),
        resize: vi.fn(),
        requestRender: vi.fn(),
        start: vi.fn(),
        stop: vi.fn(),
        destroy: vi.fn(),
      };
      const factory = vi.fn(async (_canvas: unknown, _video: unknown, given: unknown) => {
        events = given as { onError?: (message: string) => void };
        return enhancer;
      });
      return { enhancer, factory, events: () => events! };
    }

    const choose = (fixture: { detectChanges(): void }, element: HTMLElement, label: string) => {
      if (!element.querySelector('fieldset')) {
        query<HTMLButtonElement>(element, 'button[aria-haspopup="dialog"]').click();
        fixture.detectChanges();
      }
      const radio = Array.from(element.querySelectorAll<HTMLLabelElement>('fieldset label')).find((item) =>
        item.textContent?.includes(label),
      )!;
      radio.querySelector<HTMLInputElement>('input')!.click();
      settle(fixture);
    };

    it('sin WebGPU el botón está deshabilitado y explica por qué', async () => {
      const { element } = await render(info(), { webgpu: false });

      const button = query<HTMLButtonElement>(element, 'button[aria-haspopup="dialog"]');
      expect(button.disabled).toBe(true);
      expect(element.textContent).toContain('Tu navegador no tiene WebGPU; usa Chrome o Edge actualizados');
    });

    it('empieza en Original y no crea el enhancer hasta que se pide Mejorado', async () => {
      const double = enhancerDouble();
      const { fixture, element } = await render(info(), { webgpu: true, factory: double.factory });
      settle(fixture);
      expect(double.factory).not.toHaveBeenCalled();
      expect(query(element, 'canvas').classList).not.toContain('visible');

      choose(fixture, element, 'Mejorado');
      await vi.waitFor(() => expect(double.enhancer.start).toHaveBeenCalled());

      expect(double.factory).toHaveBeenCalledTimes(1);
      expect(JSON.parse(localStorage.getItem(RESCALE_STORAGE_KEY)!).view).toBe('enhanced');
    });

    it('volver a Original detiene el enhancer sin crear otro al volver a Mejorado', async () => {
      const double = enhancerDouble();
      const { fixture, element } = await render(info(), { webgpu: true, factory: double.factory });
      choose(fixture, element, 'Mejorado');
      await vi.waitFor(() => expect(double.enhancer.start).toHaveBeenCalledTimes(1));

      choose(fixture, element, 'Original');
      expect(double.enhancer.stop).toHaveBeenCalled();

      choose(fixture, element, 'Comparar');
      await vi.waitFor(() => expect(double.enhancer.start).toHaveBeenCalledTimes(2));
      expect(double.factory).toHaveBeenCalledTimes(1);
    });

    it('recuerda la última vista y la usa al abrir otro video', async () => {
      localStorage.setItem(RESCALE_STORAGE_KEY, JSON.stringify({ view: 'enhanced', kernel: 'bicubic' }));
      const double = enhancerDouble();
      const { fixture } = await render(info(), { webgpu: true, factory: double.factory });
      settle(fixture);

      await vi.waitFor(() => expect(double.enhancer.start).toHaveBeenCalled());
      expect(double.enhancer.setParams).toHaveBeenCalledWith(expect.objectContaining({ kernel: 'bicubic', split: 0 }));
    });

    it('si la GPU falla vuelve a Original y avisa', async () => {
      const double = enhancerDouble();
      const { fixture, element } = await render(info(), { webgpu: true, factory: double.factory });
      choose(fixture, element, 'Mejorado');
      await vi.waitFor(() => expect(double.enhancer.start).toHaveBeenCalled());

      double.events().onError?.('Se perdió el dispositivo.');
      settle(fixture);

      expect(element.querySelector('.notice.alert')!.textContent).toContain('Se perdió el dispositivo.');
      expect(JSON.parse(localStorage.getItem(RESCALE_STORAGE_KEY)!).view).toBe('original');
      expect(double.enhancer.stop).toHaveBeenCalled();
    });

    it('si no se puede crear el enhancer vuelve a Original y avisa', async () => {
      const factory = vi.fn().mockRejectedValue(new Error('No hay adaptador de GPU.'));
      const { fixture, element } = await render(info(), { webgpu: true, factory });
      choose(fixture, element, 'Mejorado');

      await vi.waitFor(() => {
        settle(fixture);
        expect(element.querySelector('.notice.alert')).not.toBeNull();
      });
      expect(element.querySelector('.notice.alert')!.textContent).toContain('No hay adaptador de GPU.');
      expect(JSON.parse(localStorage.getItem(RESCALE_STORAGE_KEY)!).view).toBe('original');
    });
  });
});
