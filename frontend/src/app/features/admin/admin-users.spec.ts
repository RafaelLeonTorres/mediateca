import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import type { AdminUser, LibraryChoice } from '../../core/admin/users-api';
import { SessionStore, type User } from '../../core/auth/session-store';
import { UserDialog } from './user-dialog';
import { UserPasswordDialog } from './user-password-dialog';
import { AdminUsers } from './admin-users';

const rafael: AdminUser = {
  id: 'u-admin',
  userName: 'admin',
  displayName: 'Rafael',
  role: 'Admin',
  libraryIds: [],
  createdAt: '2026-11-01T12:00:00Z',
};

const ana: AdminUser = {
  id: 'u-ana',
  userName: 'ana',
  displayName: 'Ana',
  role: 'Usuario',
  libraryIds: ['l-cine', 'l-musica'],
  createdAt: '2026-11-10T12:00:00Z',
};

const beto: AdminUser = {
  id: 'u-beto',
  userName: 'beto',
  displayName: 'Beto',
  role: 'Usuario',
  libraryIds: [],
  createdAt: '2026-11-12T12:00:00Z',
};

const libraries: LibraryChoice[] = [
  { id: 'l-cine', name: 'Cine', type: 'Movies' },
  { id: 'l-musica', name: 'Música', type: 'Music' },
];

const me: User = { id: 'u-admin', userName: 'admin', displayName: 'Rafael', isAdmin: true };

