import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Un resultado de la búsqueda del diálogo Identificar. */
export interface MetadataCandidate {
  /** Id de AniList o de IMDb, según el proveedor. */
  externalId: string;
  title: string;
  originalTitle: string | null;
  year: number | null;
  endYear: number | null;
  format: string | null;
  status: string | null;
  posterUrl: string | null;
  url: string | null;
}

export interface MetadataSearch {
  provider: string;
  providerName: string;
  results: MetadataCandidate[];
}

/** Resultado de Identificar o Actualizar. */
export interface MetadataResult {
  status: 'Matched' | 'NotFound';
  message: string | null;
  title: string | null;
}

/** Identificar y actualizar metadatos de una serie o película (solo administradores). */
@Injectable({ providedIn: 'root' })
export class MetadataApi {
  private readonly http = inject(HttpClient);

  search(id: string, query: string, year?: number | null): Promise<MetadataSearch> {
    const params: Record<string, string> = { query };
    if (year) {
      params['year'] = String(year);
    }
    return firstValueFrom(
      this.http.get<MetadataSearch>(`/api/admin/items/${id}/metadata/search`, { params }),
    );
  }

  /** Aplica el elemento elegido. `identifier` es un id o una dirección de AniList o de IMDb. */
  identify(id: string, identifier: string): Promise<MetadataResult> {
    return firstValueFrom(
      this.http.post<MetadataResult>(`/api/admin/items/${id}/metadata/identify`, { identifier }),
    );
  }

  refresh(id: string): Promise<MetadataResult> {
    return firstValueFrom(
      this.http.post<MetadataResult>(`/api/admin/items/${id}/metadata/refresh`, {}),
    );
  }
}

/**
 * Mensaje para un error de metadatos. A diferencia de `describeError`, un 429 aquí es del proveedor
 * (no del inicio de sesión), así que se usa el título que explica la API.
 */
export function describeMetadataError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Algo salió mal. Inténtalo de nuevo.';
  }
  if (error.status === 0) {
    return 'No hay conexión con la API.';
  }
  const title =
    typeof error.error === 'object' && error.error !== null
      ? (error.error as { title?: string }).title
      : undefined;
  return title ?? `La API respondió con un error (${error.status}).`;
}
