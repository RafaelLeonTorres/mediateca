import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { LibraryPage, SEARCH_DEBOUNCE_MS } from './library-page';

describe('LibraryPage', () => {
  afterEach(() => vi.useRealTimers());

  function render() {
    TestBed.configureTestingModule({
      imports: [LibraryPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: SessionStore,
          useValue: { user: signal({ id: '1', userName: 'a', displayName: 'A', isAdmin: true }) },
        },
      ],
    });
    const fixture = TestBed.createComponent(LibraryPage);
    fixture.componentRef.setInput('id', 'lib1');
    fixture.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    return { fixture, http, element: fixture.nativeElement as HTMLElement };
  }

  const itemsRequest = (http: HttpTestingController) =>
    http.expectOne((r) => r.url === '/api/libraries/lib1/items');

  it('pide la primera página ordenada por título y muestra los pósters', async () => {
    const { fixture, http, element } = render();

    const request = itemsRequest(http);
    expect(request.request.params.get('sort')).toBe('title');
    expect(request.request.params.get('page')).toBe('1');
    request.flush({
      items: [
        { id: 'i1', kind: 'Movie', title: 'Heat', year: 1995, artist: null, hasImage: false },
      ],
      total: 1,
      page: 1,
      pageSize: 48,
    });
    http.expectOne('/api/libraries').flush([]);
    http.expectOne('/api/libraries/lib1/filters').flush({ genres: ['Drama'], years: [1995] });
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('ul.grid')).not.toBeNull();
    expect(element.textContent).toContain('Heat');
    expect(element.textContent).toContain('1 resultado');
  });

  it('espera una pausa al escribir antes de buscar', () => {
    vi.useFakeTimers();
    const { fixture, http, element } = render();
    itemsRequest(http).flush({ items: [], total: 0, page: 1, pageSize: 48 });
    http.expectOne('/api/libraries').flush([]);
    http.expectOne('/api/libraries/lib1/filters').flush({ genres: [], years: [] });
    TestBed.tick();

    const input = element.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = 'mat';
    input.dispatchEvent(new Event('input'));
    input.value = 'matrix';
    input.dispatchEvent(new Event('input'));
    TestBed.tick();
    http.expectNone((r) => r.url === '/api/libraries/lib1/items');

    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    TestBed.tick();
    fixture.detectChanges();

    const request = itemsRequest(http);
    expect(request.request.params.get('search')).toBe('matrix');
    expect(request.request.params.get('page')).toBe('1');
    request.flush({ items: [], total: 0, page: 1, pageSize: 48 });
  });

  it('sin elementos y sin filtros, avisa que la biblioteca está vacía', async () => {
    const { fixture, http, element } = render();

    itemsRequest(http).flush({ items: [], total: 0, page: 1, pageSize: 48 });
    http.expectOne('/api/libraries').flush([]);
    http.expectOne('/api/libraries/lib1/filters').flush({ genres: [], years: [] });
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.textContent).toContain('Esta biblioteca está vacía');
  });
});
