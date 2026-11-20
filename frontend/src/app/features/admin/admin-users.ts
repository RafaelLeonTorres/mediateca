import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';

import {
  USER_ROLE_LABELS,
  UsersApi,
  type AdminUser,
  type UserRole,
} from '../../core/admin/users-api';
import { SessionStore } from '../../core/auth/session-store';
import { ConfirmDialog, type ConfirmDialogData } from '../../shared/confirm-dialog';
import { describeError } from '../../shared/describe-error';
import { UserDialog, type UserDialogData } from './user-dialog';
import { UserPasswordDialog, type UserPasswordDialogData } from './user-password-dialog';

/** Administración de usuarios: lista con su rol y sus bibliotecas, alta y edición, contraseña y baja. */
@Component({
  selector: 'app-admin-users',
  imports: [DatePipe, MatButton, MatIcon, MatProgressBar],
  template: `
    <header class="head">
      <div>
        <h1>Usuarios</h1>
        <p class="intro">
          Crea las cuentas de quienes usan Mediateca y elige qué bibliotecas ve cada uno. Los
          administradores ven todas las bibliotecas.
        </p>
      </div>
      <button matButton="filled" type="button" (click)="create()">
        <mat-icon aria-hidden="true">person_add</mat-icon>
        Nuevo usuario
      </button>
    </header>

    @if (notice()) {
      <p class="notice" role="status">{{ notice() }}</p>
    }

    @if (users.isLoading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Cargando usuarios" />
    }

    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }

    @if (users.error()) {
      <p class="error" role="alert">No se pudieron cargar los usuarios.</p>
    } @else {
      <ul class="users">
        @for (user of list(); track user.id) {
          <li class="user">
            <div class="top">
              <div>
                <h2>
                  {{ user.displayName }}
                  @if (isSelf(user)) {
                    <span class="you">(tú)</span>
                  }
                </h2>
                <p class="meta">
                  {{ user.userName }} ·
                  <span class="role" [class.admin]="user.role === 'Admin'">{{
                    roleLabel(user.role)
                  }}</span>
                  · Alta {{ user.createdAt | date: 'dd/MM/yyyy' }}
                </p>
              </div>
              <div class="actions">
                <button
                  matButton="tonal"
                  type="button"
                  [attr.aria-label]="'Editar a ' + user.displayName"
                  (click)="edit(user)"
                >
                  Editar
                </button>
                <button
                  matButton="text"
                  type="button"
                  [attr.aria-label]="'Cambiar la contraseña de ' + user.displayName"
                  (click)="resetPassword(user)"
                >
                  Contraseña
                </button>
                <button
                  matButton="text"
                  type="button"
                  [disabled]="isSelf(user)"
                  [attr.title]="isSelf(user) ? 'No puedes eliminar tu propia cuenta.' : null"
                  [attr.aria-label]="'Eliminar a ' + user.displayName"
                  (click)="remove(user)"
                >
                  Eliminar
                </button>
              </div>
            </div>

            @if (user.role === 'Admin') {
              <p class="libraries">Ve todas las bibliotecas.</p>
            } @else if (libraryNames(user).length === 0) {
              <p class="libraries">Sin bibliotecas: todavía no ve nada.</p>
            } @else {
              <ul class="chips" [attr.aria-label]="'Bibliotecas de ' + user.displayName">
                @for (name of libraryNames(user); track name) {
                  <li>{{ name }}</li>
                }
              </ul>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 24px;
    }

    h1 {
      margin: 0;
      font: var(--mat-sys-headline-medium);
    }

    .intro,
    .meta,
    .libraries {
      margin: 4px 0 0;
      color: var(--mat-sys-on-surface-variant);
    }

    .error {
      color: var(--mat-sys-error);
    }

    .notice {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .users {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .user {
      padding: 20px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container);
    }

    .top {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: 12px;
    }

    .user h2 {
      margin: 0;
      font: var(--mat-sys-title-large);
    }

    .you {
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .role.admin {
      color: var(--mat-sys-primary);
      font-weight: 500;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px;
    }

    .libraries {
      margin-top: 12px;
    }

    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-top: 12px;
    }

    .chips li {
      padding: 4px 12px;
      border-radius: var(--mat-sys-corner-small);
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
      font: var(--mat-sys-label-large);
    }
  `,
})
export class AdminUsers {
  private readonly api = inject(UsersApi);
  private readonly session = inject(SessionStore);
  private readonly dialog = inject(MatDialog);

  protected readonly users = this.api.users();
  protected readonly libraries = this.api.libraryChoices();
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  // value() lanza si el recurso está en error: primero se pregunta si tiene valor.
  protected readonly list = computed(() => (this.users.hasValue() ? (this.users.value() ?? []) : []));
  private readonly libraryList = computed(() =>
    this.libraries.hasValue() ? (this.libraries.value() ?? []) : [],
  );
  private readonly names = computed(
    () =>
      new Map(this.libraryList().map((library): [string, string] => [library.id, library.name])),
  );

  protected roleLabel(role: UserRole): string {
    return USER_ROLE_LABELS[role];
  }

  protected isSelf(user: AdminUser): boolean {
    return user.id === this.session.user()?.id;
  }

  /** Nombres de las bibliotecas de un usuario (las que ya no existen no se muestran). */
  protected libraryNames(user: AdminUser): string[] {
    const names = this.names();
    return user.libraryIds.map((id) => names.get(id)).filter((name): name is string => !!name);
  }

  protected create(): void {
    this.openUserDialog({ libraries: this.libraryList() });
  }

  protected edit(user: AdminUser): void {
    this.openUserDialog({ user, libraries: this.libraryList(), currentUserId: this.session.user()?.id });
  }

  protected resetPassword(user: AdminUser): void {
    this.dialog
      .open<UserPasswordDialog, UserPasswordDialogData, boolean>(UserPasswordDialog, {
        data: { user },
        width: '480px',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((changed) => {
        if (changed) {
          this.error.set(null);
          this.notice.set(
            `Se cambió la contraseña de ${user.displayName}. Sus sesiones abiertas se cerraron.`,
          );
        }
      });
  }

  protected remove(user: AdminUser): void {
    const data: ConfirmDialogData = {
      title: `¿Eliminar a "${user.displayName}"?`,
      message:
        'Se borra su cuenta con su progreso de reproducción y sus permisos, y sus sesiones abiertas se cierran. No se puede deshacer. Tus archivos y bibliotecas no se tocan.',
      confirmLabel: 'Eliminar',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe(async (confirmed) => {
        if (!confirmed) {
          return;
        }
        this.error.set(null);
        this.notice.set(null);
        try {
          await this.api.remove(user.id);
          this.notice.set(`Se eliminó a ${user.displayName}.`);
          this.users.reload();
        } catch (error) {
          this.error.set(describeError(error));
        }
      });
  }

  private openUserDialog(data: UserDialogData): void {
    this.dialog
      .open<UserDialog, UserDialogData, AdminUser>(UserDialog, {
        data,
        width: '560px',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) {
          return;
        }
        this.error.set(null);
        this.notice.set(`Se guardó a ${saved.displayName}.`);
        // Si el administrador se editó a sí mismo, la barra superior muestra su nombre nuevo.
        const me = this.session.user();
        if (me && me.id === saved.id) {
          this.session.updateUser({ ...me, displayName: saved.displayName });
        }
        this.users.reload();
      });
  }
}
