import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PlaybackApi, readCookie, streamUrl } from './playback-api';

describe('PlaybackApi', () => {
  let api: PlaybackApi;
  let http: HttpTestingController;

  const clearCookie = () => {
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  };

  beforeEach(() => {
    clearCookie();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PlaybackApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
    clearCookie();
  });

  it('arma la dirección del stream', () => {
    expect(streamUrl('abc')).toBe('/api/items/abc/stream');
  });

  it('pide los datos de reproducción de un elemento', async () => {
    const resource = TestBed.runInInjectionContext(() => api.info(() => 'e1'));
    TestBed.tick();

    http.expectOne('/api/items/e1/playback').flush({ id: 'e1', title: 'Episodio 1' });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()?.title).toBe('Episodio 1');
  });

  it('guarda el progreso con un PUT y la posición en el cuerpo', async () => {
    const saved = api.saveProgress('e1', 42.5);

    const request = http.expectOne('/api/items/e1/progress');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ positionSeconds: 42.5 });
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(saved).resolves.toBeUndefined();
  });

  it('marca como visto con POST y como no visto con DELETE', async () => {
    const played = api.markPlayed('e1');
    const post = http.expectOne('/api/items/e1/played');
    expect(post.request.method).toBe('POST');
    post.flush(null, { status: 204, statusText: 'No Content' });
    await played;

    const unplayed = api.markUnplayed('e1');
    const del = http.expectOne('/api/items/e1/played');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    await unplayed;
  });

  it('un error del servidor rechaza la promesa', async () => {
    const saved = api.saveProgress('e1', 10);

    http.expectOne('/api/items/e1/progress').flush(null, { status: 404, statusText: 'No encontrado' });

    await expect(saved).rejects.toBeDefined();
  });

  describe('al salir de la página', () => {
    it('manda un fetch con keepalive y la cabecera XSRF leída de la cookie', () => {
      document.cookie = 'XSRF-TOKEN=abc%3D123; path=/';
      const fetchMock = vi.fn().mockResolvedValue({});
      vi.stubGlobal('fetch', fetchMock);

      api.saveProgressOnExit('e1', 99);

      expect(fetchMock).toHaveBeenCalledTimes(1);
      const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
      expect(url).toBe('/api/items/e1/progress');
      expect(init.method).toBe('PUT');
      expect(init.keepalive).toBe(true);
      expect(init.credentials).toBe('same-origin');
      expect((init.headers as Record<string, string>)['X-XSRF-TOKEN']).toBe('abc=123');
      expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
      expect(JSON.parse(init.body as string)).toEqual({ positionSeconds: 99 });
    });

    it('sin cookie no manda la cabecera', () => {
      const fetchMock = vi.fn().mockResolvedValue({});
      vi.stubGlobal('fetch', fetchMock);

      api.saveProgressOnExit('e1', 5);

      const init = fetchMock.mock.calls[0][1] as RequestInit;
      expect(init.headers as Record<string, string>).not.toHaveProperty('X-XSRF-TOKEN');
    });

    it('si el fetch falla no lanza nada', async () => {
      vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('sin red')));

      expect(() => api.saveProgressOnExit('e1', 5)).not.toThrow();
      await Promise.resolve();
    });
  });

  describe('readCookie', () => {
    it('lee una cookie entre varias y la decodifica', () => {
      const fake = { cookie: 'a=1; XSRF-TOKEN=x%2By%3D; b=2' } as Document;

      expect(readCookie(fake, 'XSRF-TOKEN')).toBe('x+y=');
      expect(readCookie(fake, 'a')).toBe('1');
    });

    it('devuelve null si no existe', () => {
      expect(readCookie({ cookie: 'a=1' } as Document, 'XSRF-TOKEN')).toBeNull();
      expect(readCookie({ cookie: '' } as Document, 'XSRF-TOKEN')).toBeNull();
    });
  });
});
