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

    /** Responde las dos peticiones del inicio y deja que la pantalla se actualice. */
    const respond = async (libraries: unknown[], recent: unknown[]) => {
      http.expectOne('/api/libraries').flush(libraries);
      http.expectOne((r) => r.url === '/api/me/recently-added').flush(recent);
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
});
