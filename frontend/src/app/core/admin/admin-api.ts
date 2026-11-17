import { HttpClient, HttpResourceRef, httpResource } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { LibraryType } from '../libraries/libraries-api';

export type ScanStatus = 'Queued' | 'Running' | 'Completed' | 'Failed';

/** Avance de un escaneo. */
export interface ScanJob {
  id: string;
  libraryId: string;
  status: ScanStatus;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  added: number;
  updated: number;
  removed: number;
  errors: number;
  errorMessage: string | null;
}

/** Biblioteca vista por el administrador, con sus carpetas y su último escaneo. */
export interface AdminLibrary {
  id: string;
  name: string;
  type: LibraryType;
  paths: string[];
  createdAt: string;
  lastScannedAt: string | null;
  itemCount: number;
  lastScan: ScanJob | null;
}

/** Cuerpo para crear o editar una biblioteca. En la edición el tipo no cambia. */
export interface LibraryInput {
  name: string;
  type?: LibraryType;
  paths: string[];
}

export interface FolderEntry {
  name: string;
  path: string;
}

/** Contenido de una carpeta del servidor, para el selector de carpetas. */
export interface FolderListing {
  path: string | null;
  parent: string | null;
  folders: FolderEntry[];
}

/** Resultado de pedir el escaneo de todas las bibliotecas: cuántos escaneos nuevos entraron a la cola. */
export interface ScanAllResult {
  queued: number;
}

/** Horario del escaneo automático, como lo describe la API. */
export interface ScanSchedule {
  enabled: boolean;
  /** Expresión cron de Quartz, con segundos (por ejemplo, `0 0 3 * * ?`). */
  cronExpression: string;
  /** Zona horaria del servidor en la que se interpreta el horario. */
  timeZone: string;
  /** Próximas ejecuciones (ISO 8601); vacías si el escaneo automático está desactivado. */
  nextRuns: string[];
  updatedAt: string;
}

/** Cuerpo para guardar el horario del escaneo automático. */
export interface ScanScheduleInput {
  enabled: boolean;
  cronExpression: string;
}

/** ¿El escaneo sigue en marcha? */
export function isScanActive(job: Pick<ScanJob, 'status'> | null | undefined): boolean {
  return job?.status === 'Queued' || job?.status === 'Running';
}

/** Administración de bibliotecas: solo para administradores. */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  /** Bibliotecas con sus carpetas. Se actualiza con reload(). */
  libraries(): HttpResourceRef<AdminLibrary[] | undefined> {
    return httpResource<AdminLibrary[]>(() => '/api/admin/libraries');
  }

  create(input: LibraryInput): Promise<AdminLibrary> {
    return firstValueFrom(this.http.post<AdminLibrary>('/api/admin/libraries', input));
  }

  update(id: string, input: LibraryInput): Promise<AdminLibrary> {
    return firstValueFrom(this.http.put<AdminLibrary>(`/api/admin/libraries/${id}`, input));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/admin/libraries/${id}`));
  }

  /** Pide un escaneo. Si ya hay uno en marcha, devuelve ese. */
  scan(id: string): Promise<ScanJob> {
    return firstValueFrom(this.http.post<ScanJob>(`/api/admin/libraries/${id}/scan`, {}));
  }

  scanStatus(jobId: string): Promise<ScanJob> {
    return firstValueFrom(this.http.get<ScanJob>(`/api/admin/scans/${jobId}`));
  }

  /** Pone en la cola un escaneo de cada biblioteca. Las que ya tienen uno en marcha no cuentan. */
  scanAll(): Promise<ScanAllResult> {
    return firstValueFrom(this.http.post<ScanAllResult>('/api/admin/libraries/scan-all', {}));
  }

  /** Horario del escaneo automático. Se actualiza con reload(). */
  scanSchedule(): HttpResourceRef<ScanSchedule | undefined> {
    return httpResource<ScanSchedule>(() => '/api/admin/scan-schedule');
  }

  /** Guarda el horario del escaneo automático; el worker lo aplica en menos de un minuto. */
  updateScanSchedule(input: ScanScheduleInput): Promise<ScanSchedule> {
    return firstValueFrom(this.http.put<ScanSchedule>('/api/admin/scan-schedule', input));
  }

  /** Subcarpetas de una carpeta del servidor. Sin ruta, las carpetas de medios configuradas. */
  folders(path?: string): Promise<FolderListing> {
    return firstValueFrom(
      this.http.get<FolderListing>('/api/admin/folders', {
        params: path ? { path } : {},
      }),
    );
  }
}
