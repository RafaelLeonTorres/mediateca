import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatIcon } from '@angular/material/icon';

import type { SubtitleTrack } from '../../core/playback/subtitles-api';

/**
 * Botón CC con su menú: "Desactivados" y una opción por pista. No aparece si el video no tiene pistas. El menú
 * se maneja con el teclado (flechas, Inicio, Fin, Escape) y avisa con `openChange` cuando se abre o se cierra,
 * para que la barra de controles no se oculte mientras está abierto.
 */
@Component({
  selector: 'app-subtitle-menu',
  imports: [MatIcon],
  styleUrls: ['./player-ui.scss'],
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(keydown)': 'onKeydown($event)',
  },
  template: `
    @if (tracks().length > 0) {
      <div class="wrap">
        <button
          #trigger
          type="button"
          class="ctl"
          aria-haspopup="menu"
          [attr.aria-expanded]="open()"
          [attr.aria-label]="'Subtítulos: ' + activeLabel()"
          [attr.title]="'Subtítulos (C)'"
          (click)="toggle()"
        >
          <mat-icon aria-hidden="true">{{ activeId() ? 'closed_caption' : 'closed_caption_off' }}</mat-icon>
        </button>

        @if (open()) {
          <div class="popup" role="menu" aria-label="Subtítulos">
            <button
              type="button"
              class="item"
              role="menuitemradio"
              tabindex="-1"
              [attr.aria-checked]="activeId() === null"
              (click)="pick(null)"
            >
              <mat-icon aria-hidden="true">{{ activeId() === null ? 'check' : '' }}</mat-icon>
              Desactivados
            </button>
            @for (track of tracks(); track track.id) {
              <button
                type="button"
                class="item"
                role="menuitemradio"
                tabindex="-1"
                [attr.aria-checked]="track.id === activeId()"
                (click)="pick(track.id)"
              >
                <mat-icon aria-hidden="true">{{ track.id === activeId() ? 'check' : '' }}</mat-icon>
                {{ track.label }}
              </button>
            }
          </div>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .item {
      display: flex;
      align-items: center;
      gap: 8px;
      width: 100%;
      padding: 8px 16px 8px 8px;
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    .item:hover,
    .item:focus-visible {
      background: rgb(255 255 255 / 14%);
      outline-offset: -2px;
    }

    .item[aria-checked='true'] {
      font-weight: 500;
    }
  `,
})
export class SubtitleMenu {
  readonly tracks = input<readonly SubtitleTrack[]>([]);
  /** Id de la pista activa, o null si están desactivados. */
  readonly activeId = input<string | null>(null);
  /** El usuario eligió una pista (su id) o "Desactivados" (null). */
  readonly choose = output<string | null>();
  readonly openChange = output<boolean>();

  protected readonly open = signal(false);
  protected readonly activeLabel = computed(
    () => this.tracks().find((track) => track.id === this.activeId())?.label ?? 'desactivados',
  );

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');

  protected toggle(): void {
    this.setOpen(!this.open());
  }

  protected pick(id: string | null): void {
    this.choose.emit(id);
    this.setOpen(false);
    this.trigger()?.nativeElement.focus();
  }

  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node | null)) {
      this.setOpen(false);
    }
  }

  onKeydown(event: KeyboardEvent): void {
    // Lo que se teclea aquí no activa los atajos del reproductor (flechas = ±10 s, C, F...).
    event.stopPropagation();
    if (!this.open()) {
      return;
    }
    const items = this.items();
    const index = items.indexOf(this.host.nativeElement.ownerDocument.activeElement as HTMLElement);
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        items[(index + 1) % items.length]?.focus();
        break;
      case 'ArrowUp':
        event.preventDefault();
        items[(index <= 0 ? items.length : index) - 1]?.focus();
        break;
      case 'Home':
        event.preventDefault();
        items[0]?.focus();
        break;
      case 'End':
        event.preventDefault();
        items[items.length - 1]?.focus();
        break;
      case 'Escape':
        event.preventDefault();
        this.setOpen(false);
        this.trigger()?.nativeElement.focus();
        break;
      case 'Tab':
        this.setOpen(false);
        break;
    }
  }

  private items(): HTMLElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));
  }

  private setOpen(value: boolean): void {
    if (this.open() === value) {
      return;
    }
    this.open.set(value);
    this.openChange.emit(value);
    if (value) {
      // Al abrirlo, el foco va a la opción activa (o a la primera).
      afterNextRender(
        () => {
          const items = this.items();
          (items.find((item) => item.getAttribute('aria-checked') === 'true') ?? items[0])?.focus();
        },
        { injector: this.injector },
      );
    }
  }
}
