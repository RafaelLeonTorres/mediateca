import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';

import { adminGuard, authGuard, guestGuard, setupPendingGuard } from './guards';
import { SessionStore } from './session-store';

describe('guards', () => {
  function configure(state: { needsSetup: boolean; loggedIn: boolean; isAdmin?: boolean }) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: SessionStore,
          useValue: {
            needsSetup: signal(state.needsSetup),
            isLoggedIn: signal(state.loggedIn),
            user: signal(
              state.loggedIn
                ? { id: '1', userName: 'u', displayName: 'U', isAdmin: !!state.isAdmin }
                : null,
            ),
          },
        },
      ],
    });
  }

  function run(guard: CanActivateFn, url = '/') {
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
  }

  function path(result: unknown): string {
    return TestBed.inject(Router).serializeUrl(result as UrlTree);
  }

  it('sin administrador, todo lleva al asistente', () => {
    configure({ needsSetup: true, loggedIn: false });

    expect(path(run(authGuard))).toBe('/setup');
    expect(path(run(guestGuard))).toBe('/setup');
    expect(run(setupPendingGuard)).toBe(true);
  });

  it('sin sesión, las pantallas protegidas llevan a /login y recuerdan a dónde volver', () => {
    configure({ needsSetup: false, loggedIn: false });

    const result = run(authGuard, '/bibliotecas/1') as UrlTree;
    expect(path(result).startsWith('/login')).toBe(true);
    expect(result.queryParams).toEqual({ volver: '/bibliotecas/1' });
    expect(run(guestGuard)).toBe(true);
  });

  it('con sesión, el inicio se abre y /login y el asistente llevan al inicio', () => {
    configure({ needsSetup: false, loggedIn: true });

    expect(run(authGuard)).toBe(true);
    expect(path(run(guestGuard))).toBe('/');
    expect(path(run(setupPendingGuard))).toBe('/');
  });

  it('la administración solo se abre para administradores', () => {
    configure({ needsSetup: false, loggedIn: true, isAdmin: true });
    expect(run(adminGuard, '/admin/bibliotecas')).toBe(true);
  });

  it('un usuario sin permisos de administrador vuelve al inicio', () => {
    configure({ needsSetup: false, loggedIn: true, isAdmin: false });
    expect(path(run(adminGuard, '/admin/bibliotecas'))).toBe('/');
  });

  it('sin sesión, la administración lleva a /login y recuerda a dónde volver', () => {
    configure({ needsSetup: false, loggedIn: false });

    const result = run(adminGuard, '/admin/bibliotecas') as UrlTree;
    expect(path(result).startsWith('/login')).toBe(true);
    expect(result.queryParams).toEqual({ volver: '/admin/bibliotecas' });
  });
});
