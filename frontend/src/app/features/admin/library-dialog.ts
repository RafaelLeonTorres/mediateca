import { Component, inject, signal } from '@angular/core';
import { FormField, disabled, form, maxLength, required, submit } from '@angular/forms/signals';
import { MatButton, MatIconButton } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogActions,
  MatDialogContent,
  MatDialogRef,
  MatDialogTitle,
} from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';

import { AdminApi, type AdminLibrary, type FolderListing } from '../../core/admin/admin-api';
import { LIBRARY_TYPE_LABELS, type LibraryType } from '../../core/libraries/libraries-api';
import { describeError } from '../../shared/describe-error';

export interface LibraryDialogData {
  /** Biblioteca que se edita; sin ella, el diálogo crea una nueva. */
  library?: AdminLibrary;
}

interface LibraryModel {
  name: string;
  type: LibraryType;
}

/** Crear o editar una biblioteca: nombre, tipo y carpetas (elegidas con el selector de carpetas). */
@Component({
  selector: 'app-library-dialog',
  imports: [
    FormField,
    MatButton,
    MatDialogActions,
    MatDialogContent,
    MatDialogTitle,
    MatError,
    MatFormField,
    MatHint,
    MatIcon,
    MatIconButton,
    MatInput,
    MatLabel,
    MatProgressBar,
  ],
  template: `
    <h2 mat-dialog-title>{{ editing ? 'Editar biblioteca' : 'Nueva biblioteca' }}</h2>

    <mat-dialog-content>
      <form class="form" novalidate (submit)="$event.preventDefault(); save()">
        <mat-form-field appearance="outline">
          <mat-label>Nombre</mat-label>
          <input matInput [formField]="libraryForm.name" autocomplete="off" />
          <mat-error>Escribe un nombre de hasta 100 caracteres.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Tipo</mat-label>
          <select matNativeControl [formField]="libraryForm.type">
            @for (option of typeOptions; track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
          @if (editing) {
            <mat-hint>El tipo no se puede cambiar después de crear la biblioteca.</mat-hint>
          }
        </mat-form-field>
      </form>

      <section aria-labelledby="paths-title">
        <h3 id="paths-title">Carpetas</h3>

        @if (browsing()) {
          <div class="picker">
            <p class="current">
              {{ listing()?.path ?? 'Carpetas de medios del servidor' }}
            </p>
            @if (loadingFolders()) {
              <mat-progress-bar mode="indeterminate" aria-label="Cargando carpetas" />
            }
            @if (browseError()) {
              <p class="error" role="alert">{{ browseError() }}</p>
            }
            <ul class="folders">
              @if (listing()?.path) {
                <li>
                  <button
                    matButton="text"
                    type="button"
                    (click)="browse(listing()?.parent ?? undefined)"
                  >
                    <mat-icon aria-hidden="true">arrow_upward</mat-icon>
                    Subir un nivel
                  </button>
                </li>
              }
              @for (folder of listing()?.folders ?? []; track folder.path) {
                <li>
                  <button matButton="text" type="button" (click)="browse(folder.path)">
                    <mat-icon aria-hidden="true">folder</mat-icon>
                    {{ folder.name }}
                  </button>
                </li>
              } @empty {
                @if (!loadingFolders() && !browseError()) {
                  <li class="muted">No hay subcarpetas aquí.</li>
                }
              }
            </ul>
            <div class="picker-actions">
              <button matButton="text" type="button" (click)="browsing.set(false)">Cancelar</button>
              <button
                matButton="filled"
                type="button"
                [disabled]="!listing()?.path"
                (click)="choose()"
              >
                Elegir esta carpeta
              </button>
            </div>
          </div>
        } @else {
          @if (paths().length > 0) {
            <ul class="paths">
              @for (path of paths(); track path) {
                <li>
                  <span class="path">{{ path }}</span>
                  <button
                    matIconButton
                    type="button"
                    [attr.aria-label]="'Quitar la carpeta ' + path"
                    (click)="removePath(path)"
                  >
                    <mat-icon aria-hidden="true">close</mat-icon>
                  </button>
                </li>
              }
            </ul>
          } @else {
            <p class="muted">Todavía no agregas ninguna carpeta.</p>
          }
          @if (pathsError()) {
            <p class="error" role="alert">{{ pathsError() }}</p>
          }
          <button matButton="outlined" type="button" (click)="openPicker()">
            <mat-icon aria-hidden="true">create_new_folder</mat-icon>
            Agregar carpeta
          </button>
        }
      </section>

      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton="text" type="button" (click)="dialogRef.close(false)">Cancelar</button>
      <button matButton="filled" type="button" [disabled]="saving()" (click)="save()">
        Guardar
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      min-width: min(480px, 80vw);
    }

    h3 {
      margin: 8px 0;
      font: var(--mat-sys-title-small);
    }

    ul {
      margin: 0 0 12px;
      padding: 0;
      list-style: none;
    }

    .paths li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }

    .path {
      overflow-wrap: anywhere;
      font-family: monospace;
    }

    .current {
      margin: 0 0 8px;
      font-family: monospace;
      overflow-wrap: anywhere;
    }

    .folders {
      max-height: 240px;
      overflow-y: auto;
    }

    .picker-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
    }

    .muted {
      color: var(--mat-sys-on-surface-variant);
    }

    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class LibraryDialog {
  protected readonly dialogRef = inject<MatDialogRef<LibraryDialog, boolean>>(MatDialogRef);
  private readonly data = inject<LibraryDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(AdminApi);

  protected readonly editing = this.data.library !== undefined;
  protected readonly typeOptions = (Object.keys(LIBRARY_TYPE_LABELS) as LibraryType[]).map(
    (value) => ({ value, label: LIBRARY_TYPE_LABELS[value] }),
  );

  // Signal Forms: el modelo es una señal y el formulario es un árbol de campos sobre ella.
  private readonly model = signal<LibraryModel>({
    name: this.data.library?.name ?? '',
    type: this.data.library?.type ?? 'Movies',
  });
  protected readonly libraryForm = form(this.model, (path) => {
    required(path.name);
    maxLength(path.name, 100);
    disabled(path.type, () => this.editing);
  });

  protected readonly paths = signal<string[]>([...(this.data.library?.paths ?? [])]);
  protected readonly pathsError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly browsing = signal(false);
  protected readonly listing = signal<FolderListing | null>(null);
  protected readonly loadingFolders = signal(false);
  protected readonly browseError = signal<string | null>(null);

  protected async openPicker(): Promise<void> {
    this.browsing.set(true);
    await this.browse(undefined);
  }

  /** Muestra las subcarpetas de una ruta del servidor; sin ruta, las carpetas de medios. */
  protected async browse(path: string | undefined): Promise<void> {
    this.loadingFolders.set(true);
    this.browseError.set(null);
    try {
      this.listing.set(await this.api.folders(path));
    } catch (error) {
      this.browseError.set(describeError(error));
    } finally {
      this.loadingFolders.set(false);
    }
  }

  protected choose(): void {
    const path = this.listing()?.path;
    if (path && !this.paths().includes(path)) {
      this.paths.update((paths) => [...paths, path]);
    }
    this.pathsError.set(null);
    this.browsing.set(false);
  }

  protected removePath(path: string): void {
    this.paths.update((paths) => paths.filter((p) => p !== path));
  }

  protected async save(): Promise<void> {
    this.error.set(null);
    await submit(this.libraryForm, async () => {
      if (this.paths().length === 0) {
        this.pathsError.set('Agrega al menos una carpeta.');
        return;
      }

      this.saving.set(true);
      try {
        const { name, type } = this.model();
        const library = this.data.library;
        if (library) {
          await this.api.update(library.id, { name: name.trim(), paths: this.paths() });
        } else {
          await this.api.create({ name: name.trim(), type, paths: this.paths() });
        }
        this.dialogRef.close(true);
      } catch (error) {
        this.error.set(describeError(error));
      } finally {
        this.saving.set(false);
      }
    });
  }
}
