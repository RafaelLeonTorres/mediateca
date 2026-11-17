import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { SessionStore } from './session-store';

/** El asistente de primer arranque solo se muestra mientras no exista el administrador. */
export const setupPendingGuard: CanActivateFn = () =>
  inject(SessionStore).needsSetup() ? true : inject(Router).createUrlTree(['/']);

/**
 * Pantallas que piden sesión. Sin administrador lleva al asistente; sin sesión, a /login, que
 * después regresa a la pantalla que se pidió.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  if (session.needsSetup()) {
    return router.createUrlTree(['/setup']);
  }
  if (session.isLoggedIn()) {
    return true;
  }
  const queryParams = state.url === '/' ? {} : { volver: state.url };
  return router.createUrlTree(['/login'], { queryParams });
};

/**
 * Pantallas de administración. Quien no es administrador vuelve al inicio; sin sesión se comporta
 * como authGuard.
 */
export const adminGuard: CanActivateFn = (route, state) => {
  const session = inject(SessionStore);
  const result = authGuard(route, state);
  if (result !== true) {
    return result;
  }
  return session.user()?.isAdmin ? true : inject(Router).createUrlTree(['/']);
};

/** /login solo tiene sentido sin sesión. */
export const guestGuard: CanActivateFn = () => {
  const session = inject(SessionStore);
  const router = inject(Router);
  if (session.needsSetup()) {
    return router.createUrlTree(['/setup']);
  }
  return session.isLoggedIn() ? router.createUrlTree(['/']) : true;
};
