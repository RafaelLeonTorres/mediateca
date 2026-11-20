import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { LoginPage } from './login';

describe('LoginPage', () => {
  function render(queryParams: Record<string, string> = {}) {
    const login = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: { user: signal(null), login } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
      ],
    });
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const text = () => (element.textContent ?? '').replace(/\s+/g, ' ');
    return { fixture, element, login, text };
  }

  it('si la sesión se cerró desde el servidor, lo avisa', () => {
    const { element, text } = render({ sesion: 'cerrada', volver: '/admin/usuarios' });

    expect(text()).toContain('Tu sesión se cerró. Vuelve a iniciar sesión para continuar.');
    expect(element.querySelector('.notice')?.getAttribute('role')).toBe('status');
  });

  it('al entrar normalmente no muestra el aviso', () => {
    const { element } = render();

    expect(element.querySelector('.notice')).toBeNull();
  });

  it('después de iniciar sesión regresa a la pantalla donde estaba', async () => {
    const { element, login } = render({ sesion: 'cerrada', volver: '/admin/usuarios' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const [userName, password] = Array.from(element.querySelectorAll<HTMLInputElement>('input'));
    userName.value = ' ana ';
    userName.dispatchEvent(new Event('input'));
    password.value = 'una-clave-larga';
    password.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    // Deja correr el inicio de sesión (una promesa) y la navegación de después.
    await new Promise<void>((resolve) => setTimeout(resolve));

    expect(login).toHaveBeenCalledWith('ana', 'una-clave-larga', true);
    expect(navigate).toHaveBeenCalledWith('/admin/usuarios');
  });
});
