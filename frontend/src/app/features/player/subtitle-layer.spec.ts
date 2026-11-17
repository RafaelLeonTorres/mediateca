import { TestBed } from '@angular/core/testing';

import { SubtitleLayer } from './subtitle-layer';
import type { ActiveCue } from './subtitle-track-binder';

describe('SubtitleLayer', () => {
  const cue = (text: string, html?: () => DocumentFragment): ActiveCue =>
    ({ text, getCueAsHTML: html }) as unknown as ActiveCue;

  function render(cues: ActiveCue[], raised = false) {
    const fixture = TestBed.createComponent(SubtitleLayer);
    fixture.componentRef.setInput('cues', cues);
    fixture.componentRef.setInput('raised', raised);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, lines: () => Array.from(element.querySelectorAll<HTMLElement>('.cue')) };
  }

  it('sin cues no dibuja nada', () => {
    const { lines } = render([]);

    expect(lines()).toHaveLength(0);
  });

  it('dibuja un renglón por cue activo con el texto plano', () => {
    const { lines } = render([cue('Hola'), cue('¿Qué tal?')]);

    expect(lines().map((line) => line.textContent)).toEqual(['Hola', '¿Qué tal?']);
  });

  it('usa getCueAsHTML para conservar la cursiva y la negrita', () => {
    const fragment = () => {
      const part = document.createDocumentFragment();
      const italic = document.createElement('i');
      italic.textContent = 'susurro';
      part.appendChild(italic);
      return part;
    };
    const { lines } = render([cue('<i>susurro</i>', fragment)]);

    expect(lines()[0].querySelector('i')!.textContent).toBe('susurro');
  });

  it('el texto del archivo nunca se interpreta como HTML', () => {
    const { lines } = render([cue('<img src=x onerror=alert(1)>')]);

    expect(lines()[0].querySelector('img')).toBeNull();
    expect(lines()[0].textContent).toBe('<img src=x onerror=alert(1)>');
  });

  it('al cambiar los cues reemplaza los renglones anteriores', () => {
    const { fixture, lines } = render([cue('Uno')]);

    fixture.componentRef.setInput('cues', [cue('Dos')]);
    fixture.detectChanges();
    expect(lines().map((line) => line.textContent)).toEqual(['Dos']);

    fixture.componentRef.setInput('cues', []);
    fixture.detectChanges();
    expect(lines()).toHaveLength(0);
  });

  it('sube el bloque cuando la barra de controles está a la vista', () => {
    const { fixture, element } = render([cue('Hola')], false);
    const block = element.querySelector('.lines')!;
    expect(block.classList).not.toContain('raised');

    fixture.componentRef.setInput('raised', true);
    fixture.detectChanges();

    expect(block.classList).toContain('raised');
  });
});
