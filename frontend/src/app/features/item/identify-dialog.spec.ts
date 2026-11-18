import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import type { MetadataCandidate } from '../../core/metadata/metadata-api';
import { IdentifyDialog } from './identify-dialog';

const nextMacrotask = () => new Promise<void>((resolve) => setTimeout(resolve));

function candidate(extra: Partial<MetadataCandidate> = {}): MetadataCandidate {
  return {
    externalId: '154587',
    title: 'Frieren: Beyond Journey\'s End',
    originalTitle: 'Sousou no Frieren',
    year: 2023,
    endYear: 2024,
    format: 'TV',
    status: 'Finalizada',
    posterUrl: 'https://s4.anilist.co/file/frieren.jpg',
    url: 'https://anilist.co/anime/154587',
    ...extra,
  };
}

describe('IdentifyDialog', () => {
  function render() {
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [IdentifyDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { id: 's1', title: 'Sousou no Frieren', year: 2023 } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(IdentifyDialog);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, http, el, close };
  }

  async function searchAndRespond(
    ctx: ReturnType<typeof render>,
    results: MetadataCandidate[],
  ): Promise<void> {
    ctx.el.querySelector<HTMLButtonElement>('form.search button')!.click();
    const request = ctx.http.expectOne((r) => r.url === '/api/admin/items/s1/metadata/search');
    request.flush({ provider: 'anilist', providerName: 'AniList', results });
    await nextMacrotask();
    ctx.fixture.detectChanges();
  }

  it('empieza con el título en la búsqueda y manda título y año', async () => {
    const ctx = render();
    expect(ctx.el.querySelector<HTMLInputElement>('form.search input')!.value).toBe(
      'Sousou no Frieren',
    );
    ctx.el.querySelector<HTMLButtonElement>('form.search button')!.click();
    const request = ctx.http.expectOne((r) => r.url === '/api/admin/items/s1/metadata/search');
    expect(request.request.params.get('query')).toBe('Sousou no Frieren');
    expect(request.request.params.get('year')).toBe('2023');
    request.flush({ provider: 'anilist', providerName: 'AniList', results: [] });
    await nextMacrotask();
    ctx.fixture.detectChanges();
    expect(ctx.el.textContent).toContain('No hay resultados en AniList.');
  });

  it('muestra los resultados con año, formato y estado', async () => {
    const ctx = render();
    await searchAndRespond(ctx, [candidate()]);
    const items = ctx.el.querySelectorAll('.results .candidate');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('Sousou no Frieren');
    expect(items[0].textContent).toContain('2023 – 2024 · TV · Finalizada');
  });

  it('al elegir un resultado lo aplica y cierra el diálogo', async () => {
    const ctx = render();
    await searchAndRespond(ctx, [candidate()]);
    ctx.el.querySelector<HTMLButtonElement>('.results .candidate')!.click();
    const request = ctx.http.expectOne('/api/admin/items/s1/metadata/identify');
    expect(request.request.body).toEqual({ identifier: '154587' });
    request.flush({ status: 'Matched', message: null, title: 'Frieren' });
    await nextMacrotask();
    expect(ctx.close).toHaveBeenCalledWith(true);
  });

  it('también aplica un id o una dirección pegados', async () => {
    const ctx = render();
    const input = ctx.el.querySelector<HTMLInputElement>('form.paste input')!;
    input.value = 'https://anilist.co/anime/154587';
    input.dispatchEvent(new Event('input'));
    ctx.fixture.detectChanges();
    ctx.el.querySelector<HTMLButtonElement>('form.paste button')!.click();
    const request = ctx.http.expectOne('/api/admin/items/s1/metadata/identify');
    expect(request.request.body).toEqual({ identifier: 'https://anilist.co/anime/154587' });
    request.flush({ status: 'Matched', message: null, title: 'Frieren' });
    await nextMacrotask();
    expect(ctx.close).toHaveBeenCalledWith(true);
  });

  it('muestra el mensaje de la API si el proveedor falla y no cierra', async () => {
    const ctx = render();
    await searchAndRespond(ctx, [candidate()]);
    ctx.el.querySelector<HTMLButtonElement>('.results .candidate')!.click();
    ctx.http
      .expectOne('/api/admin/items/s1/metadata/identify')
      .flush(
        { title: 'AniList pide esperar un momento.' },
        { status: 429, statusText: 'Too Many Requests' },
      );
    await nextMacrotask();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('[role="alert"]')?.textContent).toContain(
      'AniList pide esperar un momento.',
    );
    expect(ctx.close).not.toHaveBeenCalled();
  });

  it('si el proveedor no encuentra el elemento, lo dice y no cierra', async () => {
    const ctx = render();
    const input = ctx.el.querySelector<HTMLInputElement>('form.paste input')!;
    input.value = '999';
    input.dispatchEvent(new Event('input'));
    ctx.fixture.detectChanges();
    ctx.el.querySelector<HTMLButtonElement>('form.paste button')!.click();
    ctx.http
      .expectOne('/api/admin/items/s1/metadata/identify')
      .flush({ status: 'NotFound', message: 'AniList no tiene ese id.', title: null });
    await nextMacrotask();
    ctx.fixture.detectChanges();
    expect(ctx.el.querySelector('[role="alert"]')?.textContent).toContain('AniList no tiene ese id.');
    expect(ctx.close).not.toHaveBeenCalled();
  });
});
