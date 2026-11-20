import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import type { AdminUser } from '../../core/admin/users-api';
import { UserPasswordDialog } from './user-password-dialog';

const ana: AdminUser = {
  id: 'u-ana',
  userName: 'ana',
  displayName: 'Ana',
  role: 'Usuario',
  libraryIds: [],
  createdAt: '2026-11-10T12:00:00Z',
};

/** Deja correr lo pendiente (promesas y la continuación de un await) antes de seguir. */
const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('UserPasswordDialog', () => {
  function render() {
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [UserPasswordDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { user: ana } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(UserPasswordDialog);
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
    const typeInto = (control: string, value: string) => {
      const field = element.querySelector<HTMLInputElement>(`input[name$=".${control}"]`)!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
    };
    const save = async () => {
      Array.from(element.querySelectorAll<HTMLButtonElement>('button'))
        .find((b) => (b.textContent ?? '').includes('Cambiar contraseña'))!
        .click();
      await settle();
    };

    return { http, close, settle, text, typeInto, save };
  }

  it('dice de quién es la contraseña y que sus sesiones se cierran', () => {
    const { text } = render();

    expect(text()).toContain('Contraseña de Ana');
    expect(text()).toContain('Sus sesiones abiertas se cierran');
  });

  it('no manda contraseñas cortas ni que no coinciden', async () => {
    const { http, typeInto, save, text } = render();

    typeInto('password', '1234');
    typeInto('confirm', '1234');
    await save();
    http.expectNone('/api/admin/users/u-ana/password');
    expect(text()).toContain('La contraseña debe tener al menos 8 caracteres.');

    typeInto('password', 'una-clave-larga');
    typeInto('confirm', 'otra-clave-larga');
    await save();
    http.expectNone('/api/admin/users/u-ana/password');
    expect(text()).toContain('Las contraseñas no coinciden.');
  });

  it('cambia la contraseña con PUT y se cierra con true', async () => {
    const { http, close, typeInto, save, settle } = render();

    typeInto('password', 'clave-nueva-larga');
    typeInto('confirm', 'clave-nueva-larga');
    await save();

    const request = http.expectOne('/api/admin/users/u-ana/password');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ newPassword: 'clave-nueva-larga' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(close).toHaveBeenCalledWith(true);
  });

  it('si la API la rechaza, muestra el motivo y no se cierra', async () => {
    const { http, close, typeInto, save, settle, text } = render();

    typeInto('password', 'clave-nueva-larga');
    typeInto('confirm', 'clave-nueva-larga');
    await save();
    http
      .expectOne('/api/admin/users/u-ana/password')
      .flush({ errors: { newPassword: ['La contraseña debe tener al menos un símbolo.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(text()).toContain('La contraseña debe tener al menos un símbolo.');
    expect(close).not.toHaveBeenCalled();
  });
});
