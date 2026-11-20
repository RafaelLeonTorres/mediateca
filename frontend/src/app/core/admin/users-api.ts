import { HttpClient, HttpResourceRef, httpResource } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { LibraryType } from '../libraries/libraries-api';

/** Rol de una cuenta, tal como lo entrega la API. El administrador ve todas las bibliotecas. */
export type UserRole = 'Admin' | 'Usuario';

export const USER_ROLE_LABELS: Record<UserRole, string> = {
  Admin: 'Administrador',
  Usuario: 'Usuario',
};

/** Usuario visto por el administrador: GET /api/admin/users. */
export interface AdminUser {
  id: string;
  userName: string;
  displayName: string;
  role: UserRole;
  /** Bibliotecas que puede ver. En un administrador siempre va vacía: ve todas sin necesitar permisos. */
  libraryIds: string[];
  createdAt: string;
}

/** Cuerpo para editar un usuario. El nombre de usuario no cambia. */
export interface UserUpdate {
  displayName: string;
  role: UserRole;
  /** Con el rol Admin se ignoran (y se borran las que tuviera). */
  libraryIds: string[];
}

/** Cuerpo para crear un usuario. */
export interface UserCreate extends UserUpdate {
  userName: string;
  /** Contraseña inicial que le da el administrador. */
  password: string;
}

/** Una biblioteca para elegir en las casillas de permisos. */
export interface LibraryChoice {
  id: string;
  name: string;
  type: LibraryType;
}

/** Administración de usuarios: solo para administradores. */
@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  /** Usuarios con su rol y sus bibliotecas. Se actualiza con reload(). */
  users(): HttpResourceRef<AdminUser[] | undefined> {
    return httpResource<AdminUser[]>(() => '/api/admin/users');
  }

  /** Bibliotecas que existen, para las casillas. La administración de bibliotecas las entrega con más datos; aquí solo se leen estos. */
  libraryChoices(): HttpResourceRef<LibraryChoice[] | undefined> {
    return httpResource<LibraryChoice[]>(() => '/api/admin/libraries');
  }

  create(input: UserCreate): Promise<AdminUser> {
    return firstValueFrom(this.http.post<AdminUser>('/api/admin/users', input));
  }

  update(id: string, input: UserUpdate): Promise<AdminUser> {
    return firstValueFrom(this.http.put<AdminUser>(`/api/admin/users/${id}`, input));
  }

  /** Pone una contraseña nueva a un usuario y cierra sus sesiones abiertas. */
  resetPassword(id: string, newPassword: string): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/admin/users/${id}/password`, { newPassword }));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/admin/users/${id}`));
  }
}
