import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { ChildItem, ItemDetail, ItemKind, ResumeItem } from '../../core/items/items-api';
import { SessionStore } from '../../core/auth/session-store';
import { PlayerService, type QueueTrack } from '../../core/playback/player-service';
import { ItemPage } from './item-page';

/** Deja terminar las promesas pendientes (marcar como visto espera la respuesta y luego vuelve a pedir). */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('ItemPage', () => {
  /** Reproductor de música falso: lo que el detalle le pide y la pista que "suena". */
  let player: { play: ReturnType<typeof vi.fn>; current: WritableSignal<QueueTrack | null> };

  beforeEach(() => {
    player = { play: vi.fn(), current: signal<QueueTrack | null>(null) };
  });

  function detail(kind: ItemKind, title: string, extra: Partial<ItemDetail> = {}): ItemDetail {
    return {
      id: 'x1',
      kind,
      libraryId: 'lib1',
      libraryName: 'Anime',
      libraryType: 'Anime',
      parentId: null,
      parentTitle: null,
      ancestors: [],
      title,
      year: null,
      overview: null,
      contentRating: null,
      communityRating: null,
      genres: [],
      hasImage: false,
      dateAdded: '2026-11-17T00:00:00Z',
      seasonNumber: null,
      episodeNumber: null,
      absoluteNumber: null,
      artist: null,
      trackNumber: null,
      discNumber: null,
      durationSeconds: null,
      fileSize: null,
      positionSeconds: 0,
      played: false,
      unplayedCount: null,
      originalTitle: null,
      endYear: null,
      status: null,
      studios: [],
      tags: [],
      directors: [],
      actors: [],
      runtimeMinutes: null,
      hasBackdrop: false,
      links: [],
      nextAiringAt: null,
      nextAiringEpisode: null,
      metadataUpdatedAt: null,
      metadataStatus: null,
      metadataMessage: null,
      ...extra,
    };
  }

  function child(id: string, kind: ItemKind, title: string, extra: Partial<ChildItem> = {}): ChildItem {
    return {
      id,
      kind,
      title,
      year: null,
      overview: null,
      hasImage: false,
      seasonNumber: null,
      episodeNumber: null,
      absoluteNumber: null,
      artist: null,
      trackNumber: null,
      discNumber: null,
      durationSeconds: null,
      childCount: null,
      positionSeconds: 0,
      played: false,
      unplayedCount: null,
      ...extra,
    };
  }

  function next(extra: Partial<ResumeItem> = {}): ResumeItem {
    return {
      id: 'e3',
      kind: 'Episode',
      title: 'Episodio 3',
      seriesTitle: 'Mi Anime',
      parentTitle: 'Lat',
      imageId: 'e3',
      durationSeconds: 1200,
      positionSeconds: 0,
      lastPlayedAt: null,
      ...extra,
    };
  }

  /**
   * Muestra el elemento x1 con esos hijos y ese "A continuación" (null: la API respondió 204). Responde
   * todas las solicitudes antes de esperar: `whenStable` espera a las que siguen pendientes.
   */
  async function render(
    item: ItemDetail,
    children: ChildItem[] = [],
    nextUp: ResumeItem | null = null,
    isAdmin = false,
  ) {
    TestBed.configureTestingModule({
      imports: [ItemPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PlayerService, useValue: player },
      ],
    });
    TestBed.inject(SessionStore).user.set({
      id: 'u1',
      userName: 'ana',
      displayName: 'Ana',
      isAdmin,
    });
    const fixture = TestBed.createComponent(ItemPage);
    fixture.componentRef.setInput('id', 'x1');
    fixture.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/items/x1').flush(item);
    // La respuesta se entrega en otra tarea: hasta entonces no existen las solicitudes que dependen del detalle.
    await settle();
    TestBed.tick();
    if (item.kind === 'Series' || item.kind === 'Season' || item.kind === 'MusicAlbum') {
      http.expectOne('/api/items/x1/children').flush(children);
    }
    if (item.kind === 'Series' || item.kind === 'Season') {
      const request = http.expectOne('/api/items/x1/next-up');
      if (nextUp) {
        request.flush(nextUp);
      } else {
        request.flush(null, { status: 204, statusText: 'No Content' });
      }
    }
    await settle();
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();
    http.verify();
    return { fixture, http, element: fixture.nativeElement as HTMLElement };
  }

  const buttonWithText = (element: HTMLElement, text: string) =>
    Array.from(element.querySelectorAll<HTMLElement>('a, button')).find((el) =>
      el.textContent?.includes(text),
    );

  it('muestra las carpetas de una serie (Lat, Sub) como tarjetas con cuántos episodios tienen', async () => {
    const { element } = await render(detail('Series', 'Mi Anime'), [
      child('lat', 'Season', 'Lat', { hasImage: true, childCount: 12 }),
      child('sub', 'Season', 'Sub', { hasImage: true, childCount: 1 }),
    ]);

    const headings = Array.from(element.querySelectorAll('h2')).map((h) => h.textContent?.trim());
    expect(headings).toEqual(['Temporadas']);

    const cards = Array.from(element.querySelectorAll('app-poster-card'));
    expect(cards.map((card) => card.querySelector('.title')?.textContent?.trim())).toEqual([
      'Lat',
      'Sub',
    ]);
    expect(cards[0].textContent).toContain('12 episodios');
    expect(cards[1].textContent).toContain('1 episodio');
    expect(cards[0].querySelector('a')!.getAttribute('href')).toBe('/items/lat');
    expect(cards[0].querySelector('img')!.getAttribute('src')).toBe('/api/items/lat/image');
  });

  it('lista los episodios con su número absoluto y su miniatura cuando la hay', async () => {
    const { element } = await render(detail('Season', 'Lat'), [
      child('e1', 'Episode', 'Episodio 1', { absoluteNumber: 1, hasImage: true, durationSeconds: 1425 }),
      child('e2', 'Episode', 'Episodio 2', { absoluteNumber: 2 }),
    ]);

    expect(element.querySelector('h2')!.textContent).toContain('Episodios');
    expect(element.querySelector('app-poster-card')).toBeNull();

    const rows = Array.from(element.querySelectorAll<HTMLAnchorElement>('a.child.episode'));
    expect(rows.map((row) => row.querySelector('.number')!.textContent?.trim())).toEqual(['1', '2']);
    expect(rows[0].querySelector('.duration')!.textContent).toContain('23:45');
    expect(rows[0].querySelector('.thumb img')!.getAttribute('src')).toBe('/api/items/e1/image');
    expect(rows[1].querySelector('.thumb img')).toBeNull();
    expect(rows[1].querySelector('.thumb mat-icon')).not.toBeNull();
  });

  it('si la miniatura no carga, deja el ícono en su lugar', async () => {
    const { fixture, element } = await render(detail('Season', 'Lat'), [
      child('e1', 'Episode', 'Episodio 1', { absoluteNumber: 1, hasImage: true }),
    ]);

    element.querySelector('.thumb img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('.thumb img')).toBeNull();
    expect(element.querySelector('.thumb mat-icon')).not.toBeNull();
  });

  it('una serie con temporadas y episodios sueltos muestra las dos secciones', async () => {
    const { element } = await render(detail('Series', 'Mi Anime'), [
      child('sp', 'Season', 'Especiales', { seasonNumber: 0, childCount: 1 }),
      child('e1', 'Episode', 'Episodio 1', { absoluteNumber: 1 }),
    ]);

    const headings = Array.from(element.querySelectorAll('h2')).map((h) => h.textContent?.trim());
    expect(headings).toEqual(['Temporadas', 'Episodios']);
  });

  it('una película ofrece reproducir y lleva al reproductor', async () => {
    const { element } = await render(detail('Movie', 'Heat'));

    const play = buttonWithText(element, 'Reproducir')!;
    expect(play.getAttribute('href')).toBe('/reproducir/x1');
    expect(play.textContent).not.toContain('Continuar');
  });

  it('con una posición guardada dice "Continuar desde" y el minuto', async () => {
    const { element } = await render(detail('Movie', 'Heat', { positionSeconds: 754 }));

    const play = buttonWithText(element, 'Continuar desde')!;
    expect(play.textContent).toContain('Continuar desde 12:34');
    expect(play.getAttribute('href')).toBe('/reproducir/x1');
  });

  it('con menos de unos segundos guardados empieza de cero', async () => {
    const { element } = await render(detail('Movie', 'Heat', { positionSeconds: 2 }));

    expect(buttonWithText(element, 'Continuar desde')).toBeUndefined();
    expect(buttonWithText(element, 'Reproducir')).toBeDefined();
  });

  it('marca un episodio como visto y vuelve a pedir el detalle', async () => {
    const episode = detail('Episode', 'Episodio 1');
    const { fixture, http, element } = await render(episode);
    expect(buttonWithText(element, 'Marcar como visto')).toBeDefined();

    buttonWithText(element, 'Marcar como visto')!.click();
    http.expectOne((r) => r.method === 'POST' && r.url === '/api/items/x1/played').flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    await settle();
    TestBed.tick();
    http.expectOne('/api/items/x1').flush({ ...episode, played: true });
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(buttonWithText(element, 'Marcar como no visto')).toBeDefined();
    expect(buttonWithText(element, 'Marcar como visto')).toBeUndefined();
  });

  it('marca como no visto con DELETE', async () => {
    const { http, element } = await render(detail('Movie', 'Heat', { played: true }));

    buttonWithText(element, 'Marcar como no visto')!.click();

    http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/items/x1/played').flush(null, {
      status: 204,
      statusText: 'No Content',
    });
  });

  it('avisa cuando no se pudo marcar como visto', async () => {
    const { fixture, http, element } = await render(detail('Movie', 'Heat'));

    buttonWithText(element, 'Marcar como visto')!.click();
    http.expectOne('/api/items/x1/played').flush(null, { status: 500, statusText: 'Error' });
    await settle();
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')!.textContent).toContain('No se pudo actualizar');
  });

  it('en una serie, "Reproducir" lleva al episodio de "A continuación" y lo muestra con su avance', async () => {
    const { element } = await render(
      detail('Series', 'Mi Anime', { unplayedCount: 5 }),
      [child('lat', 'Season', 'Lat', { childCount: 3, unplayedCount: 3 })],
      next({ positionSeconds: 600 }),
    );

    expect(element.querySelector('#next-title')!.textContent).toContain('A continuación');
    const card = element.querySelector('app-resume-card')!;
    expect(card.querySelector('a')!.getAttribute('href')).toBe('/reproducir/e3');
    expect(card.textContent).toContain('Mi Anime');
    expect(card.textContent).toContain('Lat · Episodio 3');
    expect(card.querySelector('mat-progress-bar')).not.toBeNull();
    const play = buttonWithText(element, 'Continuar desde 10:00')!;
    expect(play.getAttribute('href')).toBe('/reproducir/e3');
  });

  it('sin "A continuación" no muestra la tarjeta y una carpeta vuelve a ofrecer su primer episodio', async () => {
    const { element } = await render(
      detail('Season', 'Lat', { unplayedCount: 0 }),
      [
        child('e1', 'Episode', 'Episodio 1', { absoluteNumber: 1, played: true }),
        child('e2', 'Episode', 'Episodio 2', { absoluteNumber: 2, played: true }),
      ],
      null,
    );

    expect(element.querySelector('app-resume-card')).toBeNull();
    expect(element.querySelector('#next-title')).toBeNull();
    expect(buttonWithText(element, 'Reproducir')!.getAttribute('href')).toBe('/reproducir/e1');
    // Todo visto: lo que se ofrece es marcarlo como no visto.
    expect(buttonWithText(element, 'Marcar como no visto')).toBeDefined();
  });

  it('una serie con episodios por ver ofrece marcarla como vista', async () => {
    const { element } = await render(detail('Series', 'Mi Anime', { unplayedCount: 2 }), [
      child('lat', 'Season', 'Lat', { childCount: 2, unplayedCount: 2 }),
    ]);

    expect(buttonWithText(element, 'Marcar como visto')).toBeDefined();
  });

  it('las filas de episodios muestran el avance, la marca de visto y un botón para reproducir', async () => {
    const { element } = await render(detail('Season', 'Lat', { unplayedCount: 2 }), [
      child('e1', 'Episode', 'Episodio 1', { absoluteNumber: 1, played: true, durationSeconds: 1200 }),
      child('e2', 'Episode', 'Episodio 2', {
        absoluteNumber: 2,
        positionSeconds: 300,
        durationSeconds: 1200,
      }),
      child('e3', 'Episode', 'Episodio 3', { absoluteNumber: 3, durationSeconds: 1200 }),
    ]);

    const rows = Array.from(element.querySelectorAll<HTMLElement>('li.entry'));
    expect(rows[0].querySelector('.badge')!.getAttribute('aria-label')).toBe('Visto');
    expect(rows[0].querySelector('mat-progress-bar')).toBeNull();
    const bar = rows[1].querySelector('.thumb mat-progress-bar')!;
    expect(bar.getAttribute('aria-label')).toBe('Avance de Episodio 2');
    expect(rows[1].querySelector('.badge')).toBeNull();
    expect(rows[2].querySelector('.thumb mat-progress-bar')).toBeNull();
    expect(rows[2].querySelector('.badge')).toBeNull();
    const play = rows[1].querySelector('a.row-play')!;
    expect(play.getAttribute('href')).toBe('/reproducir/e2');
    expect(play.getAttribute('aria-label')).toBe('Reproducir Episodio 2');
  });

  it('las tarjetas de carpeta dicen cuántos episodios faltan por ver', async () => {
    const { element } = await render(detail('Series', 'Mi Anime', { unplayedCount: 3 }), [
      child('lat', 'Season', 'Lat', { childCount: 12, unplayedCount: 3 }),
      child('sub', 'Season', 'Sub', { childCount: 2, unplayedCount: 0 }),
      child('esp', 'Season', 'Especiales', { childCount: 1, unplayedCount: 1 }),
    ]);

    const cards = Array.from(element.querySelectorAll('app-poster-card'));
    expect(cards[0].textContent).toContain('12 episodios · 3 sin ver');
    expect(cards[1].textContent).toContain('2 episodios · Vista');
    expect(cards[2].textContent).toContain('1 episodio');
    expect(cards[2].textContent).not.toContain('sin ver');
  });

  describe('álbum', () => {
    const tracks = [
      child('t1', 'Track', 'So What', { trackNumber: 1, artist: 'Miles Davis', durationSeconds: 545 }),
      child('t2', 'Track', 'Blue in Green', { trackNumber: 2, durationSeconds: 337, hasImage: true }),
    ];

    it('"Reproducir álbum" arma la cola con todas las pistas desde la primera', async () => {
      const { element } = await render(
        detail('MusicAlbum', 'Kind of Blue', { artist: 'Miles Davis', hasImage: true }),
        tracks,
      );

      (buttonWithText(element, 'Reproducir álbum') as HTMLButtonElement).click();

      expect(player.play).toHaveBeenCalledTimes(1);
      const [queue, start] = player.play.mock.calls[0] as [QueueTrack[], number];
      expect(start).toBe(0);
      expect(queue.map((track) => track.id)).toEqual(['t1', 't2']);
      expect(queue[0]).toEqual({
        id: 't1',
        title: 'So What',
        artist: 'Miles Davis',
        album: 'Kind of Blue',
        durationSeconds: 545,
        imageId: 'x1',
      });
      // Sin artista propio usa el del álbum, y con carátula propia usa la suya.
      expect(queue[1].artist).toBe('Miles Davis');
      expect(queue[1].imageId).toBe('t2');
    });

    it('reproducir desde una pista empieza la cola en esa pista', async () => {
      const { element } = await render(detail('MusicAlbum', 'Kind of Blue'), tracks);

      element.querySelector<HTMLButtonElement>('button[aria-label="Reproducir Blue in Green"]')!.click();

      const [queue, start] = player.play.mock.calls[0] as [QueueTrack[], number];
      expect(start).toBe(1);
      expect(queue).toHaveLength(2);
    });

    it('marca la pista que está sonando', async () => {
      player.current.set({
        id: 't2',
        title: 'Blue in Green',
        artist: null,
        album: null,
        durationSeconds: null,
        imageId: null,
      });
      const { element } = await render(detail('MusicAlbum', 'Kind of Blue'), tracks);

      const rows = Array.from(element.querySelectorAll('li.entry'));
      expect(rows[0].classList).not.toContain('current');
      expect(rows[1].classList).toContain('current');
    });

    it('un álbum no se marca como visto ni pide "A continuación"', async () => {
      const { element } = await render(detail('MusicAlbum', 'Kind of Blue'), tracks);

      expect(buttonWithText(element, 'Marcar como')).toBeUndefined();
      expect(element.querySelector('app-resume-card')).toBeNull();
    });
  });

  it('una pista se puede reproducir sola', async () => {
    const { element } = await render(
      detail('Track', 'So What', { artist: 'Miles Davis', parentTitle: 'Kind of Blue', durationSeconds: 545 }),
    );

    (buttonWithText(element, 'Reproducir') as HTMLButtonElement).click();

    expect(player.play).toHaveBeenCalledWith([
      {
        id: 'x1',
        title: 'So What',
        artist: 'Miles Davis',
        album: 'Kind of Blue',
        durationSeconds: 545,
        imageId: null,
      },
    ]);
  });
  describe('metadatos', () => {
    it('una serie y una película usan la cabecera; un episodio, no', async () => {
      const series = await render(detail('Series', 'Mi Anime', { hasBackdrop: true }));
      expect(series.element.querySelector('app-item-hero')).not.toBeNull();
      expect(series.element.querySelector('app-item-hero .backdrop')).not.toBeNull();
      TestBed.resetTestingModule();

      const movie = await render(detail('Movie', 'Heat'));
      expect(movie.element.querySelector('app-item-hero')).not.toBeNull();
      expect(buttonWithText(movie.element, 'Reproducir')).toBeDefined();
      TestBed.resetTestingModule();

      const episode = await render(detail('Episode', 'Episodio 1'));
      expect(episode.element.querySelector('app-item-hero')).toBeNull();
    });

    it('muestra el título original y los botones de siempre dentro de la cabecera', async () => {
      const { element } = await render(
        detail('Series', 'Mi Anime', { originalTitle: 'Boku no Anime', played: false, unplayedCount: 3 }),
      );
      expect(element.querySelector('app-item-hero .original')!.textContent).toContain('Boku no Anime');
      expect(element.querySelectorAll('h1').length).toBe(1);
      expect(buttonWithText(element, 'Marcar como visto')).toBeDefined();
    });

    it('un usuario común no ve Identificar ni Actualizar metadatos', async () => {
      const { element } = await render(detail('Series', 'Mi Anime'));
      expect(buttonWithText(element, 'Identificar')).toBeUndefined();
      expect(buttonWithText(element, 'Actualizar metadatos')).toBeUndefined();
    });

    it('un administrador ve los dos botones en series y películas, pero no en episodios', async () => {
      const series = await render(detail('Series', 'Mi Anime'), [], null, true);
      expect(buttonWithText(series.element, 'Identificar')).toBeDefined();
      expect(buttonWithText(series.element, 'Actualizar metadatos')).toBeDefined();
      TestBed.resetTestingModule();

      const episode = await render(detail('Episode', 'Episodio 1'), [], null, true);
      expect(buttonWithText(episode.element, 'Identificar')).toBeUndefined();
    });

    it('Actualizar metadatos pide la actualización y vuelve a pedir el detalle', async () => {
      const { fixture, http, element } = await render(detail('Movie', 'Heat'), [], null, true);

      (buttonWithText(element, 'Actualizar metadatos') as HTMLButtonElement).click();
      http
        .expectOne('/api/admin/items/x1/metadata/refresh')
        .flush({ status: 'Matched', message: null, title: 'Heat' });
      await settle();
      TestBed.tick();
      fixture.detectChanges();

      http.expectOne('/api/items/x1').flush(detail('Movie', 'Heat', { year: 1995 }));
      await settle();
      http.verify();
    });

    it('si la actualización falla, muestra el mensaje de la API', async () => {
      const { fixture, http, element } = await render(detail('Movie', 'Heat'), [], null, true);

      (buttonWithText(element, 'Actualizar metadatos') as HTMLButtonElement).click();
      http
        .expectOne('/api/admin/items/x1/metadata/refresh')
        .flush({ title: 'Falta la clave de OMDb.' }, { status: 409, statusText: 'Conflict' });
      await settle();
      TestBed.tick();
      fixture.detectChanges();

      expect(element.querySelector('[role="alert"]')!.textContent).toContain('Falta la clave de OMDb.');
    });

    it('el administrador ve por qué no hay metadatos; los demás no', async () => {
      const item = detail('Movie', 'Heat', {
        metadataStatus: 'NotFound',
        metadataMessage: 'OMDb no encontró "Heat".',
      });
      const admin = await render(item, [], null, true);
      expect(admin.element.querySelector('.note')!.textContent).toContain('OMDb no encontró');
      TestBed.resetTestingModule();

      const user = await render(item);
      expect(user.element.querySelector('.note')).toBeNull();
    });
  });
});
