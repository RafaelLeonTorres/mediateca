import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, type Routes, provideRouter } from '@angular/router';

import type { PlaybackInfo } from '../../core/playback/playback-api';
import {
  AUTO_NEXT_SECONDS,
  PlayerPage,
  RESUME_NOTICE_MS,
  SAVE_INTERVAL_MS,
} from './player-page';

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

  /** Muestra /reproducir/e2 y responde sus datos antes de esperar (`whenStable` espera a lo pendiente). */
  async function render(playback: PlaybackInfo = info()) {
    TestBed.configureTestingModule({
      imports: [PlayerPage],
      providers: [provideRouter(ROUTES), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(PlayerPage);
    fixture.componentRef.setInput('id', 'e2');
    fixture.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/items/e2/playback').flush(playback);
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

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    clearCookie();
  });

  it('muestra el título con su contexto, el video a todo lo ancho y el botón para volver al detalle', async () => {
    const { element, video } = await render();

    expect(element.querySelector('h1')!.textContent).toContain('Mi Anime · Lat · Episodio 3');
    expect(video.getAttribute('src')).toBe('/api/items/e2/stream');
    expect(video.controls).toBe(true);
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
      TestBed.inject(HttpTestingController)
        .expectOne('/api/items/e2/playback')
        .flush(null, { status: 404, statusText: 'No encontrado' });
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;

      expect(element.querySelector('[role="alert"]')!.textContent).toContain('No se pudo cargar el video');
      expect(element.querySelector('video')).toBeNull();
      expect(element.querySelector('a[href="/"]')).not.toBeNull();
    });
  });
});
