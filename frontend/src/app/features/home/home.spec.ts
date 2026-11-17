import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { SessionStore, type User } from '../../core/auth/session-store';
import { Home } from './home';

/** Deja terminar las promesas pendientes (el clic espera el logout y luego navega). */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('Home', () => {
  const admin: User = { id: '1', userName: 'admin', displayName: 'Rafael', isAdmin: true };

  function render(user: User) {
    const logout = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideRouter([]), { provide: SessionStore, useValue: { user: signal(user), logout } }],
    });
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    return { element: fixture.nativeElement as HTMLElement, logout };
  }

  it('saluda con el nombre del usuario', () => {
    const { element } = render(admin);

    expect(element.textContent).toContain('Hola, Rafael');
  });

  it('cierra la sesión y lleva a /login', async () => {
    const { element, logout } = render(admin);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    element.querySelector('button')!.click();
    await settle();

    expect(logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/login');
  });
});
