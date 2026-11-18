import { Component, inject, signal } from '@angular/core';
import { FormField, form, maxLength } from '@angular/forms/signals';
import { MatButton } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogActions,
  MatDialogContent,
  MatDialogRef,
  MatDialogTitle,
} from '@angular/material/dialog';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';

import {
  MetadataApi,
  describeMetadataError,
  type MetadataCandidate,
  type MetadataSearch,
} from '../../core/metadata/metadata-api';

export interface IdentifyDialogData {
  /** Serie o película que se identifica. */
  id: string;
  title: string;
  year: number | null;
}

interface IdentifyModel {
  query: string;
  identifier: string;
}

/**
 * Identificar una serie o película a mano (solo administradores): se busca en el proveedor (AniList u OMDb)
 * y se elige un resultado, o se pega un id o una dirección. Al elegir se aplica enseguida.
 * Se cierra con true cuando se aplicó algo, para que la ficha se vuelva a pedir.
 */
@Component({
  selector: 'app-identify-dialog',
  imports: [
    FormField,
    MatButton,
    MatDialogActions,
    MatDialogContent,
    MatDialogTitle,
    MatFormField,
    MatHint,
    MatIcon,
    MatInput,
    MatLabel,
    MatProgressBar,
  ],
  template: `
    <h2 mat-dialog-title>Identificar</h2>

    <mat-dialog-content>
      <form class="search" novalidate (submit)="$event.preventDefault(); search()">
        <mat-form-field appearance="outline">
          <mat-label>Buscar por nombre</mat-label>
          <input matInput [formField]="identifyForm.query" autocomplete="off" />
        </mat-form-field>
        <button matButton="filled" type="submit" [disabled]="busy()">
          <mat-icon aria-hidden="true">search</mat-icon>
          Buscar
        </button>
      </form>

      @if (busy()) {
        <mat-progress-bar mode="indeterminate" aria-label="Trabajando" />
      }

      @if (searched()) {
        @if (result().results.length === 0) {
          <p class="muted">No hay resultados en {{ result().providerName }}.</p>
        } @else {
          <ul class="results" [attr.aria-label]="'Resultados de ' + result().providerName">
            @for (candidate of result().results; track candidate.externalId) {
              <li>
                <button
                  type="button"
                  class="candidate"
                  [disabled]="busy()"
                  (click)="apply(candidate.externalId)"
                >
                  @if (candidate.posterUrl) {
                    <img [src]="candidate.posterUrl" alt="" loading="lazy" referrerpolicy="no-referrer" />
                  } @else {
                    <span class="no-poster" aria-hidden="true"><mat-icon>image</mat-icon></span>
                  }
                  <span class="text">
                    <span class="name">{{ candidate.title }}</span>
                    @if (candidate.originalTitle && candidate.originalTitle !== candidate.title) {
                      <span class="sub">{{ candidate.originalTitle }}</span>
                    }
                    <span class="sub">{{ detailsOf(candidate) }}</span>
                  </span>
                </button>
              </li>
            }
          </ul>
        }
      }

      <form class="paste" novalidate (submit)="$event.preventDefault(); applyPasted()">
        <mat-form-field appearance="outline">
          <mat-label>O pega un id o una dirección</mat-label>
          <input matInput [formField]="identifyForm.identifier" autocomplete="off" />
          <mat-hint>Por ejemplo, https://anilist.co/anime/154587 o tt1254207.</mat-hint>
        </mat-form-field>
        <button matButton="outlined" type="submit" [disabled]="busy()">Usar</button>
      </form>

      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton="text" type="button" (click)="dialogRef.close(false)">Cancelar</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content {
      min-width: min(560px, 80vw);
    }

    .search,
    .paste {
      display: flex;
      align-items: flex-start;
      gap: 8px;
    }

    .search mat-form-field,
    .paste mat-form-field {
      flex: 1;
    }

    .results {
      display: flex;
      flex-direction: column;
      gap: 4px;
      max-height: 320px;
      margin: 0 0 16px;
      padding: 0;
      overflow-y: auto;
      list-style: none;
    }

    .candidate {
      display: flex;
      align-items: center;
      gap: 12px;
      width: 100%;
      padding: 8px;
      border: 0;
      border-radius: var(--mat-sys-corner-medium);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
      outline: none;
    }

    .candidate:hover:not(:disabled) {
      background: var(--mat-sys-surface-container-high);
    }

    .candidate:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    .candidate img,
    .no-poster {
      flex: none;
      width: 48px;
      height: 72px;
      border-radius: var(--mat-sys-corner-small);
      object-fit: cover;
      background: var(--mat-sys-surface-container-high);
    }

    .no-poster {
      display: grid;
      place-items: center;
      color: var(--mat-sys-on-surface-variant);
    }

    .text {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .name {
      font: var(--mat-sys-title-small);
      overflow-wrap: anywhere;
    }

    .sub,
    .muted {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
      overflow-wrap: anywhere;
    }

    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class IdentifyDialog {
  protected readonly dialogRef = inject<MatDialogRef<IdentifyDialog, boolean>>(MatDialogRef);
  private readonly data = inject<IdentifyDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(MetadataApi);

  private readonly model = signal<IdentifyModel>({ query: this.data.title, identifier: '' });
  protected readonly identifyForm = form(this.model, (path) => {
    maxLength(path.query, 200);
    maxLength(path.identifier, 300);
  });

  protected readonly busy = signal(false);
  protected readonly searched = signal(false);
  protected readonly result = signal<MetadataSearch>({ provider: '', providerName: '', results: [] });
  protected readonly error = signal<string | null>(null);

  protected async search(): Promise<void> {
    const query = this.model().query.trim();
    if (!query) {
      this.error.set('Escribe qué buscar.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      this.result.set(await this.api.search(this.data.id, query, this.data.year));
      this.searched.set(true);
    } catch (error) {
      this.error.set(describeMetadataError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async applyPasted(): Promise<void> {
    const identifier = this.model().identifier.trim();
    if (!identifier) {
      this.error.set('Pega un id o una dirección.');
      return;
    }
    await this.apply(identifier);
  }

  /** Aplica el resultado elegido o el id pegado. Si el proveedor no lo encuentra, el diálogo sigue abierto. */
  protected async apply(identifier: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const outcome = await this.api.identify(this.data.id, identifier);
      if (outcome.status === 'Matched') {
        this.dialogRef.close(true);
      } else {
        this.error.set(outcome.message ?? 'No se encontró ese elemento.');
      }
    } catch (error) {
      this.error.set(describeMetadataError(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** "2026 · TV · En emisión": lo que ayuda a distinguir un resultado de otro. */
  protected detailsOf(candidate: MetadataCandidate): string {
    const years =
      candidate.year && candidate.endYear && candidate.endYear !== candidate.year
        ? `${candidate.year} – ${candidate.endYear}`
        : candidate.year
          ? String(candidate.year)
          : null;
    return [years, candidate.format, candidate.status].filter(Boolean).join(' · ');
  }
}
