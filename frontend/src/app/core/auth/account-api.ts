import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { User } from './session-store';

/** Mi cuenta: cualquier usuario con sesión cambia su nombre visible y su contraseña. */
@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  /** Cambia el nombre visible. Devuelve al usuario como queda. */
  updateName(displayName: string): Promise<User> {
    return firstValueFrom(this.http.put<User>('/api/me/account', { displayName }));
  }

  /**
   * Cambia la contraseña con la actual y la nueva. La sesión desde la que se cambia sigue; las demás se
   * cierran. Con la actual equivocada responde 400 (no 401: la sesión es buena).
   */
  changePassword(currentPassword: string, newPassword: string): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/me/password', { currentPassword, newPassword }));
  }
}
