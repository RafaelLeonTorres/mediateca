import { Routes } from '@angular/router';

import { authGuard, guestGuard, setupPendingGuard } from './core/auth/guards';

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
    path: '',
    title: 'Mediateca',
    canActivate: [authGuard],
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
  },
  { path: '**', redirectTo: '' },
];
