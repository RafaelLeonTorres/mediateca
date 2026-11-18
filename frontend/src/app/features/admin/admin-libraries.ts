import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import {
  AdminApi,
  isScanActive,
  type AdminLibrary,
  type MetadataSummary,
  type ScanJob,
} from '../../core/admin/admin-api';
import { LIBRARY_TYPE_LABELS, type LibraryType } from '../../core/libraries/libraries-api';
import { ConfirmDialog, type ConfirmDialogData } from '../../shared/confirm-dialog';
import { describeMetadataError } from '../../core/metadata/metadata-api';
import { describeError } from '../../shared/describe-error';
import { LibraryDialog, type LibraryDialogData } from './library-dialog';
import { ScanScheduleSection } from './scan-schedule';

/** Cada cuántos milisegundos se pregunta cómo va un escaneo. */
export const SCAN_POLL_MS = 2000;

/** Cada cuántos milisegundos se vuelve a pedir la lista mientras hay metadatos por buscar. */
export const METADATA_POLL_MS = 10_000;

/** Pasado este tiempo en la cola sin que nadie lo tome, se avisa que quizá el worker no está corriendo. */
export const QUEUE_WARNING_MS = 30_000;

/** Mensaje que confirma cuántos escaneos entraron a la cola con "Escanear todas". */
function describeQueued(queued: number): string {
  if (queued === 0) {
    return 'No se puso nada en cola: todas las bibliotecas ya tienen un escaneo pendiente.';
  }
  return queued === 1 ? 'Se puso en cola 1 escaneo.' : `Se pusieron en cola ${queued} escaneos.`;
}

/** ¿Quedan elementos por buscar y el proveedor puede trabajar? Si falta la clave, no tiene caso esperar. */
function hasQueuedMetadata(metadata: MetadataSummary | null | undefined): boolean {
  return !!metadata && metadata.pending > 0 && metadata.configured;
}

