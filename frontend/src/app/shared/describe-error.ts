import { HttpErrorResponse } from '@angular/common/http';

/** Respuesta de error de ASP.NET Core: ProblemDetails o ValidationProblemDetails. */
interface Problem {
  title?: string;
  errors?: Record<string, string[]>;
}

/** Convierte un error de la API en un mensaje para mostrar en pantalla. */
export function describeError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Algo salió mal. Inténtalo de nuevo.';
  }
  if (error.status === 0) {
    return 'No hay conexión con la API.';
  }
  if (error.status === 429) {
    return 'Demasiados intentos. Espera un minuto e inténtalo de nuevo.';
  }

  const problem: Problem = typeof error.error === 'object' && error.error !== null ? error.error : {};
  const messages = Object.values(problem.errors ?? {}).flat();
  if (messages.length > 0) {
    return messages.join(' ');
  }
  return problem.title ?? `La API respondió con un error (${error.status}).`;
}
