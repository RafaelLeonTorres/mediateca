import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { USER_ROLE_LABELS, UsersApi, type AdminUser } from './users-api';

const ana: AdminUser = {
  id: 'u1',
  userName: 'ana',
  displayName: 'Ana',
  role: 'Usuario',
  libraryIds: ['l1'],
  createdAt: '2026-11-17T12:00:00Z',
};

describe('UsersApi', () => {
  let api: UsersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(UsersApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lista los usuarios', async () => {
    const resource = TestBed.runInInjectionContext(() => api.users());
    TestBed.tick();

    http.expectOne('/api/admin/users').flush([ana]);
    // La respuesta llega al recurso de forma asíncrona.
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()).toEqual([ana]);
  });

  it('lee las bibliotecas para las casillas desde la administración de bibliotecas', async () => {
    const resource = TestBed.runInInjectionContext(() => api.libraryChoices());
    TestBed.tick();

    http.expectOne('/api/admin/libraries').flush([{ id: 'l1', name: 'Cine', type: 'Movies' }]);
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()).toEqual([{ id: 'l1', name: 'Cine', type: 'Movies' }]);
  });

  it('crea un usuario con POST', async () => {
    const input = {
      userName: 'ana',
      displayName: 'Ana',
      password: 'una-clave-larga',
      role: 'Usuario' as const,
      libraryIds: ['l1'],
    };
    const result = api.create(input);

    const request = http.expectOne('/api/admin/users');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    request.flush(ana);

    expect((await result).id).toBe('u1');
  });

  it('edita con PUT, cambia la contraseña con PUT y elimina con DELETE', async () => {
    const updated = api.update('u1', { displayName: 'Ana M.', role: 'Admin', libraryIds: [] });
    const put = http.expectOne('/api/admin/users/u1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ displayName: 'Ana M.', role: 'Admin', libraryIds: [] });
    put.flush(ana);
    await updated;

    const reset = api.resetPassword('u1', 'clave-nueva-larga');
    const password = http.expectOne('/api/admin/users/u1/password');
    expect(password.request.method).toBe('PUT');
    expect(password.request.body).toEqual({ newPassword: 'clave-nueva-larga' });
    password.flush(null, { status: 204, statusText: 'No Content' });
    await reset;

    const removed = api.remove('u1');
    const del = http.expectOne('/api/admin/users/u1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    await removed;
  });

  it('nombra los roles en español', () => {
    expect(USER_ROLE_LABELS).toEqual({ Admin: 'Administrador', Usuario: 'Usuario' });
  });
});
