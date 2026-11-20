import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { SessionStore, type User } from '../../core/auth/session-store';
import { MyAccount } from './my-account';

const ana: User = { id: 'u-ana', userName: 'ana', displayName: 'Ana', isAdmin: false };

/** Deja correr lo pendiente (promesas y la continuación de un await) antes de seguir. */
const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('MyAccount', () => {
  function render(user: User = ana) {
    const updateUser = vi.fn();
    TestBed.configureTestingModule({
      imports: [MyAccount],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: { user: signal<User | null>(user), updateUser } },
      ],
    });
    const fixture = TestBed.createComponent(MyAccount);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    TestBed.tick();
    const element = fixture.nativeElement as HTMLElement;

    const settle = async () => {
      await nextMacrotask();
      TestBed.tick();
      // Dos pasadas: el estado de error de un campo se actualiza durante la primera y se pinta en la segunda.
      fixture.detectChanges();
      fixture.detectChanges();
    };
    const text = () => (element.textContent ?? '').replace(/\s+/g, ' ');
    // Signal Forms le pone a cada campo el nombre "<formulario>.<campo>".
    const input = (control: string) =>
      element.querySelector<HTMLInputElement>(`input[name$=".${control}"]`)!;
    const typeInto = (control: string, value: string) => {
      const field = input(control);
      field.value = value;
      field.dispatchEvent(new Event('input'));
    };
    /** Envía el formulario que contiene ese campo (como al pulsar su botón). */
    const submit = async (control: string) => {
      input(control).closest('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
    };

    return { http, element, updateUser, settle, text, input, typeInto, submit };
  }

  it('muestra el usuario, su rol y su nombre visible', () => {
    const { input, text } = render();

    expect(text()).toContain('Mi cuenta');
    expect(text()).toContain('Usuario: ana');
    expect(input('displayName').value).toBe('Ana');
  });

  it('a un administrador lo identifica como tal', () => {
    const { text } = render({ ...ana, userName: 'admin', displayName: 'Rafael', isAdmin: true });

    expect(text()).toContain('Administrador');
  });

  it('cambia el nombre visible y actualiza la sesión', async () => {
    const { http, updateUser, typeInto, submit, settle, text } = render();

    typeInto('displayName', '  Ana María ');
    await submit('displayName');

    const request = http.expectOne('/api/me/account');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ displayName: 'Ana María' });
    const updated = { ...ana, displayName: 'Ana María' };
    request.flush(updated);
    await settle();

    expect(updateUser).toHaveBeenCalledWith(updated);
    expect(text()).toContain('Se guardó tu nombre.');
  });

  it('un nombre vacío o de puros espacios no se manda', async () => {
    const { http, typeInto, submit, text } = render();

    typeInto('displayName', '   ');
    await submit('displayName');

    http.expectNone('/api/me/account');
    expect(text()).toContain('Escribe un nombre de hasta 100 caracteres.');
  });

  it('no manda la contraseña si falta algo, es corta o no coincide', async () => {
    const { http, typeInto, submit, text } = render();

    await submit('current');
    http.expectNone('/api/me/password');
    expect(text()).toContain('Escribe tu contraseña actual.');

    typeInto('current', 'la-de-antes');
    typeInto('next', '1234');
    typeInto('confirm', '1234');
    await submit('current');
    http.expectNone('/api/me/password');
    expect(text()).toContain('La contraseña debe tener al menos 8 caracteres.');

    typeInto('next', 'clave-nueva-larga');
    typeInto('confirm', 'otra-clave-larga');
    await submit('current');
    http.expectNone('/api/me/password');
    expect(text()).toContain('Las contraseñas no coinciden.');
  });

  it('cambia la contraseña, avisa que las demás sesiones se cerraron y limpia el formulario', async () => {
    const { http, element, input, typeInto, submit, settle, text } = render();

    typeInto('current', 'la-de-antes');
    typeInto('next', 'clave-nueva-larga');
    typeInto('confirm', 'clave-nueva-larga');
    await submit('current');

    const request = http.expectOne('/api/me/password');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ currentPassword: 'la-de-antes', newPassword: 'clave-nueva-larga' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(text()).toContain('Se cambió tu contraseña. Las demás sesiones abiertas se cerraron.');
    expect(input('current').value).toBe('');
    expect(input('next').value).toBe('');
    expect(input('confirm').value).toBe('');
    // Los campos vacíos quedan como al principio, sin errores en rojo.
    expect(text()).not.toContain('Escribe tu contraseña actual.');
    expect(text()).not.toContain('La contraseña debe tener al menos 8 caracteres.');
    expect(element.querySelector('.mat-form-field-invalid')).toBeNull();
  });

  it('con la contraseña actual equivocada muestra el motivo y conserva lo escrito', async () => {
    const { http, input, typeInto, submit, settle, text } = render();

    typeInto('current', 'no-es-la-clave');
    typeInto('next', 'clave-nueva-larga');
    typeInto('confirm', 'clave-nueva-larga');
    await submit('current');
    http.expectOne('/api/me/password').flush(
      { errors: { currentPassword: ['La contraseña no es correcta.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(text()).toContain('La contraseña no es correcta.');
    expect(text()).not.toContain('Se cambió tu contraseña');
    expect(input('next').value).toBe('clave-nueva-larga');
  });
});
