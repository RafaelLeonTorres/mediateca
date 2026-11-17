import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withFetch, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { SessionStore } from './core/auth/session-store';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Angular lee la cookie XSRF-TOKEN y la manda en la cabecera X-XSRF-TOKEN de cada POST.
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    ),
    provideRouter(routes, withComponentInputBinding()),
    // Antes de la primera pantalla: ¿falta el primer arranque? ¿hay una sesión iniciada?
    provideAppInitializer(() => inject(SessionStore).load()),
  ],
};
