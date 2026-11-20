import { Component, computed, inject, signal } from '@angular/core';
import {
  FormField,
  disabled,
  form,
  maxLength,
  minLength,
  pattern,
  required,
  submit,
} from '@angular/forms/signals';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import {
  MAT_DIALOG_DATA,
  MatDialogActions,
  MatDialogContent,
  MatDialogRef,
  MatDialogTitle,
} from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';

import {
  USER_ROLE_LABELS,
  UsersApi,
  type AdminUser,
  type LibraryChoice,
  type UserRole,
} from '../../core/admin/users-api';
import { describeError } from '../../shared/describe-error';
import { MIN_PASSWORD_LENGTH } from '../../shared/password-fields';

export interface UserDialogData {
  /** Usuario que se edita; sin él, el diálogo crea uno nuevo. */
  user?: AdminUser;
  /** Bibliotecas entre las que se eligen los permisos. */
  libraries: LibraryChoice[];
  /** Quién está editando: no puede cambiarse el rol a sí mismo. */
  currentUserId?: string;
}

interface UserModel {
  userName: string;
  displayName: string;
  password: string;
  role: UserRole;
}

/** Con al menos un carácter que no sea espacio. */
const NOT_BLANK = /\S/;

/** Crear o editar un usuario: nombre, rol y casillas de bibliotecas (no cuentan para un administrador). */
@Component({
  selector: 'app-user-dialog',
  imports: [
    FormField,
    MatButton,
    MatCheckbox,
    MatDialogActions,
    MatDialogContent,
    MatDialogTitle,
    MatError,
    MatFormField,
    MatHint,
    MatInput,
    MatLabel,
  ],
  template: `
    <h2 mat-dialog-title>{{ editing ? 'Editar usuario' : 'Nuevo usuario' }}</h2>

    <mat-dialog-content>
      <form class="form" novalidate (submit)="$event.preventDefault(); save()">
        @if (editing) {
          <p class="fixed">
            Usuario: <strong>{{ data.user?.userName }}</strong>
          </p>
        } @else {
          <mat-form-field appearance="outline">
            <mat-label>Usuario</mat-label>
            <input
              matInput
              [formField]="userForm.userName"
              autocomplete="off"
              autocapitalize="none"
            />
            <mat-hint>Con él inicia sesión. No se puede cambiar después.</mat-hint>
            <mat-error>Escribe un nombre de usuario de hasta 50 caracteres.</mat-error>
          </mat-form-field>
        }

        <mat-form-field appearance="outline">
          <mat-label>Nombre visible</mat-label>
          <input matInput [formField]="userForm.displayName" autocomplete="off" />
          <mat-error>Escribe un nombre de hasta 100 caracteres.</mat-error>
        </mat-form-field>

        @if (!editing) {
          <mat-form-field appearance="outline">
            <mat-label>Contraseña inicial</mat-label>
            <input
              matInput
              type="password"
              [formField]="userForm.password"
              autocomplete="new-password"
            />
            <mat-hint>Al menos {{ minPassword }} caracteres. Díselo a la persona: puede cambiarla en Mi cuenta.</mat-hint>
            <mat-error>La contraseña debe tener al menos {{ minPassword }} caracteres.</mat-error>
          </mat-form-field>
        }

        <mat-form-field appearance="outline">
          <mat-label>Rol</mat-label>
          <select matNativeControl [formField]="userForm.role">
            @for (option of roleOptions; track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
          @if (isSelf) {
            <mat-hint>No puedes cambiar tu propio rol.</mat-hint>
          }
        </mat-form-field>
      </form>

      <fieldset class="libraries" aria-describedby="libraries-help">
        <legend>Bibliotecas</legend>
        <p id="libraries-help" class="help">
          @if (isAdminRole()) {
            Un administrador ve todas las bibliotecas, así que aquí no se le asigna ninguna.
          } @else {
            Marca las bibliotecas que puede ver. Sin ninguna, no verá contenido.
          }
        </p>
        @for (library of data.libraries; track library.id) {
          <mat-checkbox
            [checked]="isAdminRole() || selected().has(library.id)"
            [disabled]="isAdminRole()"
            (change)="toggle(library.id, $event.checked)"
          >
            {{ library.name }}
          </mat-checkbox>
        } @empty {
          <p class="help">Todavía no hay bibliotecas. Crea una en Administrar › Bibliotecas.</p>
        }
      </fieldset>

      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton="text" type="button" (click)="dialogRef.close()">Cancelar</button>
      <button matButton="filled" type="button" [disabled]="saving()" (click)="save()">
        Guardar
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    /* El espacio entre campos separa la ayuda de un campo de la etiqueta flotante del siguiente. */
    .form {
      display: flex;
      flex-direction: column;
      gap: 4px;
      min-width: min(480px, 80vw);
    }

    .fixed {
      margin: 0 0 16px;
    }

    .libraries {
      margin: 0;
      padding: 12px 16px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--mat-sys-corner-medium);
    }

    legend {
      padding: 0 8px;
      font: var(--mat-sys-title-small);
    }

    .libraries mat-checkbox {
      display: block;
    }

    .help {
      margin: 0 0 8px;
      color: var(--mat-sys-on-surface-variant);
    }

    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class UserDialog {
  protected readonly dialogRef = inject<MatDialogRef<UserDialog, AdminUser>>(MatDialogRef);
  protected readonly data = inject<UserDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(UsersApi);

  protected readonly editing = this.data.user !== undefined;
  protected readonly isSelf = this.data.user !== undefined && this.data.user.id === this.data.currentUserId;
  protected readonly minPassword = MIN_PASSWORD_LENGTH;
  protected readonly roleOptions = (Object.keys(USER_ROLE_LABELS) as UserRole[])
    .reverse()
    .map((value) => ({ value, label: USER_ROLE_LABELS[value] }));

  // Signal Forms: el modelo es una señal y el formulario es un árbol de campos sobre ella.
  private readonly model = signal<UserModel>({
    userName: this.data.user?.userName ?? '',
    displayName: this.data.user?.displayName ?? '',
    password: '',
    role: this.data.user?.role ?? 'Usuario',
  });
  protected readonly userForm = form(this.model, (path) => {
    // El nombre de usuario y la contraseña inicial solo se piden al crear: al editar no se cambian.
    required(path.userName, { when: () => !this.editing });
    pattern(path.userName, NOT_BLANK);
    maxLength(path.userName, 50);
    required(path.displayName);
    pattern(path.displayName, NOT_BLANK);
    maxLength(path.displayName, 100);
    required(path.password, { when: () => !this.editing });
    minLength(path.password, MIN_PASSWORD_LENGTH);
    // Nadie se cambia el rol a sí mismo (la API tampoco lo deja).
    disabled(path.role, () => this.isSelf);
  });

  protected readonly isAdminRole = computed(() => this.model().role === 'Admin');

  /** Bibliotecas marcadas. Se conservan aunque se cambie el rol de ida y vuelta antes de guardar. */
  protected readonly selected = signal<ReadonlySet<string>>(new Set(this.data.user?.libraryIds));
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected toggle(id: string, checked: boolean): void {
    this.selected.update((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  protected async save(): Promise<void> {
    this.error.set(null);
    // submit() marca los campos como tocados (así se ven los errores) y solo corre si todo es válido.
    await submit(this.userForm, async () => {
      this.saving.set(true);
      try {
        const { userName, displayName, password, role } = this.model();
        // Un administrador ve todo: no se mandan permisos. Y solo se mandan bibliotecas que existen.
        const libraryIds =
          role === 'Admin'
            ? []
            : this.data.libraries
                .filter((library) => this.selected().has(library.id))
                .map((library) => library.id);
        const user = this.data.user
          ? await this.api.update(this.data.user.id, {
              displayName: displayName.trim(),
              role,
              libraryIds,
            })
          : await this.api.create({
              userName: userName.trim(),
              displayName: displayName.trim(),
              password,
              role,
              libraryIds,
            });
        this.dialogRef.close(user);
      } catch (error) {
        this.error.set(describeError(error));
      } finally {
        this.saving.set(false);
      }
    });
  }
}
