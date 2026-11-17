import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { AdminLibrary, ScanJob, ScanSchedule } from '../../core/admin/admin-api';
import { AdminLibraries } from './admin-libraries';

const job = (status: ScanJob['status'], createdAt: string): ScanJob => ({
  id: 'j1',
  libraryId: 'l1',
  status,
  createdAt,
  startedAt: null,
  finishedAt: null,
  added: 0,
  updated: 0,
  removed: 0,
  errors: 0,
  errorMessage: null,
});

const library = (lastScan: ScanJob | null): AdminLibrary => ({
  id: 'l1',
  name: 'Cine',
  type: 'Movies',
  paths: ['/media/cine'],
  createdAt: '2026-01-01T00:00:00Z',
  lastScannedAt: null,
  itemCount: 3,
  lastScan,
});

/** Deja correr lo pendiente (promesas y la continuación de un await) antes de seguir. */
const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

const schedule: ScanSchedule = {
  enabled: true,
  cronExpression: '0 0 3 * * ?',
  timeZone: 'UTC',
  nextRuns: ['2026-11-18T03:00:00Z'],
  updatedAt: '2026-11-17T12:00:00Z',
};

describe('AdminLibraries', () => {
  function render() {
    TestBed.configureTestingModule({
      imports: [AdminLibraries],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AdminLibraries);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    TestBed.tick();
    const element = fixture.nativeElement as HTMLElement;

    const settle = async () => {
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
    };

    /** Responde la lista de bibliotecas y el horario, y deja que la pantalla se actualice. */
    const load = async (libraries: AdminLibrary[]) => {
      http.expectOne('/api/admin/libraries').flush(libraries);
      http.expectOne('/api/admin/scan-schedule').flush(schedule);
      await TestBed.inject(ApplicationRef).whenStable();
      await settle();
    };

    const text = () => (element.textContent ?? '').replace(/\s+/g, ' ');
    const buttonNamed = (name: string) =>
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find((button) =>
        (button.textContent ?? '').includes(name),
      )!;

    return { http, element, load, settle, text, buttonNamed };
  }

  it('muestra la sección de escaneo automático debajo de las bibliotecas', async () => {
    const { load, text } = render();

    await load([library(null)]);

    expect(text()).toContain('Cine');
    expect(text()).toContain('Escaneo automático');
  });

  it('Escanear todas pide los escaneos, avisa cuántos entraron a la cola y sigue su avance', async () => {
    const { http, load, settle, text, buttonNamed } = render();
    await load([library(job('Completed', '2026-11-17T10:00:00Z'))]);

    buttonNamed('Escanear todas').click();
    await nextMacrotask();
    const request = http.expectOne('/api/admin/libraries/scan-all');
    expect(request.request.method).toBe('POST');
    request.flush({ queued: 1 }, { status: 202, statusText: 'Accepted' });

    // Con la respuesta, la pantalla muestra el aviso y recarga la lista. La recarga se responde antes
    // de esperar a que la app quede estable: mientras una petición está pendiente, no lo está.
    await nextMacrotask();
    TestBed.tick();
    http.expectOne('/api/admin/libraries').flush([library(job('Queued', new Date().toISOString()))]);
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();

    expect(text()).toContain('Se puso en cola 1 escaneo.');
    // La lista nueva trae el escaneo en la cola y la pantalla lo sigue.
    expect(text()).toContain('En espera…');
    expect(text()).not.toContain('El escaneo sigue en cola.');
  });

  it('con varios escaneos nuevos usa el plural', async () => {
    const { http, load, settle, text, buttonNamed } = render();
    await load([library(null)]);

    buttonNamed('Escanear todas').click();
    await nextMacrotask();
    http.expectOne('/api/admin/libraries/scan-all').flush({ queued: 3 });

    await nextMacrotask();
    TestBed.tick();
    http.expectOne('/api/admin/libraries').flush([library(null)]);
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();

    expect(text()).toContain('Se pusieron en cola 3 escaneos.');
  });

  it('si un escaneo lleva más de 30 s en la cola, avisa que revise el worker', async () => {
    const { load, text } = render();
    const oneMinuteAgo = new Date(Date.now() - 60_000).toISOString();

    await load([library(job('Queued', oneMinuteAgo))]);

    expect(text()).toContain(
      'El escaneo sigue en cola. Revisa que el contenedor worker esté corriendo (docker compose logs worker).',
    );
  });

  it('un escaneo en cola desde hace pocos segundos no avisa nada', async () => {
    const { load, text } = render();

    await load([library(job('Queued', new Date().toISOString()))]);

    expect(text()).toContain('En espera…');
    expect(text()).not.toContain('El escaneo sigue en cola.');
  });
});
