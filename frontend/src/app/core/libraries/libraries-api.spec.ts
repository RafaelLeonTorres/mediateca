import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  EMPTY_ITEMS_QUERY,
  ITEMS_PAGE_SIZE,
  LibrariesApi,
  buildItemsRequest,
  type ItemsQuery,
} from './libraries-api';

describe('buildItemsRequest', () => {
  it('con la consulta vacía solo manda orden, página y tamaño', () => {
    const request = buildItemsRequest('abc', EMPTY_ITEMS_QUERY);

    expect(request.url).toBe('/api/libraries/abc/items');
    expect(request.params).toEqual({ sort: 'title', page: 1, pageSize: ITEMS_PAGE_SIZE });
  });

  it('incluye búsqueda, género y año solo cuando tienen valor', () => {
    const query: ItemsQuery = {
      search: '  matrix ',
      genre: 'Ciencia ficción',
      year: 1999,
      sort: 'year',
      page: 3,
    };

    expect(buildItemsRequest('abc', query).params).toEqual({
      sort: 'year',
      page: 3,
      pageSize: ITEMS_PAGE_SIZE,
      search: 'matrix',
      genre: 'Ciencia ficción',
      year: 1999,
    });
  });

  it('ignora una búsqueda de puros espacios', () => {
    const request = buildItemsRequest('abc', { ...EMPTY_ITEMS_QUERY, search: '   ' });

    expect(request.params).not.toHaveProperty('search');
  });
});

describe('LibrariesApi', () => {
  let api: LibrariesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(LibrariesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('pide las bibliotecas visibles', async () => {
    const resource = TestBed.runInInjectionContext(() => api.libraries());
    TestBed.tick();

    http
      .expectOne('/api/libraries')
      .flush([{ id: '1', name: 'Cine', type: 'Movies', itemCount: 2 }]);
    // La respuesta llega al recurso de forma asíncrona.
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()).toEqual([{ id: '1', name: 'Cine', type: 'Movies', itemCount: 2 }]);
  });

  it('pide los elementos con los parámetros de la consulta y los vuelve a pedir si cambia', async () => {
    const query = signal<ItemsQuery>({ ...EMPTY_ITEMS_QUERY });
    const resource = TestBed.runInInjectionContext(() =>
      api.items(
        () => 'lib1',
        () => query(),
      ),
    );
    TestBed.tick();

    const first = http.expectOne((r) => r.url === '/api/libraries/lib1/items');
    expect(first.request.params.get('sort')).toBe('title');
    expect(first.request.params.get('page')).toBe('1');
    first.flush({ items: [], total: 0, page: 1, pageSize: ITEMS_PAGE_SIZE });
    await TestBed.inject(ApplicationRef).whenStable();
    expect(resource.value()?.total).toBe(0);

    query.set({ ...EMPTY_ITEMS_QUERY, search: 'alien', page: 2 });
    TestBed.tick();

    const second = http.expectOne((r) => r.url === '/api/libraries/lib1/items');
    expect(second.request.params.get('search')).toBe('alien');
    expect(second.request.params.get('page')).toBe('2');
    second.flush({ items: [], total: 0, page: 2, pageSize: ITEMS_PAGE_SIZE });
  });

  it('sin id de biblioteca no hace ninguna petición', () => {
    TestBed.runInInjectionContext(() => api.filters(() => undefined));
    TestBed.tick();

    http.expectNone((r) => r.url.startsWith('/api/libraries'));
  });
});
