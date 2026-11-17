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
    await firstValueFrom(
      this.http.post<void>('/api/auth/login', { userName, password, rememberMe }),
    );
    await this.refreshUser();
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
    await this.refreshUser();
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
