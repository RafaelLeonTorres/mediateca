import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { SessionStore, type User } from '../../core/auth/session-store';
import { AppLayout } from './app-layout';

/** Deja terminar las promesas pendientes (el clic espera el logout y luego navega). */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('AppLayout', () => {
  function render(user: User) {
    const logout = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      imports: [AppLayout],
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: { user: signal(user), logout } },
      ],
    });
    const fixture = TestBed.createComponent(AppLayout);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, logout };
  }

  it('el administrador ve el enlace a la administración', () => {
    const { element } = render({ id: '1', userName: 'a', displayName: 'Rafael', isAdmin: true });

    expect(element.querySelector('a[href="/admin/bibliotecas"]')).not.toBeNull();
  });

  it('un usuario normal no ve el enlace a la administración', () => {
    const { element } = render({ id: '2', userName: 'b', displayName: 'Ana', isAdmin: false });

    expect(element.querySelector('a[href="/admin/bibliotecas"]')).toBeNull();
    expect(element.querySelector('.brand')!.textContent).toContain('Mediateca');
  });

  it('cierra la sesión desde el menú de usuario y lleva a /login', async () => {
    const { fixture, element, logout } = render({
      id: '1',
      userName: 'a',
      displayName: 'Rafael',
      isAdmin: true,
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    element.querySelector<HTMLButtonElement>('button[aria-label^="Menú de"]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    // El menú se pinta en una capa aparte, fuera del componente.
    const item = [...document.querySelectorAll<HTMLButtonElement>('button[mat-menu-item]')].find(
      (b) => b.textContent?.includes('Cerrar sesión'),
    );
    item!.click();
    await settle();

    expect(logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/login');
  });
});
