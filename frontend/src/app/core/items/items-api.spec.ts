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

  it('pide "Continuar viendo" con el límite', async () => {
    const resource = TestBed.runInInjectionContext(() => api.continueWatching(5));
    TestBed.tick();

    const request = http.expectOne((r) => r.url === '/api/me/continue-watching');
    expect(request.request.params.get('limit')).toBe('5');
    request.flush([{ id: 'e1', title: 'Episodio 1' }]);
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()?.[0]?.title).toBe('Episodio 1');
  });

  it('pide "A continuación" de una serie y deja null cuando no queda nada (204)', async () => {
    const id = signal<string | undefined>('serie');
    const resource = TestBed.runInInjectionContext(() => api.nextUp(() => id()));
    TestBed.tick();

    http.expectOne('/api/items/serie/next-up').flush({ id: 'e3', title: 'Episodio 3' });
    await TestBed.inject(ApplicationRef).whenStable();
    expect(resource.value()?.title).toBe('Episodio 3');

    id.set('otra');
    TestBed.tick();
    http.expectOne('/api/items/otra/next-up').flush(null, { status: 204, statusText: 'No Content' });
    await TestBed.inject(ApplicationRef).whenStable();
    expect(resource.value()).toBeNull();
  });

  it('sin id no pide "A continuación"', () => {
    TestBed.runInInjectionContext(() => api.nextUp(() => undefined));
    TestBed.tick();

    http.expectNone((r) => r.url.endsWith('/next-up'));
  });
});
