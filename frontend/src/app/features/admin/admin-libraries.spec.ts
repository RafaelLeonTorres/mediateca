import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { AdminLibrary, MetadataSummary, ScanJob, ScanSchedule } from '../../core/admin/admin-api';
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

const library = (
  lastScan: ScanJob | null,
  metadata: MetadataSummary | null = null,
): AdminLibrary => ({
  id: 'l1',
  name: 'Cine',
  type: 'Movies',
  paths: ['/discos/F/Cine'],
  displayPaths: ['F:\\Cine'],
  createdAt: '2026-01-01T00:00:00Z',
  lastScannedAt: null,
  itemCount: 3,
  lastScan,
  metadata,
});

const omdb = (extra: Partial<MetadataSummary> = {}): MetadataSummary => ({
  provider: 'omdb',
  providerName: 'OMDb',
  pending: 0,
  matched: 2,
  notFound: 1,
  error: 0,
  configured: true,
  pausedUntil: null,
  message: null,
  ...extra,
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

  it('muestra las carpetas como en Windows y no con la ruta del contenedor', async () => {
    const { load, text } = render();

    await load([library(null)]);

    expect(text()).toContain('F:\\Cine');
    expect(text()).not.toContain('/discos/F/Cine');
  });

  it('si el último escaneo terminó pero no pudo leer una carpeta, muestra el aviso', async () => {
    const { load, text } = render();
    const unreadable: ScanJob = {
      ...job('Completed', '2026-11-17T10:00:00Z'),
      errors: 1,
      errorMessage: 'No se pudo leer F:\\Cine. Revisa que el disco esté conectado.',
    };

    await load([library(unreadable)]);

    expect(text()).toContain('1 con errores');
    expect(text()).toContain('No se pudo leer F:\\Cine. Revisa que el disco esté conectado.');
  });

  it('un escaneo sin problemas no muestra ningún aviso', async () => {
    const { element, load } = render();

    await load([library(job('Completed', '2026-11-17T10:00:00Z'))]);

    expect(element.querySelector('.warning')).toBeNull();
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
  describe('metadatos', () => {
    it('sin proveedor (música) no muestra la sección', async () => {
      const { load, element } = render();
      await load([library(null)]);
      expect(element.querySelector('.metadata')).toBeNull();
    });

    it('muestra cuántos elementos hay en cada estado', async () => {
      const { load, text } = render();
      await load([library(null, omdb({ pending: 4, error: 1 }))]);

      expect(text()).toContain('Metadatos (OMDb)');
      expect(text()).toContain('2 encontrados');
      expect(text()).toContain('4 pendientes');
      expect(text()).toContain('1 sin resultado');
      expect(text()).toContain('1 con error');
    });

    it('avisa que falta la clave de OMDb y desactiva "Buscar metadatos"', async () => {
      const { load, text, buttonNamed } = render();
      await load([
        library(null, omdb({ configured: false, message: 'Falta la clave de OMDb (OMDB_API_KEY).' })),
      ]);

      expect(text()).toContain('Falta la clave de OMDb');
      expect(buttonNamed('Buscar metadatos').disabled).toBe(true);
    });

    it('avisa si el proveedor está en pausa', async () => {
      const { load, text } = render();
      await load([library(null, omdb({ pausedUntil: '2026-11-18T00:01:00Z' }))]);
      expect(text()).toContain('OMDb está en pausa');
    });

    it('"Buscar metadatos" pide la cola, muestra el mensaje y vuelve a pedir la lista', async () => {
      const { http, load, settle, text, buttonNamed } = render();
      await load([library(null, omdb({ pending: 3 }))]);

      buttonNamed('Buscar metadatos').click();
      http
        .expectOne('/api/admin/libraries/l1/metadata/fetch')
        .flush({ queued: 3, configured: true, message: null }, { status: 202, statusText: 'Accepted' });

      // Con la respuesta, la pantalla muestra el aviso y recarga la lista. La recarga se responde antes
      // de esperar a que la app quede estable: mientras una petición está pendiente, no lo está.
      await nextMacrotask();
      TestBed.tick();
      http.expectOne('/api/admin/libraries').flush([library(null, omdb({ pending: 3 }))]);
      await TestBed.inject(ApplicationRef).whenStable();
      await settle();

      expect(text()).toContain('Se pusieron en cola 3 elementos.');
    });

    it('si la petición falla, muestra el mensaje de la API', async () => {
      const { http, load, settle, element, buttonNamed } = render();
      await load([library(null, omdb())]);

      buttonNamed('Buscar metadatos').click();
      http
        .expectOne('/api/admin/libraries/l1/metadata/fetch')
        .flush({ title: 'Falta la clave de OMDb.' }, { status: 409, statusText: 'Conflict' });
      await nextMacrotask();
      await settle();

      expect(element.querySelector('[role="alert"]')!.textContent).toContain('Falta la clave de OMDb.');
    });
  });
});
