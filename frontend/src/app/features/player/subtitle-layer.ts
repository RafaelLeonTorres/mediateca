import { Component, ElementRef, effect, input, viewChild } from '@angular/core';

import type { ActiveCue } from './subtitle-track-binder';

/**
 * Dibuja los cues activos como HTML encima del video y del lienzo de WebGPU, así los subtítulos se ven igual
 * con y sin reescalado. El tamaño del texto es relativo al alto del escenario (unidades `cqh`) y el bloque
 * sube cuando la barra de controles está visible, para que no la tape. No recibe clics.
 */
@Component({
  selector: 'app-subtitle-layer',
  template: `<div #lines class="lines" [class.raised]="raised()"></div>`,
  styles: `
    :host {
      position: absolute;
      inset: 0;
      container-type: size;
      pointer-events: none;
      z-index: 1;
    }

    .lines {
      position: absolute;
      right: 0;
      bottom: 4cqh;
      left: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.2em;
      padding: 0 6%;
      font-size: clamp(14px, 4.6cqh, 72px);
      transition: bottom 0.2s ease;
    }

    .lines.raised {
      bottom: max(4cqh, 92px);
    }

    :host ::ng-deep .cue {
      max-width: 100%;
      color: #fff;
      font-weight: 500;
      line-height: 1.25;
      text-align: center;
      white-space: pre-line;
      text-shadow:
        0 0 0.12em #000,
        0 0 0.12em #000,
        0 0.05em 0.1em #000,
        0.06em 0.06em 0.08em #000;
    }

    @media (prefers-reduced-motion: reduce) {
      .lines {
        transition: none;
      }
    }
  `,
})
export class SubtitleLayer {
  readonly cues = input<readonly ActiveCue[]>([]);
  /** Subir el texto: la barra de controles está a la vista. */
  readonly raised = input(false);

  private readonly lines = viewChild.required<ElementRef<HTMLElement>>('lines');

  constructor() {
    effect(() => {
      const cues = this.cues();
      const container = this.lines().nativeElement;
      container.replaceChildren();
      for (const cue of cues) {
        const line = container.ownerDocument.createElement('div');
        line.className = 'cue';
        // getCueAsHTML() convierte las etiquetas de WebVTT (i, b, u, c, v) en elementos del DOM; nunca hay
        // HTML crudo del archivo, y sin esa función se usa el texto plano.
        const html = cue.getCueAsHTML?.();
        if (html) {
          line.appendChild(html);
        } else {
          line.textContent = cue.text ?? '';
        }
        container.appendChild(line);
      }
    });
  }
}
