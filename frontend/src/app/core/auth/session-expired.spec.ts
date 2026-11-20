import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { sessionExpiredInterceptor } from './session-expired';
import { SessionStore } from './session-store';

const rafael = { id: '1', userName: 'admin', displayName: 'Rafael', isAdmin: true };

describe('sessionExpiredInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let session: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([sessionExpiredInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionStore);
    session.user.set(rafael);
  });

  afterEach(() => controller.verify());

  /** El componente no navega de verdad: solo se mira a dónde mandaría. */
  const spyNavigate = () => vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

  /** Hace la llamada, la responde con ese estado y devuelve el estado del error que le llega a quien llamó. */
  async function respond(url: string, status: number): Promise<number | undefined> {
    const result = new Promise<number | undefined>((resolve) => {
      http.get(url).subscribe({ next: () => resolve(undefined), error: (e) => resolve(e.status) });
    });
    controller.expectOne(url).flush(null, { status, statusText: 'x' });
    return result;
  }

  /** Al cerrarse la sesión se pide un token XSRF nuevo; sin sesión, /api/auth/me responde 401. */
  const answerXsrfRefresh = () =>
    controller.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });

  it('un 401 de la API con la sesión iniciada limpia al usuario y lleva a /login avisando', async () => {
    const navigate = spyNavigate();
    const status = await respond('/api/libraries', 401);

    // El error sigue llegando a quien llamó.
    expect(status).toBe(401);
    expect(session.isLoggedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { sesion: 'cerrada' } });
    answerXsrfRefresh();
  });

  it('recuerda la pantalla donde estaba para volver después de iniciar sesión', async () => {
    const navigate = spyNavigate();
    Object.defineProperty(TestBed.inject(Router), 'url', { get: () => '/admin/usuarios' });

    await respond('/api/admin/users', 401);

    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { sesion: 'cerrada', volver: '/admin/usuarios' },
    });
    answerXsrfRefresh();
  });

  it('varios 401 a la vez cierran la sesión una sola vez', async () => {
    const navigate = spyNavigate();
    const fail = (url: string) =>
      new Promise<void>((resolve) => http.get(url).subscribe({ error: () => resolve() }));
    const first = fail('/api/libraries');
    const second = fail('/api/me/recently-added');
    controller.expectOne('/api/libraries').flush(null, { status: 401, statusText: 'x' });
    controller.expectOne('/api/me/recently-added').flush(null, { status: 401, statusText: 'x' });
    await Promise.all([first, second]);

    expect(navigate).toHaveBeenCalledTimes(1);
    // Un solo pedido de token: expectOne falla si hubiera dos.
    answerXsrfRefresh();
  });

  it('un 400 (por ejemplo, la contraseña actual equivocada) no cierra la sesión', async () => {
    const navigate = spyNavigate();
    await respond('/api/me/password', 400);

    expect(session.isLoggedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('un 401 al iniciar sesión o preguntar quién soy es lo esperado y no hace nada', async () => {
    const navigate = spyNavigate();
    await respond('/api/auth/login', 401);
    await respond('/api/auth/me', 401);

    expect(session.isLoggedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('sin sesión iniciada no navega', async () => {
    const navigate = spyNavigate();
    session.user.set(null);

    await respond('/api/libraries', 401);

    expect(navigate).not.toHaveBeenCalled();
  });
});
