import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SessionStore, type User } from '../../core/auth/session-store';
import { Home } from './home';

describe('Home', () => {
  const admin: User = { id: '1', userName: 'admin', displayName: 'Rafael', isAdmin: true };
  const viewer: User = { id: '2', userName: 'ana', displayName: 'Ana', isAdmin: false };

  function render(user: User) {
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: { user: signal(user) } },
      ],
    });
    const fixture = TestBed.createComponent(Home);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    TestBed.tick();

    /** Responde las tres peticiones del inicio y deja que la pantalla se actualice. */
    const respond = async (libraries: unknown[], recent: unknown[], resume: unknown[] = []) => {
      http.expectOne('/api/libraries').flush(libraries);
      http.expectOne((r) => r.url === '/api/me/recently-added').flush(recent);
      http.expectOne((r) => r.url === '/api/me/continue-watching').flush(resume);
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { element: fixture.nativeElement as HTMLElement, respond };
  }

  it('saluda con el nombre del usuario', () => {
    const { element } = render(admin);

    expect(element.textContent).toContain('Hola, Rafael');
  });

  it('sin bibliotecas, el administrador ve cómo agregar la primera', async () => {
    const { element, respond } = render(admin);

    await respond([], []);

    expect(element.textContent).toContain('Todavía no hay bibliotecas');
    expect(element.querySelector('a[href="/admin/bibliotecas"]')).not.toBeNull();
  });

  it('sin bibliotecas, un usuario normal solo ve el aviso', async () => {
    const { element, respond } = render(viewer);

    await respond([], []);

    expect(element.textContent).toContain('te dé acceso');
    expect(element.querySelector('a[href="/admin/bibliotecas"]')).toBeNull();
  });

  it('muestra lo agregado recientemente y las bibliotecas', async () => {
    const { element, respond } = render(viewer);

    await respond(
      [{ id: 'l1', name: 'Cine', type: 'Movies', itemCount: 2 }],
      [{ id: 'i1', kind: 'Movie', title: 'Heat', year: 1995, artist: null, hasImage: false }],
    );

    expect(element.textContent).toContain('Agregado recientemente');
    expect(element.textContent).toContain('Heat');
    expect(element.textContent).toContain('Mis bibliotecas');
    expect(element.querySelector('a[href="/bibliotecas/l1"]')!.textContent).toContain('Cine');
  });

  it('muestra "Continuar viendo" arriba de lo agregado recientemente, con tarjetas que retoman el video', async () => {
    const { element, respond } = render(viewer);

    await respond(
      [{ id: 'l1', name: 'Anime', type: 'Anime', itemCount: 1 }],
      [{ id: 'i1', kind: 'Series', title: 'Mi Anime', year: null, artist: null, hasImage: false }],
      [
        {
          id: 'e2',
          kind: 'Episode',
          title: 'Episodio 2',
          seriesTitle: 'Mi Anime',
          parentTitle: 'Lat',
          imageId: 'e2',
          durationSeconds: 1200,
          positionSeconds: 300,
          lastPlayedAt: '2026-11-17T20:00:00Z',
        },
      ],
    );

    const headings = Array.from(element.querySelectorAll('h2')).map((h) => h.textContent?.trim());
    expect(headings).toEqual(['Continuar viendo', 'Agregado recientemente', 'Mis bibliotecas']);

    const card = element.querySelector('app-resume-card')!;
    expect(card.querySelector('a')!.getAttribute('href')).toBe('/reproducir/e2');
    expect(card.querySelector('img')!.getAttribute('src')).toBe('/api/items/e2/image');
    expect(card.querySelector('.title')!.textContent).toContain('Mi Anime');
    expect(card.querySelector('.subtitle')!.textContent).toContain('Lat · Episodio 2');
    expect(card.querySelector('mat-progress-bar')!.getAttribute('aria-label')).toBe('Avance: 25 %');
  });

  it('sin nada a medias no muestra "Continuar viendo"', async () => {
    const { element, respond } = render(viewer);

    await respond(
      [{ id: 'l1', name: 'Cine', type: 'Movies', itemCount: 1 }],
      [{ id: 'i1', kind: 'Movie', title: 'Heat', year: 1995, artist: null, hasImage: false }],
      [],
    );

    expect(element.textContent).not.toContain('Continuar viendo');
    expect(element.querySelector('app-resume-card')).toBeNull();
  });
});
