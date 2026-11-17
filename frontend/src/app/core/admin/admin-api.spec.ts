import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AdminApi, isScanActive, type ScanJob, type ScanSchedule } from './admin-api';

const job = (status: ScanJob['status']): ScanJob => ({
  id: 'j1',
  libraryId: 'l1',
  status,
  createdAt: '2026-01-01T00:00:00Z',
  startedAt: null,
  finishedAt: null,
  added: 0,
  updated: 0,
  removed: 0,
  errors: 0,
  errorMessage: null,
});

const schedule: ScanSchedule = {
  enabled: true,
  cronExpression: '0 0 3 * * ?',
  timeZone: 'America/Mexico_City',
  nextRuns: ['2026-11-18T09:00:00Z', '2026-11-19T09:00:00Z', '2026-11-20T09:00:00Z'],
  updatedAt: '2026-11-17T12:00:00Z',
};

describe('AdminApi', () => {
  let api: AdminApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(AdminApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lista las bibliotecas de administración', async () => {
    const resource = TestBed.runInInjectionContext(() => api.libraries());
    TestBed.tick();

    http.expectOne('/api/admin/libraries').flush([]);
    // La respuesta llega al recurso de forma asíncrona.
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()).toEqual([]);
  });

  it('crea una biblioteca con POST', async () => {
    const result = api.create({ name: 'Cine', type: 'Movies', paths: ['/media/cine'] });

    const request = http.expectOne('/api/admin/libraries');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Cine', type: 'Movies', paths: ['/media/cine'] });
    request.flush({ id: 'l1' });

    expect((await result).id).toBe('l1');
  });

  it('edita con PUT y elimina con DELETE', async () => {
    const updated = api.update('l1', { name: 'Películas', paths: ['/media/cine'] });
    const put = http.expectOne('/api/admin/libraries/l1');
    expect(put.request.method).toBe('PUT');
    put.flush({ id: 'l1' });
    await updated;

    const removed = api.remove('l1');
    const del = http.expectOne('/api/admin/libraries/l1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    await removed;
  });

  it('pide un escaneo y consulta su avance', async () => {
    const started = api.scan('l1');
    const post = http.expectOne('/api/admin/libraries/l1/scan');
    expect(post.request.method).toBe('POST');
    post.flush(job('Queued'), { status: 202, statusText: 'Accepted' });
    expect((await started).status).toBe('Queued');

    const status = api.scanStatus('j1');
    http.expectOne('/api/admin/scans/j1').flush(job('Running'));
    expect((await status).status).toBe('Running');
  });

  it('pide el escaneo de todas las bibliotecas con POST', async () => {
    const result = api.scanAll();

    const request = http.expectOne('/api/admin/libraries/scan-all');
    expect(request.request.method).toBe('POST');
    request.flush({ queued: 2 }, { status: 202, statusText: 'Accepted' });

    expect(await result).toEqual({ queued: 2 });
  });

  it('lee el horario del escaneo automático', async () => {
    const resource = TestBed.runInInjectionContext(() => api.scanSchedule());
    TestBed.tick();

    http.expectOne('/api/admin/scan-schedule').flush(schedule);
    // La respuesta llega al recurso de forma asíncrona.
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()).toEqual(schedule);
  });

  it('guarda el horario del escaneo automático con PUT', async () => {
    const result = api.updateScanSchedule({ enabled: false, cronExpression: '0 0 */6 * * ?' });

    const request = http.expectOne('/api/admin/scan-schedule');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ enabled: false, cronExpression: '0 0 */6 * * ?' });
    request.flush({ ...schedule, enabled: false, nextRuns: [] });

    expect(await result).toMatchObject({ enabled: false, nextRuns: [] });
  });

  it('explora carpetas con y sin ruta', async () => {
    const roots = api.folders();
    const first = http.expectOne('/api/admin/folders');
    expect(first.request.params.keys()).toEqual([]);
    first.flush({ path: null, displayPath: null, parent: null, folders: [] });
    await roots;

    const inside = api.folders('/media/cine');
    const second = http.expectOne((r) => r.url === '/api/admin/folders');
    expect(second.request.params.get('path')).toBe('/media/cine');
    second.flush({ path: '/media/cine', displayPath: '/media/cine', parent: '/media', folders: [] });
    await inside;
  });

  it('un escaneo está activo mientras está en espera o corriendo', () => {
    expect(isScanActive(job('Queued'))).toBe(true);
    expect(isScanActive(job('Running'))).toBe(true);
    expect(isScanActive(job('Completed'))).toBe(false);
    expect(isScanActive(job('Failed'))).toBe(false);
    expect(isScanActive(null)).toBe(false);
  });
});
