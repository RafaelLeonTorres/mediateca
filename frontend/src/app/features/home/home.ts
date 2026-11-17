import { HttpClient } from '@angular/common/http';
import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';

interface Health {
  status: string;
  time: string;
}

/** Inicio provisional (fase 0): confirma que Angular llega a la API a través del proxy. */
@Component({
  selector: 'app-home',
  template: `
    <main class="home">
      <h1>Mediateca</h1>
      <p>Gestor de contenido multimedia.</p>
      @switch (estado()) {
        @case ('ok') {
          <p class="estado ok">La API responde correctamente.</p>
        }
        @case ('error') {
          <p class="estado error">No hay conexión con la API. Revisa que esté corriendo en http://localhost:5080.</p>
        }
        @default {
          <p class="estado">Revisando la API…</p>
        }
      }
    </main>
  `,
  styles: `
    .home {
      max-width: 40rem;
      margin: 4rem auto;
      padding: 0 1.5rem;
      font-family: system-ui, sans-serif;
    }
    .estado {
      padding: 0.75rem 1rem;
      border-radius: 0.5rem;
      background: #f1f1f4;
    }
    .ok {
      background: #e6f4ea;
    }
    .error {
      background: #fce8e6;
    }
  `,
})
export class Home {
  private readonly http = inject(HttpClient);

  protected readonly estado = toSignal(
    this.http.get<Health>('/api/health').pipe(
      map((health) => (health.status === 'ok' ? 'ok' : 'error')),
      catchError(() => of('error')),
    ),
    { initialValue: 'cargando' },
  );
}
