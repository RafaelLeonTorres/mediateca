import { Component, computed, input, output } from '@angular/core';
import { MatIcon } from '@angular/material/icon';

/** Dónde está el video dentro del lienzo, en porcentajes del escenario (el comparador se coloca encima). */
export interface VideoCover {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** Cuánto mueven las flechas el divisor (con Mayús, más). */
const STEP = 0.02;
const BIG_STEP = 0.1;

/**
 * Divisor del comparador (vista Comparar): una barra vertical que se arrastra con el ratón o el dedo, o se mueve
 * con las flechas, Inicio y Fin. A la izquierda se ve el video original y a la derecha el mejorado. `split` es la
 * fracción (0 a 1) del video que se muestra sin mejorar.
 */
@Component({
  selector: 'app-compare-divider',
  imports: [MatIcon],
  template: `
    @if (cover(); as c) {
      <div
        class="cover"
        [style.left.%]="c.left"
        [style.top.%]="c.top"
        [style.width.%]="c.width"
        [style.height.%]="c.height"
      >
        <span class="tag left">Original</span>
        <span class="tag right">Mejorado</span>
        <div
          class="handle"
          role="slider"
          tabindex="0"
          aria-label="Comparador entre original y mejorado"
          aria-orientation="horizontal"
          aria-valuemin="0"
          aria-valuemax="100"
          [attr.aria-valuenow]="percent()"
          [attr.aria-valuetext]="percent() + ' % original'"
          [style.left.%]="percent()"
          (pointerdown)="startDrag($event)"
          (keydown)="onKeydown($event)"
        >
          <span class="grip"><mat-icon aria-hidden="true">swap_horiz</mat-icon></span>
        </div>
      </div>
    }
  `,
  styles: `
    :host {
      position: absolute;
      inset: 0;
      z-index: 1;
      pointer-events: none;
    }

    .cover {
      position: absolute;
    }

    .tag {
      position: absolute;
      top: 8px;
      padding: 2px 10px;
      border-radius: 999px;
      background: rgb(0 0 0 / 60%);
      color: #fff;
      font: var(--mat-sys-label-small);
    }

    .left {
      left: 8px;
    }

    .right {
      right: 8px;
    }

    .handle {
      position: absolute;
      top: 0;
      bottom: 0;
      display: grid;
      place-items: center;
      width: 32px;
      margin-left: -16px;
      cursor: ew-resize;
      pointer-events: auto;
      touch-action: none;
    }

    .handle::before {
      content: '';
      position: absolute;
      top: 0;
      bottom: 0;
      left: 50%;
      width: 2px;
      margin-left: -1px;
      background: rgb(255 255 255 / 92%);
      box-shadow: 0 0 0 1px rgb(0 0 0 / 45%);
    }

    .grip {
      position: relative;
      display: grid;
      place-items: center;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: #fff;
      color: #000;
    }

    .handle:focus-visible {
      outline: none;
    }

    .handle:focus-visible .grip {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }
  `,
})
export class CompareDivider {
  readonly cover = input<VideoCover | null>(null);
  /** Fracción (0 a 1) del video que se muestra sin mejorar, a la izquierda del divisor. */
  readonly split = input(0.5);
  readonly splitChange = output<number>();

  protected readonly percent = computed(() => Math.round(this.split() * 100));

  protected startDrag(event: PointerEvent): void {
    const handle = event.currentTarget as HTMLElement;
    const area = handle.parentElement;
    if (!area) {
      return;
    }
    event.preventDefault();
    handle.setPointerCapture?.(event.pointerId);
    const move = (e: PointerEvent) => {
      const rect = area.getBoundingClientRect();
      if (rect.width > 0) {
        this.splitChange.emit(clamp01((e.clientX - rect.left) / rect.width));
      }
    };
    const stop = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', stop);
      handle.removeEventListener('pointercancel', stop);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', stop);
    handle.addEventListener('pointercancel', stop);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const step = event.shiftKey ? BIG_STEP : STEP;
    const current = this.split();
    const next =
      event.key === 'ArrowLeft' ? current - step
      : event.key === 'ArrowRight' ? current + step
      : event.key === 'Home' ? 0
      : event.key === 'End' ? 1
      : null;
    if (next === null) {
      return;
    }
    // Las flechas mueven el divisor, no el video (±10 s).
    event.preventDefault();
    event.stopPropagation();
    this.splitChange.emit(clamp01(next));
  }
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value));
}
