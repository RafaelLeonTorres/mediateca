import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { ResumeItem } from '../../core/items/items-api';
import { ResumeCard } from './resume-card';

describe('ResumeCard', () => {
  const episode: ResumeItem = {
    id: 'e2',
    kind: 'Episode',
    title: 'Episodio 2',
    seriesTitle: 'Mi Anime',
    parentTitle: 'Lat',
    imageId: 'e2',
    durationSeconds: 1200,
    positionSeconds: 300,
    lastPlayedAt: '2026-11-17T20:00:00Z',
  };

  function render(item: ResumeItem) {
    TestBed.configureTestingModule({ imports: [ResumeCard], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(ResumeCard);
    fixture.componentRef.setInput('item', item);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('abre el reproductor del episodio y muestra la serie, la carpeta y el avance', () => {
    const { element } = render(episode);

    expect(element.querySelector('a')!.getAttribute('href')).toBe('/reproducir/e2');
    expect(element.querySelector('.title')!.textContent).toBe('Mi Anime');
    expect(element.querySelector('.subtitle')!.textContent).toBe('Lat · Episodio 2 · 5:00 de 20:00');
    expect(element.querySelector('img')!.getAttribute('src')).toBe('/api/items/e2/image');
    const bar = element.querySelector('mat-progress-bar')!;
    expect(bar.getAttribute('aria-label')).toBe('Avance: 25 %');
  });

  it('una película muestra su título y sin carpeta', () => {
    const { element } = render({
      ...episode,
      id: 'm1',
      kind: 'Movie',
      title: 'Heat',
      seriesTitle: null,
      parentTitle: null,
      imageId: 'm1',
      positionSeconds: 600,
      durationSeconds: 7200,
    });

    expect(element.querySelector('.title')!.textContent).toBe('Heat');
    expect(element.querySelector('.subtitle')!.textContent).toBe('10:00 de 2:00:00');
  });

  it('sin duración conocida no dibuja la barra', () => {
    const { element } = render({ ...episode, durationSeconds: null });

    expect(element.querySelector('mat-progress-bar')).toBeNull();
    expect(element.querySelector('.subtitle')!.textContent).toBe('Lat · Episodio 2');
  });

  it('sin imagen muestra el ícono del tipo', () => {
    const { element } = render({ ...episode, imageId: null });

    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('.placeholder mat-icon')).not.toBeNull();
  });

  it('si la imagen no carga cambia al ícono del tipo', () => {
    const { fixture, element } = render(episode);

    element.querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('.placeholder')).not.toBeNull();
  });
});
