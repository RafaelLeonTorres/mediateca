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

  it('si la biblioteca no existe o no es de las suyas, lo dice en lugar de quedarse cargando', async () => {
    const { fixture, http, element } = render();

    // La API responde 404 en los dos casos, para no revelar que la biblioteca existe.
    itemsRequest(http).flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne('/api/libraries').flush([]);
    http.expectOne('/api/libraries/lib1/filters').flush(null, { status: 404, statusText: 'Not Found' });
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.textContent).toContain('No se encontró la biblioteca');
    expect(element.querySelector('mat-progress-bar')).toBeNull();
    expect(element.querySelector('form.filters')).toBeNull();
    expect(element.querySelector('a[href="/"]')?.textContent).toContain('Volver al inicio');
  });

  it('si fallan los filtros, la cuadrícula se ve igual', async () => {
    const { fixture, http, element } = render();

    itemsRequest(http).flush({
      items: [{ id: 'i1', kind: 'Movie', title: 'Heat', year: 1995, artist: null, hasImage: false }],
      total: 1,
      page: 1,
      pageSize: 48,
    });
    http.expectOne('/api/libraries').flush([]);
    http.expectOne('/api/libraries/lib1/filters').flush(null, { status: 500, statusText: 'Error' });
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.textContent).toContain('Heat');
    expect(element.textContent).not.toContain('No se encontró la biblioteca');
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