/** Deja correr lo pendiente (promesas y la continuación de un await) antes de seguir. */
const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('AdminUsers', () => {
  function render() {
    // Los diálogos no se abren de verdad: se mira con qué datos se piden y se responde lo que cerraría cada uno.
    const open = vi.fn();
    const updateUser = vi.fn();
    TestBed.configureTestingModule({
      imports: [AdminUsers],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: { open } },
        { provide: SessionStore, useValue: { user: signal<User | null>(me), updateUser } },
      ],
    });
    const fixture = TestBed.createComponent(AdminUsers);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    TestBed.tick();
    const element = fixture.nativeElement as HTMLElement;

    const settle = async () => {
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
    };

    /** Responde la lista de usuarios y la de bibliotecas, y deja que la pantalla se actualice. */
    const load = async (users: AdminUser[]) => {
      http.expectOne('/api/admin/users').flush(users);
      http.expectOne('/api/admin/libraries').flush(libraries);
      await TestBed.inject(ApplicationRef).whenStable();
      await settle();
    };

    /** Lo que responderá el diálogo cuando se cierre. */
    const closeWith = (value: unknown) => open.mockReturnValue({ afterClosed: () => of(value) });

    const text = () => (element.textContent ?? '').replace(/\s+/g, ' ');
    const button = (label: string) => element.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
    const buttonNamed = (name: string) =>
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
        (b.textContent ?? '').includes(name),
      )!;

    return { http, element, open, updateUser, load, settle, closeWith, text, button, buttonNamed };
  }

  it('muestra a cada usuario con su rol y los nombres de sus bibliotecas', async () => {
    const { element, load, text } = render();

    await load([rafael, ana, beto]);

    expect(text()).toContain('Rafael');
    expect(text()).toContain('Administrador');
    // Un administrador ve todo: no se listan bibliotecas.
    expect(text()).toContain('Ve todas las bibliotecas.');
    expect(text()).toContain('Ana');
    const chips = Array.from(element.querySelectorAll('ul[aria-label="Bibliotecas de Ana"] li')).map(
      (li) => li.textContent?.trim(),
    );
    expect(chips).toEqual(['Cine', 'Música']);
    // Sin bibliotecas se dice claro, y no se confunde con "todas".
    expect(text()).toContain('Sin bibliotecas: todavía no ve nada.');
    expect(text()).toContain('10/11/2026');
  });

  it('marca la cuenta propia y no deja eliminarla', async () => {
    const { load, text, button } = render();

    await load([rafael, ana]);

    expect(text()).toContain('(tú)');
    expect(button('Eliminar a Rafael').disabled).toBe(true);
    expect(button('Eliminar a Rafael').getAttribute('title')).toBe('No puedes eliminar tu propia cuenta.');
    expect(button('Eliminar a Ana').disabled).toBe(false);
  });

  it('si no se pueden cargar los usuarios, lo avisa', async () => {
    const { http, settle, text } = render();

    http.expectOne('/api/admin/users').flush(null, { status: 500, statusText: 'Error' });
    http.expectOne('/api/admin/libraries').flush(libraries);
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();

    expect(text()).toContain('No se pudieron cargar los usuarios.');
  });

  it('Nuevo usuario abre el diálogo con las bibliotecas para elegir', async () => {
    const { open, load, closeWith, buttonNamed } = render();
    await load([rafael]);
    closeWith(undefined);

    buttonNamed('Nuevo usuario').click();

    expect(open).toHaveBeenCalledTimes(1);
    const [component, config] = open.mock.calls[0];
    expect(component).toBe(UserDialog);
    expect(config.data).toEqual({ libraries });
  });

  it('Editar pasa el usuario, y al guardar recarga la lista y lo avisa', async () => {
    const { http, open, load, settle, closeWith, text, button } = render();
    await load([rafael, ana]);
    closeWith({ ...ana, displayName: 'Ana María' });

    button('Editar a Ana').click();

    expect(open.mock.calls[0][1].data).toEqual({ user: ana, libraries, currentUserId: 'u-admin' });
    await nextMacrotask();
    TestBed.tick();
    http.expectOne('/api/admin/users').flush([rafael, { ...ana, displayName: 'Ana María' }]);
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();
    expect(text()).toContain('Se guardó a Ana María.');
    expect(text()).toContain('Ana María');
  });

  it('si el administrador se edita a sí mismo, la barra superior muestra su nombre nuevo', async () => {
    const { http, updateUser, load, settle, closeWith, button } = render();
    await load([rafael, ana]);
    closeWith({ ...rafael, displayName: 'Rafa' });

    button('Editar a Rafael').click();

    expect(updateUser).toHaveBeenCalledWith({ ...me, displayName: 'Rafa' });
    await nextMacrotask();
    TestBed.tick();
    http.expectOne('/api/admin/users').flush([{ ...rafael, displayName: 'Rafa' }, ana]);
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();
  });

  it('cancelar el diálogo no recarga ni avisa nada', async () => {
    const { http, load, closeWith, text, button } = render();
    await load([rafael, ana]);
    closeWith(undefined);

    button('Editar a Ana').click();
    await nextMacrotask();
    TestBed.tick();

    http.expectNone('/api/admin/users');
    expect(text()).not.toContain('Se guardó');
  });

  it('Contraseña abre el diálogo del usuario y avisa que sus sesiones se cerraron', async () => {
    const { open, load, settle, closeWith, text, button } = render();
    await load([rafael, ana]);
    closeWith(true);

    button('Cambiar la contraseña de Ana').click();
    await settle();

    const [component, config] = open.mock.calls[0];
    expect(component).toBe(UserPasswordDialog);
    expect(config.data).toEqual({ user: ana });
    expect(text()).toContain('Se cambió la contraseña de Ana. Sus sesiones abiertas se cerraron.');
  });

  it('Eliminar pide confirmación y, si se confirma, borra al usuario y recarga la lista', async () => {
    const { http, open, load, settle, closeWith, text, button } = render();
    await load([rafael, ana]);
    closeWith(true);

    button('Eliminar a Ana').click();

    expect(open.mock.calls[0][1].data.title).toBe('¿Eliminar a "Ana"?');
    const request = http.expectOne('/api/admin/users/u-ana');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await nextMacrotask();
    TestBed.tick();
    http.expectOne('/api/admin/users').flush([rafael]);
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();

    expect(text()).toContain('Se eliminó a Ana.');
    expect(text()).not.toContain('Sin bibliotecas');
  });

  it('si no se confirma, no se borra nada', async () => {
    const { http, load, closeWith, button } = render();
    await load([rafael, ana]);
    closeWith(false);

    button('Eliminar a Ana').click();
    await nextMacrotask();

    http.expectNone('/api/admin/users/u-ana');
  });

  it('si la baja falla, muestra el motivo de la API', async () => {
    const { http, load, settle, closeWith, text, button } = render();
    await load([rafael, ana]);
    closeWith(true);

    button('Eliminar a Ana').click();
    http
      .expectOne('/api/admin/users/u-ana')
      .flush({ title: 'Tiene que quedar al menos un administrador.' }, { status: 409, statusText: 'Conflict' });
    await nextMacrotask();
    await settle();

    expect(text()).toContain('Tiene que quedar al menos un administrador.');
  });
});
