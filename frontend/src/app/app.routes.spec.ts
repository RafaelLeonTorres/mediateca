import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { SessionStore } from './core/auth/session-store';

describe('rutas', () => {
  function navigator(isAdmin: boolean) {
    const user = signal({ id: '1', userName: 'a', displayName: 'A', isAdmin });
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        { provide: SessionStore, useValue: { needsSetup: signal(false), isLoggedIn: () => true, user } },
      ],
    });
    return TestBed.inject(Router);
  }

  it('/admin lleva a Bibliotecas y /admin/bibliotecas sigue funcionando', async () => {
    const router = navigator(true);

    await router.navigateByUrl('/admin');
    expect(router.url).toBe('/admin/bibliotecas');

    await router.navigateByUrl('/admin/usuarios');
    expect(router.url).toBe('/admin/usuarios');

    await router.navigateByUrl('/admin/bibliotecas');
    expect(router.url).toBe('/admin/bibliotecas');
  });

  it('un usuario normal no entra a ninguna sección de la administración', async () => {
    const router = navigator(false);

    await router.navigateByUrl('/admin/usuarios');
    expect(router.url).toBe('/');

    await router.navigateByUrl('/admin/bibliotecas');
    expect(router.url).toBe('/');
  });

  it('Mi cuenta la abre cualquier usuario con sesión', async () => {
    const router = navigator(false);

    await router.navigateByUrl('/mi-cuenta');

    expect(router.url).toBe('/mi-cuenta');
  });
});
