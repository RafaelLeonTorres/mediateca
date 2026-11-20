import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import type { AdminUser, LibraryChoice } from '../../core/admin/users-api';
import { UserDialog, type UserDialogData } from './user-dialog';

const libraries: LibraryChoice[] = [
  { id: 'l-cine', name: 'Cine', type: 'Movies' },
  { id: 'l-musica', name: 'Música', type: 'Music' },
];

const ana: AdminUser = {
  id: 'u-ana',
  userName: 'ana',
  displayName: 'Ana',
  role: 'Usuario',
  libraryIds: ['l-musica'],
  createdAt: '2026-11-10T12:00:00Z',
};

/** Deja correr lo pendiente (promesas y la continuación de un await) antes de seguir. */
const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('UserDialog', () => {
  function render(data: UserDialogData) {
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [UserDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(UserDialog);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    TestBed.tick();
    const element = fixture.nativeElement as HTMLElement;

    // Sin whenStable: mientras hay una petición pendiente, la app nunca queda estable.
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
    const selectRole = async (role: string) => {
      const select = element.querySelector<HTMLSelectElement>('select')!;
      select.value = role;
      // Como en el navegador: al elegir una opción, un select dispara input y change.
      select.dispatchEvent(new Event('input', { bubbles: true }));
      select.dispatchEvent(new Event('change', { bubbles: true }));
      await settle();
    };
    const checkboxes = () =>
      Array.from(element.querySelectorAll<HTMLInputElement>('mat-checkbox input[type="checkbox"]'));
    const save = async () => {
      Array.from(element.querySelectorAll<HTMLButtonElement>('button'))
        .find((b) => (b.textContent ?? '').includes('Guardar'))!
        .click();
      await settle();
    };

    return { http, element, close, settle, text, input, typeInto, selectRole, checkboxes, save };
  }

  it('al crear pide usuario, nombre, contraseña y rol, y lista las bibliotecas sin marcar', () => {
    const { element, input, checkboxes } = render({ libraries });

    expect(element.querySelector('h2')?.textContent).toContain('Nuevo usuario');
    expect(input('userName')).not.toBeNull();
    expect(input('displayName')).not.toBeNull();
    expect(input('password')).not.toBeNull();
    expect(element.querySelector('select')?.value).toBe('Usuario');
    expect(checkboxes().map((box) => box.checked)).toEqual([false, false]);
    expect(element.textContent).toContain('Cine');
    expect(element.textContent).toContain('Música');
  });

  it('sin datos no guarda: muestra qué falta', async () => {
    const { http, close, save, text } = render({ libraries });

    await save();

    http.expectNone('/api/admin/users');
    expect(close).not.toHaveBeenCalled();
    expect(text()).toContain('Escribe un nombre de usuario de hasta 50 caracteres.');
    expect(text()).toContain('Escribe un nombre de hasta 100 caracteres.');
    expect(text()).toContain('La contraseña debe tener al menos 8 caracteres.');
  });

  it('una contraseña corta no se manda', async () => {
    const { http, typeInto, save } = render({ libraries });

    typeInto('userName', 'ana');
    typeInto('displayName', 'Ana');
    typeInto('password', '1234');
    await save();

    http.expectNone('/api/admin/users');
  });

  it('crea al usuario con las bibliotecas marcadas y se cierra con el usuario creado', async () => {
    const { http, close, typeInto, checkboxes, save, settle } = render({ libraries });
    const created: AdminUser = { ...ana, libraryIds: ['l-cine'] };

    typeInto('userName', ' ana ');
    typeInto('displayName', ' Ana ');
    typeInto('password', 'una-clave-larga');
    checkboxes()[0].click();
    await settle();
    await save();

    const request = http.expectOne('/api/admin/users');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      userName: 'ana',
      displayName: 'Ana',
      password: 'una-clave-larga',
      role: 'Usuario',
      libraryIds: ['l-cine'],
    });
    request.flush(created);
    await settle();

    expect(close).toHaveBeenCalledWith(created);
  });

  it('con el rol Administrador las casillas se deshabilitan, se explica por qué y no se mandan bibliotecas', async () => {
    const { http, element, typeInto, checkboxes, selectRole, save, settle, text } = render({ libraries });
    typeInto('userName', 'jefa');
    typeInto('displayName', 'Jefa');
    typeInto('password', 'una-clave-larga');
    checkboxes()[1].click();
    await settle();

    await selectRole('Admin');

    expect(checkboxes().every((box) => box.disabled)).toBe(true);
    expect(checkboxes().every((box) => box.checked)).toBe(true);
    expect(text()).toContain('Un administrador ve todas las bibliotecas, así que aquí no se le asigna ninguna.');
    expect(element.querySelector('fieldset')?.getAttribute('aria-describedby')).toBe('libraries-help');

    await save();
    const request = http.expectOne('/api/admin/users');
    expect(request.request.body).toMatchObject({ role: 'Admin', libraryIds: [] });
    request.flush({ ...ana, role: 'Admin', libraryIds: [] });

    // Al volver a Usuario, la casilla que se había marcado sigue marcada.
    await selectRole('Usuario');
    expect(checkboxes().map((box) => box.checked)).toEqual([false, true]);
    expect(checkboxes().every((box) => !box.disabled)).toBe(true);
  });

  it('al editar muestra el usuario fijo, sus datos y sus bibliotecas, sin pedir contraseña', () => {
    const { element, input, checkboxes, text } = render({ user: ana, libraries, currentUserId: 'otro' });

    expect(element.querySelector('h2')?.textContent).toContain('Editar usuario');
    expect(text()).toContain('Usuario: ana');
    expect(input('userName')).toBeNull();
    expect(input('password')).toBeNull();
    expect(input('displayName').value).toBe('Ana');
    expect(checkboxes().map((box) => box.checked)).toEqual([false, true]);
  });

  it('al editar manda solo nombre, rol y bibliotecas', async () => {
    const { http, close, typeInto, checkboxes, save, settle } = render({
      user: ana,
      libraries,
      currentUserId: 'otro',
    });

    typeInto('displayName', 'Ana María');
    checkboxes()[0].click();
    await settle();
    await save();

    const request = http.expectOne('/api/admin/users/u-ana');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      displayName: 'Ana María',
      role: 'Usuario',
      libraryIds: ['l-cine', 'l-musica'],
    });
    const updated = { ...ana, displayName: 'Ana María', libraryIds: ['l-cine', 'l-musica'] };
    request.flush(updated);
    await settle();

    expect(close).toHaveBeenCalledWith(updated);
  });

  it('el administrador no puede cambiarse su propio rol', () => {
    const rafael: AdminUser = { ...ana, id: 'u-admin', userName: 'admin', role: 'Admin', libraryIds: [] };

    const { element, text } = render({ user: rafael, libraries, currentUserId: 'u-admin' });

    expect(element.querySelector('select')?.disabled).toBe(true);
    expect(text()).toContain('No puedes cambiar tu propio rol.');
  });

  it('si la API rechaza el alta, muestra el motivo y no se cierra', async () => {
    const { http, close, typeInto, save, settle, text } = render({ libraries });
    typeInto('userName', 'ana');
    typeInto('displayName', 'Ana');
    typeInto('password', 'una-clave-larga');

    await save();
    http.expectOne('/api/admin/users').flush(
      { title: 'One or more validation errors occurred.', errors: { userName: ["Ya existe el usuario 'ana'."] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(text()).toContain("Ya existe el usuario 'ana'.");
    expect(close).not.toHaveBeenCalled();
  });

  it('sin bibliotecas en el servidor, lo explica', () => {
    const { text } = render({ libraries: [] });

    expect(text()).toContain('Todavía no hay bibliotecas.');
  });
});