/** Administración de bibliotecas: lista, alta y edición, baja y escaneo con avance. */
@Component({
  selector: 'app-admin-libraries',
  imports: [DatePipe, MatButton, MatIcon, MatProgressBar, RouterLink, ScanScheduleSection],
  template: `
    <header class="head">
      <h1>Bibliotecas</h1>
      <div class="head-actions">
        <button
          matButton="tonal"
          type="button"
          [disabled]="scanningAll() || list().length === 0"
          (click)="scanAll()"
        >
          <mat-icon aria-hidden="true">refresh</mat-icon>
          Escanear todas
        </button>
        <button matButton="filled" type="button" (click)="create()">
          <mat-icon aria-hidden="true">add</mat-icon>
          Nueva biblioteca
        </button>
      </div>
    </header>

    @if (scanAllMessage()) {
      <p class="notice" role="status">{{ scanAllMessage() }}</p>
    }

    @if (libraries.isLoading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Cargando bibliotecas" />
    }

    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }

    @if (libraries.error()) {
      <p class="error" role="alert">No se pudieron cargar las bibliotecas.</p>
    } @else if (loaded() && list().length === 0) {
      <section class="empty" aria-labelledby="empty-title">
        <h2 id="empty-title">Todavía no hay bibliotecas</h2>
        <p>
          Crea una biblioteca, elige las carpetas donde están tus archivos y escanéala para que
          aparezcan en el inicio.
        </p>
        <button matButton="filled" type="button" (click)="create()">Crear la primera</button>
      </section>
    } @else {
      <ul class="libraries">
        @for (library of list(); track library.id) {
          <li class="library">
            <div class="top">
              <div>
                <h2>
                  <a [routerLink]="['/bibliotecas', library.id]">{{ library.name }}</a>
                </h2>
                <p class="meta">
                  {{ typeLabel(library.type) }} · {{ library.itemCount }}
                  {{ library.itemCount === 1 ? 'elemento' : 'elementos' }}
                </p>
              </div>
              <div class="actions">
                <button
                  matButton="tonal"
                  type="button"
                  [disabled]="isActive(library)"
                  (click)="scan(library)"
                >
                  <mat-icon aria-hidden="true">refresh</mat-icon>
                  Escanear
                </button>
                <button matButton="text" type="button" (click)="edit(library)">Editar</button>
                <button
                  matButton="text"
                  type="button"
                  [disabled]="isActive(library)"
                  (click)="remove(library)"
                >
                  Eliminar
                </button>
              </div>
            </div>

            <ul class="paths" [attr.aria-label]="'Carpetas de ' + library.name">
              @for (path of library.displayPaths; track path) {
                <li>{{ path }}</li>
              }
            </ul>

            @if (library.metadata; as meta) {
              <section class="metadata" [attr.aria-label]="'Metadatos de ' + library.name">
                <p class="counts">
                  <span class="label">Metadatos ({{ meta.providerName }})</span>
                  <span>{{ meta.matched }} encontrados</span>
                  <span>{{ meta.pending }} pendientes</span>
                  <span>{{ meta.notFound }} sin resultado</span>
                  <span>{{ meta.error }} con error</span>
                </p>
                @if (!meta.configured) {
                  <p class="warning" role="status">
                    {{ meta.message ?? 'Falta la clave de OMDb. Define OMDB_API_KEY y reinicia los contenedores api y worker.' }}
                  </p>
                } @else if (meta.pausedUntil) {
                  <p class="notice-inline" role="status">
                    {{ meta.providerName }} está en pausa hasta las
                    {{ meta.pausedUntil | date: 'HH:mm' }}; la cola sigue sola.
                  </p>
                }
                <div class="meta-actions">
                  <button
                    matButton="tonal"
                    type="button"
                    [disabled]="!meta.configured || fetchingMetadata().has(library.id)"
                    (click)="fetchMetadata(library)"
                  >
                    <mat-icon aria-hidden="true">travel_explore</mat-icon>
                    Buscar metadatos
                  </button>
                  @if (metadataMessages()[library.id]; as message) {
                    <span class="notice-inline" role="status">{{ message }}</span>
                  }
                </div>
              </section>
            }

            @let job = jobOf(library);
            @if (job && isActive(library)) {
              <div class="scan" role="status">
                <mat-progress-bar mode="indeterminate" aria-label="Escaneando" />
                <p>
                  {{ job.status === 'Queued' ? 'En espera…' : 'Escaneando…' }}
                  {{ job.added }} nuevos, {{ job.updated }} actualizados, {{ job.removed }}
                  eliminados.
                </p>
                @if (isStuck(job)) {
                  <p class="warning">
                    El escaneo sigue en cola. Revisa que el contenedor worker esté corriendo (docker
                    compose logs worker).
                  </p>
                }
              </div>
            } @else if (job?.status === 'Failed') {
              <p class="error" role="alert">
                El último escaneo falló: {{ job?.errorMessage ?? 'error desconocido' }}
              </p>
            } @else if (job?.status === 'Completed') {
              <p class="scan-result">
                Último escaneo: {{ job?.finishedAt | date: 'dd/MM/yyyy HH:mm' }} ·
                {{ job?.added }} nuevos, {{ job?.updated }} actualizados, {{ job?.removed }}
                eliminados
                @if (job && job.errors > 0) {
                  · {{ job.errors }} con errores
                }
              </p>
              @if (job?.errorMessage) {
                <p class="warning" role="status">{{ job?.errorMessage }}</p>
              }
            } @else {
              <p class="scan-result">Todavía no se ha escaneado.</p>
            }
          </li>
        }
      </ul>
    }

    <app-scan-schedule />
  `,
  styles: `
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 24px;
    }

    h1 {
      margin: 0;
      font: var(--mat-sys-headline-medium);
    }

    .head-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;
    }

    .error,
    .warning {
      color: var(--mat-sys-error);
    }

    .warning {
      margin: 8px 0 0;
    }

    .metadata {
      margin-top: 12px;
    }

    .counts {
      display: flex;
      flex-wrap: wrap;
      gap: 4px 16px;
      margin: 0;
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .counts .label {
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface);
    }

    .meta-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-top: 8px;
    }

    .notice-inline {
      margin: 8px 0 0;
      color: var(--mat-sys-on-surface-variant);
    }

    .meta-actions .notice-inline {
      margin: 0;
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

    .libraries {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .library {
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

    .library h2 {
      margin: 0;
      font: var(--mat-sys-title-large);
    }

    .library h2 a {
      color: inherit;
      text-decoration: none;
      border-radius: var(--mat-sys-corner-extra-small);
      outline: none;
    }

    .library h2 a:hover {
      text-decoration: underline;
    }

    .library h2 a:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    .meta,
    .scan-result,
    .paths {
      color: var(--mat-sys-on-surface-variant);
    }

    .meta {
      margin: 4px 0 0;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px;
    }

    .paths {
      margin: 12px 0;
      font-family: monospace;
      overflow-wrap: anywhere;
    }

    .scan p,
    .scan-result {
      margin: 8px 0 0;
    }

    .empty {
      padding: 32px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container-low);
    }

    .empty h2 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-medium);
    }

    .empty p {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class AdminLibraries {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly libraries = this.api.libraries();
  protected readonly list = computed(() => this.libraries.value() ?? []);
  protected readonly loaded = computed(() => this.libraries.status() === 'resolved');
  protected readonly error = signal<string | null>(null);
  protected readonly scanningAll = signal(false);
  protected readonly scanAllMessage = signal<string | null>(null);

  /** Bibliotecas a las que se acaba de pedir buscar metadatos (por id). */
  protected readonly fetchingMetadata = signal<ReadonlySet<string>>(new Set());
  /** Mensaje de la última petición de metadatos de cada biblioteca (por id). */
  protected readonly metadataMessages = signal<Record<string, string>>({});
  private metadataTimer: ReturnType<typeof setTimeout> | null = null;

  /** Hora de la última revisión, para saber cuánto lleva un escaneo en la cola (se actualiza al preguntar). */
  private readonly now = signal(Date.now());

  /** Último avance conocido de cada biblioteca (por id), el que se ve en pantalla. */
  private readonly jobs = signal<Record<string, ScanJob>>({});
  private readonly polling = new Set<string>();
  private readonly timers = new Set<ReturnType<typeof setTimeout>>();
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.timers.forEach((timer) => clearTimeout(timer));
      if (this.metadataTimer) {
        clearTimeout(this.metadataTimer);
      }
    });

    // Mientras haya metadatos por buscar, la lista se vuelve a pedir cada 10 s para ver cómo avanza la cola.
    effect(() => {
      const waiting = this.list().some((library) => hasQueuedMetadata(library.metadata));
      if (waiting && !this.metadataTimer && !this.destroyed) {
        this.metadataTimer = setTimeout(() => {
          this.metadataTimer = null;
          if (!this.destroyed) {
            this.libraries.reload();
          }
        }, METADATA_POLL_MS);
      }
    });

    // Si la página se abre con un escaneo en marcha, se sigue su avance.
    effect(() => {
      for (const library of this.list()) {
        if (isScanActive(library.lastScan) && library.lastScan) {
          void this.follow(library.id, library.lastScan);
        }
      }
    });
  }

  protected typeLabel(type: LibraryType): string {
    return LIBRARY_TYPE_LABELS[type];
  }

  /** Avance más reciente: el que se está siguiendo o, si no hay, el que trae la lista. */
  protected jobOf(library: AdminLibrary): ScanJob | null {
    return this.jobs()[library.id] ?? library.lastScan;
  }

  protected isActive(library: AdminLibrary): boolean {
    return isScanActive(this.jobOf(library));
  }

  protected create(): void {
    this.openDialog({});
  }

  protected edit(library: AdminLibrary): void {
    this.openDialog({ library });
  }

  /** ¿Lleva más de 30 s en la cola? Lo normal es que el worker lo tome en unos segundos. */
  protected isStuck(job: ScanJob): boolean {
    return job.status === 'Queued' && this.now() - Date.parse(job.createdAt) > QUEUE_WARNING_MS;
  }

  /** Pone en la cola la búsqueda de metadatos de las series o películas pendientes de la biblioteca. */
  protected async fetchMetadata(library: AdminLibrary): Promise<void> {
    this.error.set(null);
    this.fetchingMetadata.update((ids) => new Set(ids).add(library.id));
    try {
      const result = await this.api.fetchMetadata(library.id);
      const message =
        result.message ??
        (result.queued === 0
          ? 'No hay nada pendiente.'
          : result.queued === 1
            ? 'Se puso en cola 1 elemento.'
            : `Se pusieron en cola ${result.queued} elementos.`);
      this.metadataMessages.update((messages) => ({ ...messages, [library.id]: message }));
      this.libraries.reload();
    } catch (error) {
      this.error.set(describeMetadataError(error));
    } finally {
      this.fetchingMetadata.update((ids) => {
        const next = new Set(ids);
        next.delete(library.id);
        return next;
      });
    }
  }

  protected async scanAll(): Promise<void> {
    this.error.set(null);
    this.scanAllMessage.set(null);
    this.scanningAll.set(true);
    try {
      const { queued } = await this.api.scanAll();
      this.scanAllMessage.set(describeQueued(queued));
      // La lista nueva trae los escaneos recién puestos en la cola y el efecto del constructor sigue su avance.
      this.libraries.reload();
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.scanningAll.set(false);
    }
  }

  protected async scan(library: AdminLibrary): Promise<void> {
    this.error.set(null);
    try {
      const job = await this.api.scan(library.id);
      void this.follow(library.id, job);
    } catch (error) {
      this.error.set(describeError(error));
    }
  }

  protected remove(library: AdminLibrary): void {
    const data: ConfirmDialogData = {
      title: `¿Eliminar "${library.name}"?`,
      message:
        'Se quita la biblioteca y su catálogo de Mediateca. Tus archivos en disco no se borran.',
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
        try {
          await this.api.remove(library.id);
          this.libraries.reload();
        } catch (error) {
          this.error.set(describeError(error));
        }
      });
  }

  private openDialog(data: LibraryDialogData): void {
    this.dialog
      .open<LibraryDialog, LibraryDialogData, boolean>(LibraryDialog, { data, width: '560px' })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.libraries.reload();
        }
      });
  }

  /** Pregunta cada 2 s cómo va el escaneo hasta que termina; al final recarga la lista. */
  private async follow(libraryId: string, first: ScanJob): Promise<void> {
    if (this.polling.has(libraryId)) {
      return;
    }
    this.polling.add(libraryId);
    try {
      let job = first;
      this.now.set(Date.now());
      this.jobs.update((jobs) => ({ ...jobs, [libraryId]: job }));
      while (isScanActive(job) && !this.destroyed) {
        await this.wait(SCAN_POLL_MS);
        if (this.destroyed) {
          return;
        }
        job = await this.api.scanStatus(job.id);
        this.now.set(Date.now());
        this.jobs.update((jobs) => ({ ...jobs, [libraryId]: job }));
      }
      this.libraries.reload();
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.polling.delete(libraryId);
    }
  }

  private wait(ms: number): Promise<void> {
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        this.timers.delete(timer);
        resolve();
      }, ms);
      this.timers.add(timer);
    });
  }
}
