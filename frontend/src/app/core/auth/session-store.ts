import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Usuario de la sesión, como lo entrega GET /api/auth/me. */
export interface User {
  id: string;
  userName: string;
  displayName: string;
  isAdmin: boolean;
}

interface SetupStatus {
  needsSetup: boolean;
}

/**
 * Estado de la sesión: si falta el primer arranque y quién inició sesión.
 *
 * Cada GET a /api/setup/status y /api/auth/me deja el token XSRF en una cookie, y Angular lo
 * manda solo en la cabecera X-XSRF-TOKEN de cada POST. El token va ligado al usuario, por eso
 * después de iniciar o cerrar sesión se vuelve a pedir /api/auth/me.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly http = inject(HttpClient);

  readonly needsSetup = signal(false);
  readonly user = signal<User | null>(null);
  readonly isLoggedIn = computed(() => this.user() !== null);
  /** La API no respondió al abrir la app. */
  readonly apiError = signal(false);

  /** El token XSRF nuevo que se pidió cuando la sesión se cerró desde el servidor (ver expire). */
  private xsrfRefresh: Promise<void> | null = null;

  /** Se llama al arrancar la app, antes de la primera pantalla. */
  async load(): Promise<void> {
    try {
      const status = await firstValueFrom(this.http.get<SetupStatus>('/api/setup/status'));
      this.needsSetup.set(status.needsSetup);
      await this.refreshUser();
      this.apiError.set(false);
    } catch {
      this.apiError.set(true);
    }
  }

  async setup(displayName: string, userName: string, password: string): Promise<void> {
    await firstValueFrom(this.http.post<User>('/api/setup', { displayName, userName, password }));
    this.needsSetup.set(false);
    await this.refreshUser();
  }

  async login(userName: string, password: string, rememberMe: boolean): Promise<void> {
    // Si la sesión se acaba de cerrar, primero tiene que llegar el token XSRF sin usuario.
    if (this.xsrfRefresh) {
      await this.xsrfRefresh;
      this.xsrfRefresh = null;
    }
    await firstValueFrom(
      this.http.post<void>('/api/auth/login', { userName, password, rememberMe }),
    );
    await this.refreshUser();
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
    await this.refreshUser();
  }

  /** Pone al usuario de la sesión tal como lo devolvió la API (por ejemplo, con su nombre visible nuevo). */
  updateUser(user: User): void {
    this.user.set(user);
  }

  /**
   * La API rechazó la sesión (401): se cerró desde el servidor. Sin usuario, los guards mandan a /login.
   *
   * El token XSRF de la cookie va ligado al usuario que ya no tiene sesión, así que la API rechazaría con él
   * el siguiente inicio de sesión ("Falta el token XSRF o no es válido"). Por eso se pide uno nuevo a
   * /api/auth/me, que sin sesión responde 401 pero lo entrega igual.
   */
  expire(): void {
    this.user.set(null);
    // Sin conexión no hay token nuevo; el inicio de sesión mostrará el error que corresponda.
    this.xsrfRefresh = this.refreshUser().catch(() => undefined);
  }

  /** Pide el usuario actual y un token XSRF nuevo. Sin sesión, la API responde 401. */
  private async refreshUser(): Promise<void> {
    try {
      this.user.set(await firstValueFrom(this.http.get<User>('/api/auth/me')));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.user.set(null);
        return;
      }
      throw error;
    }
  }
}
