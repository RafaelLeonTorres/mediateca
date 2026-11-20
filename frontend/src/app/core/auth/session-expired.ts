import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { SessionStore } from './session-store';

/** /login?sesion=cerrada: la pantalla de inicio de sesión explica que la sesión se cerró. */
export const SESSION_CLOSED_PARAM = 'sesion';
export const SESSION_CLOSED_VALUE = 'cerrada';

/**
 * Llamadas donde un 401 es lo esperado (iniciar sesión, preguntar quién soy) y no una sesión cerrada.
 * Todo lo que no es de la API tampoco cuenta.
 */
function isSessionCall(url: string): boolean {
  return !url.startsWith('/api/') || url.startsWith('/api/auth/') || url.startsWith('/api/setup');
}

/**
 * Si la API responde 401 mientras hay una sesión iniciada, la sesión se cerró desde el servidor (otro
 * administrador cambió su rol o su contraseña, o borró la cuenta): se limpia el usuario y se lleva a /login,
 * que avisa que la sesión se cerró y después regresa a la pantalla donde estaba. Un error de validación es un
 * 400, no un 401, así que equivocarse de contraseña actual en Mi cuenta no cierra la sesión.
 */
export const sessionExpiredInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionStore);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        session.isLoggedIn() &&
        !isSessionCall(request.url)
      ) {
        const volver = router.url;
        session.expire();
        void router.navigate(['/login'], {
          queryParams: {
            [SESSION_CLOSED_PARAM]: SESSION_CLOSED_VALUE,
            ...(volver === '/' || volver.startsWith('/login') ? {} : { volver }),
          },
        });
      }
      return throwError(() => error);
    }),
  );
};
