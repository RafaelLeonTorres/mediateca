import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { ChildItem, ItemDetail, ItemKind } from '../../core/items/items-api';
import { ItemPage } from './item-page';

describe('ItemPage', () => {
  function detail(kind: ItemKind, title: string): ItemDetail {
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
      ...extra,
    };
  }

  /** Muestra el elemento x1 con esos hijos. Responde las dos solicitudes antes de esperar. */
  async function render(item: ItemDetail, children: ChildItem[]) {
    TestBed.configureTestingModule({
      imports: [ItemPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(ItemPage);
    fixture.componentRef.setInput('id', 'x1');
    fixture.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/items/x1').flush(item);
    TestBed.tick();
    http.expectOne('/api/items/x1/children').flush(children);
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();
    http.verify();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

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
});
