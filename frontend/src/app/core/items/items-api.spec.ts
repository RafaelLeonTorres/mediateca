import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ItemsApi, imageUrl } from './items-api';

describe('ItemsApi', () => {
  let api: ItemsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ItemsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('arma la dirección de la imagen', () => {
    expect(imageUrl('abc')).toBe('/api/items/abc/image');
  });

  it('pide lo agregado recientemente con el límite', async () => {
    const resource = TestBed.runInInjectionContext(() => api.recentlyAdded(10));
    TestBed.tick();

    const request = http.expectOne((r) => r.url === '/api/me/recently-added');
    expect(request.request.params.get('limit')).toBe('10');
    request.flush([]);
    // La respuesta llega al recurso de forma asíncrona.
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()).toEqual([]);
  });

  it('pide el detalle y vuelve a pedirlo al cambiar el id', () => {
    const id = signal<string | undefined>('uno');
    TestBed.runInInjectionContext(() => api.detail(() => id()));
    TestBed.tick();
    http.expectOne('/api/items/uno').flush({ id: 'uno' });
    TestBed.tick();

    id.set('dos');
    TestBed.tick();
    http.expectOne('/api/items/dos').flush({ id: 'dos' });
  });

  it('pide los hijos de un elemento', async () => {
    const resource = TestBed.runInInjectionContext(() => api.children(() => 'serie'));
    TestBed.tick();

    http.expectOne('/api/items/serie/children').flush([{ id: 't1', title: 'Temporada 1' }]);
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()?.[0]?.title).toBe('Temporada 1');
  });
});
