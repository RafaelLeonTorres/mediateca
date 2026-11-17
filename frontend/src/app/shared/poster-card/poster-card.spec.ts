import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PosterCard } from './poster-card';

describe('PosterCard', () => {
  function render(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({ imports: [PosterCard], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(PosterCard);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('muestra el póster con su texto alternativo y enlaza al detalle', () => {
    const { element } = render({ id: 'abc', title: 'Heat', hasImage: true, subtitle: '1995' });

    const img = element.querySelector('img')!;
    expect(img.getAttribute('src')).toBe('/api/items/abc/image');
    expect(img.getAttribute('alt')).toBe('Póster de Heat');
    expect(element.querySelector('a')!.getAttribute('href')).toBe('/items/abc');
    expect(element.textContent).toContain('1995');
  });

  it('sin imagen muestra el marcador con el ícono del tipo', () => {
    const { element } = render({ id: 'abc', title: 'Kind of Blue', kind: 'MusicAlbum' });

    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('mat-icon')!.textContent).toContain('album');
  });

  it('si la imagen da 404 cambia al marcador', () => {
    const { fixture, element } = render({ id: 'abc', title: 'Heat', hasImage: true });

    element.querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('mat-icon')).not.toBeNull();
  });
});
