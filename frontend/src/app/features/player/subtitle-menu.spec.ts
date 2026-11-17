import { TestBed } from '@angular/core/testing';

import type { SubtitleTrack } from '../../core/playback/subtitles-api';
import { SubtitleMenu } from './subtitle-menu';

describe('SubtitleMenu', () => {
  function track(id: string, label: string, language: string | null = null): SubtitleTrack {
    return {
      id,
      label,
      language,
      codec: 'subrip',
      source: 'embedded',
      isDefault: false,
      isForced: false,
      url: `/api/items/v1/subtitles/${id}.vtt`,
    };
  }

  const tracks = [track('e2', 'Español', 'es'), track('e3', 'Inglés', 'en')];

  function render(list: SubtitleTrack[] = tracks, activeId: string | null = null) {
    const fixture = TestBed.createComponent(SubtitleMenu);
    fixture.componentRef.setInput('tracks', list);
    fixture.componentRef.setInput('activeId', activeId);
    const chosen: (string | null)[] = [];
    const opened: boolean[] = [];
    fixture.componentInstance.choose.subscribe((id) => chosen.push(id));
    fixture.componentInstance.openChange.subscribe((open) => opened.push(open));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = () => element.querySelector<HTMLButtonElement>('button[aria-haspopup="menu"]')!;
    const items = () => Array.from(element.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]'));
    return { fixture, element, trigger, items, chosen, opened };
  }

  it('sin pistas no muestra nada', () => {
    const { element } = render([]);

    expect(element.querySelector('button')).toBeNull();
  });

  it('con pistas muestra el botón CC cerrado y dice que están desactivados', () => {
    const { element, trigger } = render();

    expect(trigger().getAttribute('aria-label')).toBe('Subtítulos: desactivados');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(element.querySelector('[role="menu"]')).toBeNull();
  });

  it('al abrirlo ofrece Desactivados y una opción por pista, con la activa marcada', () => {
    const { fixture, trigger, items, opened } = render(tracks, 'e3');

    trigger().click();
    fixture.detectChanges();

    expect(opened).toEqual([true]);
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(trigger().getAttribute('aria-label')).toBe('Subtítulos: Inglés');
    expect(items().map((item) => item.getAttribute('aria-checked'))).toEqual(['false', 'false', 'true']);
    expect(items()[0].textContent).toContain('Desactivados');
    expect(items()[1].textContent).toContain('Español');
    expect(items()[2].textContent).toContain('Inglés');
  });

  it('elegir una pista avisa con su id y cierra el menú', () => {
    const { fixture, element, trigger, items, chosen, opened } = render();
    trigger().click();
    fixture.detectChanges();

    items()[1].click();
    fixture.detectChanges();

    expect(chosen).toEqual(['e2']);
    expect(opened).toEqual([true, false]);
    expect(element.querySelector('[role="menu"]')).toBeNull();
  });

  it('elegir Desactivados avisa con null', () => {
    const { fixture, trigger, items, chosen } = render(tracks, 'e2');
    trigger().click();
    fixture.detectChanges();

    items()[0].click();

    expect(chosen).toEqual([null]);
  });

  it('las flechas recorren las opciones, Escape cierra y devuelve el foco al botón', () => {
    const { fixture, element, trigger, items, opened } = render();
    document.body.appendChild(element);
    trigger().click();
    fixture.detectChanges();
    items()[0].focus();

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(items()[1]);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    expect(document.activeElement).toBe(items()[2]);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(items()[0]);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(element.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger());
    expect(opened).toEqual([true, false]);
    element.remove();
  });

  it('las teclas del menú no llegan a los atajos del reproductor', () => {
    const { element } = render();
    const reached = vi.fn();
    document.body.addEventListener('keydown', reached);
    document.body.appendChild(element);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));

    expect(reached).not.toHaveBeenCalled();
    document.body.removeEventListener('keydown', reached);
    element.remove();
  });

  it('un clic fuera lo cierra', () => {
    const { fixture, element, trigger } = render();
    trigger().click();
    fixture.detectChanges();

    document.body.click();
    fixture.detectChanges();

    expect(element.querySelector('[role="menu"]')).toBeNull();
  });
});
