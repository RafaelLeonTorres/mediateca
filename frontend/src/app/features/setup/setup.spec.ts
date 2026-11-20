import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import { SetupPage } from './setup';

/** Deja correr lo pendiente (promesas y la continuación de un await) antes de seguir. */
const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('SetupPage', () => {
  function render() {
    const setup = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      imports: [SetupPage],
      providers: [provideRouter([]), { provide: SessionStore, useValue: { setup } }],
    });
    const fixture = TestBed.createComponent(SetupPage);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const text = () => (element.textContent ?? '').replace(/\s+/g, ' ');
    // Signal Forms le pone a cada campo el nombre "<formulario>.<campo>".
    const typeInto = (control: string, value: string) => {
      const field = element.querySelector<HTMLInputElement>(`input[name$=".${control}"]`)!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
    };
    const send = async () => {
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await nextMacrotask();
      // Dos pasadas: el estado de error de un campo se actualiza durante la primera y se pinta en la segunda.
      fixture.detectChanges();
      fixture.detectChanges();
    };

    return { setup, navigate, text, typeInto, send };
  }

  it('sin datos no crea nada y dice qué falta', async () => {
    const { setup, send, text } = render();

    await send();

    expect(setup).not.toHaveBeenCalled();
    expect(text()).toContain('Escribe tu nombre.');
    expect(text()).toContain('Escribe un nombre de usuario.');
    expect(text()).toContain('La contraseña debe tener al menos 8 caracteres.');
  });

  it('no crea al administrador si las contraseñas no coinciden', async () => {
    const { setup, typeInto, send, text } = render();
    typeInto('displayName', 'Rafael');
    typeInto('userName', 'admin');
    typeInto('password', 'una-clave-larga');
    typeInto('confirm', 'otra-clave-larga');

    await send();

    expect(setup).not.toHaveBeenCalled();
    expect(text()).toContain('Las contraseñas no coinciden.');
  });

  it('crea al administrador con los datos sin espacios de más y entra a la app', async () => {
    const { setup, navigate, typeInto, send } = render();
    typeInto('displayName', '  Rafael ');
    typeInto('userName', ' admin ');
    typeInto('password', 'una-clave-larga');
    typeInto('confirm', 'una-clave-larga');

    await send();

    expect(setup).toHaveBeenCalledWith('Rafael', 'admin', 'una-clave-larga');
    expect(navigate).toHaveBeenCalledWith('/');
  });
});
