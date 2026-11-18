import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { ItemDetail } from '../../core/items/items-api';
import { ItemHero } from './item-hero';

function detail(extra: Partial<ItemDetail> = {}): ItemDetail {
  return {
    id: 's1',
    kind: 'Series',
    libraryId: 'lib1',
    libraryName: 'Anime',
    libraryType: 'Anime',
    parentId: null,
    parentTitle: null,
    ancestors: [],
    title: 'Sousou no Frieren',
    year: 2023,
    overview: 'La maga Frieren y el viaje después del viaje.',
    contentRating: null,
    communityRating: null,
    genres: [],
    hasImage: true,
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

@Component({
  imports: [ItemHero],
  template: `<app-item-hero [item]="item"><button type="button">Reproducir</button></app-item-hero>`,
})
class Host {
  item = detail();
}

describe('ItemHero', () => {
  function render(extra: Partial<ItemDetail> = {}) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.item = detail(extra);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('muestra el título, el póster y la sinopsis, y proyecta los botones', () => {
    const el = render();
    expect(el.querySelector('h1')?.textContent).toContain('Sousou no Frieren');
    expect(el.querySelector('.poster img')?.getAttribute('src')).toBe('/api/items/s1/image');
    expect(el.querySelector('.overview')?.textContent).toContain('La maga Frieren');
    expect(el.querySelector('.actions button')?.textContent).toContain('Reproducir');
  });

  it('no usa encabezados h2 ni avisos de error', () => {
    const el = render({ genres: ['Fantasía'] });
    expect(el.querySelector('h2')).toBeNull();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('pone la imagen de fondo solo si el elemento la tiene', () => {
    expect(render().querySelector('.backdrop')).toBeNull();
    const el = render({ hasBackdrop: true });
    expect(el.querySelector('.backdrop')?.getAttribute('src')).toBe(
      '/api/items/s1/image?type=backdrop',
    );
    expect(el.querySelector('.backdrop')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('muestra el título original solo si es distinto', () => {
    expect(render({ originalTitle: '葬送のフリーレン' }).querySelector('.original')?.textContent).toContain(
      '葬送のフリーレン',
    );
    expect(render({ originalTitle: 'Sousou no Frieren' }).querySelector('.original')).toBeNull();
  });

  it('escribe "2026 – Actualidad" para una serie en emisión', () => {
    const el = render({ year: 2026, status: 'Airing' });
    expect(el.querySelector('.years')?.textContent).toBe('2026 – Actualidad');
    expect(el.querySelector('.status')?.textContent).toBe('En emisión');
  });

  it('escribe el rango de años de una serie terminada', () => {
    const el = render({ year: 2019, endYear: 2021, status: 'Finished' });
    expect(el.querySelector('.years')?.textContent).toBe('2019 – 2021');
    expect(el.querySelector('.status')?.textContent).toBe('Finalizada');
  });

  it('una película muestra solo el año y su duración en minutos', () => {
    const el = render({ kind: 'Movie', year: 2008, runtimeMinutes: 10 });
    expect(el.querySelector('.years')?.textContent).toBe('2008');
    expect(el.querySelector('.duration')?.textContent).toBe('10 min');
  });

  it('muestra calificación y clasificación por edad', () => {
    const el = render({ communityRating: 8.64, contentRating: '18+' });
    expect(el.querySelector('.rating')?.textContent).toContain('8.6');
    expect(el.querySelector('.badge')?.textContent).toBe('18+');
  });

  it('dice cuándo se emite solo si la serie está en emisión', () => {
    const airing = render({ status: 'Airing', nextAiringAt: '2026-11-17T23:00:00Z' });
    expect(airing.querySelector('.airing')?.textContent).toContain('Se emite los');
    const finished = render({ status: 'Finished', nextAiringAt: '2026-11-17T23:00:00Z' });
    expect(finished.querySelector('.airing')).toBeNull();
  });

  it('muestra estudios y, como máximo, 10 etiquetas', () => {
    const tags = Array.from({ length: 14 }, (_, index) => `Etiqueta ${index + 1}`);
    const el = render({ studios: ['Madhouse', 'Bones'], tags });
    expect(el.querySelector('.line')?.textContent).toContain('Madhouse, Bones');
    expect(el.querySelectorAll('.tags li').length).toBe(10);
  });

  it('abre los enlaces externos en otra pestaña y muestra la atribución de AniList', () => {
    const el = render({
      links: [
        { site: 'AniList', url: 'https://anilist.co/anime/154587' },
        { site: 'MyAnimeList', url: 'https://myanimelist.net/anime/52991' },
      ],
    });
    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('.links a'));
    expect(links.length).toBe(2);
    expect(links[0].textContent).toContain('AniList');
    expect(links[1].textContent).toContain('MyAnimeList');
    expect(links.every((a) => a.target === '_blank' && a.rel.includes('noopener'))).toBe(true);
    expect(el.querySelector('.attribution a')?.textContent).toContain('Datos de AniList');
  });

  it('sin enlaces de AniList no muestra la atribución', () => {
    expect(render().querySelector('.attribution')).toBeNull();
  });
});
