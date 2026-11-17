import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SessionStore } from './session-store';

/** Deja correr las promesas pendientes: cada paso de la sesión espera la respuesta anterior. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

const rafael = { id: '1', userName: 'admin', displayName: 'Rafael', isAdmin: true };

describe('SessionStore', () => {
  let store: SessionStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(SessionStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('pide el primer arranque si todavía no hay administrador', async () => {
    const loading = store.load();
    http.expectOne('/api/setup/status').flush({ needsSetup: true });
    await settle();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await loading;

    expect(store.needsSetup()).toBe(true);
    expect(store.isLoggedIn()).toBe(false);
    expect(store.apiError()).toBe(false);
  });

  it('recupera la sesión iniciada', async () => {
    const loading = store.load();
    http.expectOne('/api/setup/status').flush({ needsSetup: false });
    await settle();
    http.expectOne('/api/auth/me').flush(rafael);
    await loading;

    expect(store.user()?.displayName).toBe('Rafael');
  });

  it('marca el error si la API no responde', async () => {
    const loading = store.load();
    http.expectOne('/api/setup/status').error(new ProgressEvent('error'));
    await loading;

    expect(store.apiError()).toBe(true);
  });

  it('inicia sesión y después pide el usuario', async () => {
    const login = store.login('admin', 'una-clave-larga', true);
    const request = http.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({
      userName: 'admin',
      password: 'una-clave-larga',
      rememberMe: true,
    });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    http.expectOne('/api/auth/me').flush(rafael);
    await login;

    expect(store.isLoggedIn()).toBe(true);
  });
});
