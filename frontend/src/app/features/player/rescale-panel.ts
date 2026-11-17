import { Component, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { MatIcon } from '@angular/material/icon';

import type { ScaleKernel } from './enhancer/enhancer-params';
import {
  KERNEL_OPTIONS,
  OUTPUT_OPTIONS,
  VIEW_OPTIONS,
  type RescaleSettings,
} from './rescale-settings';
import { WEBGPU_UNAVAILABLE_TEXT } from './video-enhancer';

/**
 * Botón "Reescalar" y su panel con las opciones del reescalado por GPU: vista (Original, Mejorado o Comparar),
 * resolución de salida, núcleo de escalado, nitidez adaptativa, reducción de ruido, anti-ringing, dithering y
 * Restablecer. Sin WebGPU el botón queda deshabilitado y dice por qué. El componente no guarda nada: recibe los
 * ajustes y avisa de cada cambio; el reproductor los aplica y los recuerda.
 */
@Component({
  selector: 'app-rescale-panel',
  imports: [MatIcon],
  styleUrls: ['./player-ui.scss'],
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(keydown)': 'onKeydown($event)',
  },
  template: `
    <div class="wrap">
      <span class="hint" [attr.title]="supported() ? null : unavailableText">
        <button
          #trigger
          type="button"
          class="ctl"
          aria-haspopup="dialog"
          [disabled]="!supported()"
          [attr.aria-expanded]="supported() ? open() : null"
          [attr.aria-label]="supported() ? 'Reescalar con la GPU' : 'Reescalar con la GPU (no disponible)'"
          [class.active]="supported() && settings().view !== 'original'"
          (click)="toggle()"
        >
          <mat-icon aria-hidden="true">auto_awesome</mat-icon>
          <span class="label">Reescalar</span>
        </button>
      </span>
      @if (!supported()) {
        <span class="sr-only" role="note">{{ unavailableText }}</span>
      }

      @if (open() && supported()) {
        <div class="popup panel" role="dialog" aria-label="Reescalar con la GPU">
          <fieldset class="view">
            <legend>Vista</legend>
            @for (option of views; track option.id) {
              <label [class.on]="settings().view === option.id">
                <input
                  type="radio"
                  name="rescale-view"
                  [value]="option.id"
                  [checked]="settings().view === option.id"
                  (change)="update({ view: option.id })"
                />
                {{ option.label }}
              </label>
            }
          </fieldset>

          <label class="field">
            <span>Resolución de salida</span>
            <select (change)="onOutput($event)">
              @for (option of outputs; track option.height) {
                <option [value]="option.height" [selected]="option.height === settings().outputHeight">
                  {{ option.label }}
                </option>
              }
            </select>
          </label>

          <label class="field">
            <span>Escalado</span>
            <select (change)="onKernel($event)">
              @for (option of kernels; track option.id) {
                <option [value]="option.id" [selected]="option.id === settings().kernel">
                  {{ option.label }}
                </option>
              }
            </select>
          </label>

          <label class="field">
            <span>Nitidez adaptativa <output>{{ percent(settings().sharpen) }} %</output></span>
            <input
              type="range"
              min="0"
              max="100"
              step="1"
              [value]="percent(settings().sharpen)"
              (input)="update({ sharpen: fraction($event) })"
            />
          </label>
          <label class="field">
            <span>Reducción de ruido <output>{{ percent(settings().denoise) }} %</output></span>
            <input
              type="range"
              min="0"
              max="100"
              step="1"
              [value]="percent(settings().denoise)"
              (input)="update({ denoise: fraction($event) })"
            />
          </label>
          <label class="field">
            <span>Anti-ringing <output>{{ percent(settings().antiRinging) }} %</output></span>
            <input
              type="range"
              min="0"
              max="100"
              step="1"
              [value]="percent(settings().antiRinging)"
              (input)="update({ antiRinging: fraction($event) })"
            />
          </label>
          <label class="check">
            <input
              type="checkbox"
              [checked]="settings().dither"
              (change)="update({ dither: !settings().dither })"
            />
            Dithering (evita bandas en los degradados)
          </label>

          <div class="footer">
            <button type="button" class="reset" (click)="reset.emit()">Restablecer</button>
            @if (info(); as text) {
              <span class="info" role="status">{{ text }}</span>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .hint {
      display: inline-flex;
    }

    .ctl.active {
      color: var(--mat-sys-primary);
    }

    .panel {
      display: flex;
      flex-direction: column;
      gap: 12px;
      width: 300px;
      padding: 16px;
      box-sizing: border-box;
    }

    fieldset {
      display: flex;
      gap: 4px;
      margin: 0;
      padding: 0;
      border: 0;
    }

    legend,
    .field > span {
      margin-bottom: 4px;
      color: rgb(255 255 255 / 72%);
      font: var(--mat-sys-label-medium);
    }

    .view label {
      flex: 1;
      padding: 6px 4px;
      border: 1px solid rgb(255 255 255 / 30%);
      border-radius: 8px;
      text-align: center;
      cursor: pointer;
    }

    .view label.on {
      border-color: var(--mat-sys-primary);
      background: rgb(255 255 255 / 14%);
    }

    .view input {
      position: absolute;
      opacity: 0;
    }

    .view label:focus-within {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    .field {
      display: flex;
      flex-direction: column;
    }

    .field output {
      float: right;
      color: #fff;
    }

    select {
      padding: 6px;
      border: 1px solid rgb(255 255 255 / 30%);
      border-radius: 8px;
      background: #1c1c20;
      color: #fff;
      font: inherit;
    }

    input[type='range'] {
      width: 100%;
      accent-color: var(--mat-sys-primary);
    }

    select:focus-visible,
    input:focus-visible,
    .reset:focus-visible {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    .check {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }

    .reset {
      padding: 6px 14px;
      border: 1px solid rgb(255 255 255 / 40%);
      border-radius: 16px;
      background: transparent;
      color: #fff;
      font: inherit;
      cursor: pointer;
    }

    .info {
      color: rgb(255 255 255 / 60%);
      font: var(--mat-sys-label-small);
      text-align: right;
    }

    @media (max-width: 600px) {
      .label {
        display: none;
      }
    }
  `,
})
export class RescalePanel {
  readonly settings = input.required<RescaleSettings>();
  /** El navegador tiene WebGPU. Sin él, el botón está deshabilitado. */
  readonly supported = input(true);
  /** Dato pequeño opcional: la GPU y los cuadros por segundo. */
  readonly info = input<string | null>(null);

