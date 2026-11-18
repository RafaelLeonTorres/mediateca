import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import type { AdminLibrary, FolderListing } from '../../core/admin/admin-api';
import { LibraryDialog, type LibraryDialogData } from './library-dialog';

const roots = (folders: FolderListing['folders']): FolderListing => ({
  path: null,
  displayPath: null,
  parent: null,
  folders,
});

const disks: FolderListing = roots([
  { name: 'Carpeta de medios', path: '/media', displayPath: '/media' },
  { name: 'Disco C:', path: '/discos/C', displayPath: 'C:\\' },
  { name: 'Disco F:', path: '/discos/F', displayPath: 'F:\\' },
]);

const diskF: FolderListing = {
  path: '/discos/F',
  displayPath: 'F:\\',
  parent: null,
  folders: [{ name: 'Peliculas', path: '/discos/F/Peliculas', displayPath: 'F:\\Peliculas' }],
};

const peliculas: FolderListing = {
  path: '/discos/F/Peliculas',
  displayPath: 'F:\\Peliculas',
  parent: '/discos/F',
  folders: [],
};

const existing: AdminLibrary = {
  id: 'l1',
  name: 'Cine',
  type: 'Movies',
  paths: ['/discos/D/Cine'],
  displayPaths: ['D:\\Cine'],
  createdAt: '2026-01-01T00:00:00Z',
  lastScannedAt: null,
  itemCount: 0,
  lastScan: null,
  metadata: null,
};

/** Deja correr lo pendiente (promesas y la continuación de un await) antes de seguir. */
const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('LibraryDialog', () => {
  function render(data: LibraryDialogData = {}) {
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [LibraryDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(LibraryDialog);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    TestBed.tick();
    const element = fixture.nativeElement as HTMLElement;

    // Sin whenStable: mientras hay una petición pendiente, la app nunca queda estable.
    const settle = async () => {
      await nextMacrotask();
      TestBed.tick();
      fixture.detectChanges();
    };
    const text = () => (element.textContent ?? '').replace(/\s+/g, ' ');
    const buttonNamed = (name: string) =>
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find((button) =>
        (button.textContent ?? '').includes(name),
      )!;

    /** Responde la petición de carpetas de una ruta (sin ruta, la lista inicial). */
    const answerFolders = async (listing: FolderListing, path?: string) => {
      const request = http.expectOne((r) => r.url === '/api/admin/folders');
      expect(request.request.params.get('path')).toBe(path ?? null);
      request.flush(listing);
      await settle();
    };

    const open = async (listing: FolderListing) => {
      buttonNamed('Agregar carpeta').click();
      await answerFolders(listing);
    };

    return { http, element, close, settle, text, buttonNamed, answerFolders, open };
  }

  it('al abrir el selector muestra los discos y la carpeta de medios por su nombre', async () => {
    const { open, text } = render();

    await open(disks);

    expect(text()).toContain('Tus discos y carpetas de medios');
    expect(text()).toContain('Carpeta de medios');
    expect(text()).toContain('Disco C:');
    expect(text()).toContain('Disco F:');
    expect(text()).not.toContain('/discos/F');
  });

  it('sin carpetas en la lista inicial explica cómo montar los discos', async () => {
    const { open, text } = render();

    await open(roots([]));

    expect(text()).toContain(
      'No se ven carpetas. En Windows, corre setup.ps1 para montar tus discos; en Linux o macOS, monta tus carpetas en /discos o usa MEDIA_PATH (ver README).',
    );
    expect(text()).not.toContain('No hay subcarpetas aquí.');
  });

  it('una carpeta vacía dentro de un disco no muestra la ayuda de setup.ps1', async () => {
    const { open, buttonNamed, answerFolders, text } = render();
    await open(disks);

    buttonNamed('Disco F:').click();
    await answerFolders({ ...diskF, folders: [] }, '/discos/F');

    expect(text()).toContain('No hay subcarpetas aquí.');
    expect(text()).not.toContain('setup.ps1');
  });

  it('dentro de un disco muestra la ruta de Windows y subir un nivel vuelve a la lista inicial', async () => {
    const { open, buttonNamed, answerFolders, text } = render();
    await open(disks);

    buttonNamed('Disco F:').click();
    await answerFolders(diskF, '/discos/F');

    expect(text()).toContain('F:\\');
    expect(text()).toContain('Peliculas');

    // Arriba de un disco no hay ruta: se pide la lista inicial.
    buttonNamed('Subir un nivel').click();
    await answerFolders(disks);

    expect(text()).toContain('Disco C:');
  });

  it('muestra la carpeta elegida con su ruta de Windows y manda al guardar la ruta del contenedor', async () => {
    const { http, element, close, open, buttonNamed, answerFolders, settle, text } = render();
    await open(disks);
    buttonNamed('Disco F:').click();
    await answerFolders(diskF, '/discos/F');
    buttonNamed('Peliculas').click();
    await answerFolders(peliculas, '/discos/F/Peliculas');

    buttonNamed('Elegir esta carpeta').click();
    await settle();

    expect(text()).toContain('F:\\Peliculas');
    expect(text()).not.toContain('/discos/F/Peliculas');

    const name = element.querySelector<HTMLInputElement>('input.mat-mdc-input-element')!;
    name.value = '  Mis películas  ';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    element
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
    await nextMacrotask();
    await nextMacrotask();

    const request = http.expectOne('/api/admin/libraries');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Mis películas',
      type: 'Movies',
      paths: ['/discos/F/Peliculas'],
    });
    request.flush({ id: 'l2' });
    await nextMacrotask();

    expect(close).toHaveBeenCalledWith(true);
  });

  it('al editar muestra las carpetas con su ruta de Windows y guarda las del contenedor', async () => {
    const { http, element, close, settle, text, buttonNamed } = render({ library: existing });

    expect(text()).toContain('D:\\Cine');
    expect(text()).not.toContain('/discos/D/Cine');
    // Se compara el atributo directamente: en un selector CSS la diagonal invertida es un escape.
    const removeButtons = Array.from(element.querySelectorAll('button'));
    expect(
      removeButtons.some((button) => button.getAttribute('aria-label') === 'Quitar la carpeta D:\\Cine'),
    ).toBe(true);

    buttonNamed('Guardar').click();
    await nextMacrotask();
    await nextMacrotask();
    const request = http.expectOne('/api/admin/libraries/l1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'Cine', paths: ['/discos/D/Cine'] });
    request.flush(existing);
    await settle();

    expect(close).toHaveBeenCalledWith(true);
  });

  it('si la carpeta elegida ya está en la lista no la repite', async () => {
    const { element, open, buttonNamed, answerFolders, settle } = render({ library: existing });
    await open(roots([{ name: 'Disco D:', path: '/discos/D', displayPath: 'D:\\' }]));
    buttonNamed('Disco D:').click();
    await answerFolders(
      {
        path: '/discos/D',
        displayPath: 'D:\\',
        parent: null,
        folders: [{ name: 'Cine', path: '/discos/D/Cine', displayPath: 'D:\\Cine' }],
      },
      '/discos/D',
    );
    buttonNamed('Cine').click();
    await answerFolders(
      { path: '/discos/D/Cine', displayPath: 'D:\\Cine', parent: '/discos/D', folders: [] },
      '/discos/D/Cine',
    );

    buttonNamed('Elegir esta carpeta').click();
    await settle();

    expect(element.querySelectorAll('.paths li')).toHaveLength(1);
  });
});
