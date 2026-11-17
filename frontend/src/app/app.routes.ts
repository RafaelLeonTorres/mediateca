import { Routes } from '@angular/router';

import { adminGuard, authGuard, guestGuard, setupPendingGuard } from './core/auth/guards';

export const routes: Routes = [
  {
    path: 'setup',
    title: 'Configurar Mediateca',
    canActivate: [setupPendingGuard],
    loadComponent: () => import('./features/setup/setup').then((m) => m.SetupPage),
  },
  {
    path: 'login',
    title: 'Iniciar sesión · Mediateca',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login').then((m) => m.LoginPage),
  },
  {
    // El reproductor de video ocupa toda la pantalla, sin la barra superior; pide sesión como el resto.
    path: 'reproducir/:id',
    title: 'Reproducir · Mediateca',
    canActivate: [authGuard],
    loadComponent: () => import('./features/player/player-page').then((m) => m.PlayerPage),
  },
  {
    // Todas las pantallas con sesión comparten la barra superior.
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shared/layout/app-layout').then((m) => m.AppLayout),
    children: [
      {
        path: '',
        title: 'Mediateca',
        pathMatch: 'full',
        loadComponent: () => import('./features/home/home').then((m) => m.Home),
      },
      {
        path: 'bibliotecas/:id',
        title: 'Biblioteca · Mediateca',
        loadComponent: () => import('./features/library/library-page').then((m) => m.LibraryPage),
      },
      {
        path: 'items/:id',
        title: 'Detalle · Mediateca',
        loadComponent: () => import('./features/item/item-page').then((m) => m.ItemPage),
      },
      {
        path: 'admin/bibliotecas',
        title: 'Administrar bibliotecas · Mediateca',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/admin-libraries').then((m) => m.AdminLibraries),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
