import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AccountApi } from './account-api';

describe('AccountApi', () => {
  let api: AccountApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(AccountApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('cambia el nombre visible con PUT y devuelve al usuario', async () => {
    const result = api.updateName('Ana María');

    const request = http.expectOne('/api/me/account');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ displayName: 'Ana María' });
    request.flush({ id: 'u1', userName: 'ana', displayName: 'Ana María', isAdmin: false });

    expect((await result).displayName).toBe('Ana María');
  });

  it('cambia la contraseña con la actual y la nueva', async () => {
    const result = api.changePassword('la-de-antes', 'la-de-ahora');

    const request = http.expectOne('/api/me/password');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ currentPassword: 'la-de-antes', newPassword: 'la-de-ahora' });
    request.flush(null, { status: 204, statusText: 'No Content' });

    await result;
  });
});