  readonly settingsChange = output<RescaleSettings>();
  readonly reset = output<void>();
  readonly openChange = output<boolean>();

  protected readonly views = VIEW_OPTIONS;
  protected readonly outputs = OUTPUT_OPTIONS;
  protected readonly kernels = KERNEL_OPTIONS;
  protected readonly unavailableText = WEBGPU_UNAVAILABLE_TEXT;
  protected readonly open = signal(false);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');

  protected percent(value: number): number {
    return Math.round(value * 100);
  }

  protected fraction(event: Event): number {
    return (event.target as HTMLInputElement).valueAsNumber / 100;
  }

  protected toggle(): void {
    this.setOpen(!this.open());
  }

  protected update(change: Partial<RescaleSettings>): void {
    this.settingsChange.emit({ ...this.settings(), ...change });
  }

  protected onOutput(event: Event): void {
    const height = Number((event.target as HTMLSelectElement).value);
    if (OUTPUT_OPTIONS.some((option) => option.height === height)) {
      this.update({ outputHeight: height });
    }
  }

  protected onKernel(event: Event): void {
    const id = (event.target as HTMLSelectElement).value as ScaleKernel;
    if (KERNEL_OPTIONS.some((option) => option.id === id)) {
      this.update({ kernel: id });
    }
  }

  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node | null)) {
      this.setOpen(false);
    }
  }

  onKeydown(event: KeyboardEvent): void {
    // Lo que se teclea en el panel no activa los atajos del reproductor.
    event.stopPropagation();
    if (event.key === 'Escape' && this.open()) {
      event.preventDefault();
      this.setOpen(false);
      this.trigger()?.nativeElement.focus();
    }
  }

  private setOpen(value: boolean): void {
    if (this.open() !== value) {
      this.open.set(value);
      this.openChange.emit(value);
    }
  }
}
