import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import type { Subscription } from 'rxjs';

import {
  MAX_SUBTITLE_BYTES,
  SUBTITLE_FILE_TYPES,
  SUBTITLE_LANGUAGES,
  SubtitlesApi,
  type SubtitleTrack,
  guessSubtitleLanguage,
  isSubtitleFileName,
  subtitleErrorMessage,
} from '../../core/playback/subtitles-api';
import { readSubtitlePreference } from './player-preferences';

/**
 * Botón CC con su menú, siempre a la vista aunque el video no traiga subtítulos: "Desactivados", una opción por
 * pista y "Cargar archivo…" para subir un .srt, .vtt, .ass o .ssa propio. Al elegir el archivo, el menú se vuelve un
 * formulario corto con el idioma (el que dice el nombre del archivo o, si no dice, el último elegido). Si la pista
 * activa es una subida que el usuario puede borrar, también ofrece "Borrar el archivo subido".
 *
 * El menú se maneja con el teclado (flechas, Inicio, Fin, Escape) y avisa con `openChange` cuando se abre o se
 * cierra, para que la barra de controles no se oculte mientras está abierto.
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
        <mat-icon aria-hidden="true">{{
          activeId() ? 'closed_caption' : 'closed_caption_off'
        }}</mat-icon>
      </button>
      <input #picker type="file" hidden [accept]="fileTypes" (change)="onFilePicked($event)" />

      @if (open()) {
        @if (file(); as chosen) {
          <form
            class="popup panel"
            role="dialog"
            aria-label="Agregar subtítulos"
            (submit)="send($event)"
          >
            <p class="name" [attr.title]="chosen.name">{{ chosen.name }}</p>
            <label class="field">
              <span>Idioma</span>
              <select #language [disabled]="busy()">
                @for (option of languages; track option.code) {
                  <option [value]="option.code" [selected]="option.code === guess()">
                    {{ option.label }}
                  </option>
                }
              </select>
            </label>
            @if (error(); as message) {
              <p class="error" role="alert">{{ message }}</p>
            }
            <div class="footer">
              <button type="button" class="action" (click)="backToList()">Cancelar</button>
              <button type="submit" class="action primary" [disabled]="busy()">
                {{ busy() ? 'Subiendo…' : 'Agregar' }}
              </button>
            </div>
          </form>
        } @else {
          <div class="popup" role="menu" aria-label="Subtítulos">
            @if (tracks().length === 0) {
              <div class="item note" role="menuitem" aria-disabled="true" tabindex="-1">
                Este video no tiene subtítulos
              </div>
            } @else {
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
                  <mat-icon aria-hidden="true">{{
                    track.id === activeId() ? 'check' : ''
                  }}</mat-icon>
                  {{ track.label }}
                </button>
              }
            }
            <div class="sep" role="separator"></div>
            <button
              #upload
              type="button"
              class="item"
              role="menuitem"
              tabindex="-1"
              (click)="pickFile()"
            >
              <mat-icon aria-hidden="true">upload_file</mat-icon>
              Cargar archivo…
            </button>
            @if (removable(); as track) {
              <button
                type="button"
                class="item"
                role="menuitem"
                tabindex="-1"
                [attr.aria-disabled]="busy() || null"
                (click)="remove(track)"
              >
                <mat-icon aria-hidden="true">delete</mat-icon>
                Borrar el archivo subido
              </button>
            }
            @if (error(); as message) {
              <p class="error" role="alert">{{ message }}</p>
            }
          </div>
        }
      }
    </div>
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

    .item:not([aria-disabled='true']):hover,
    .item:focus-visible {
      background: rgb(255 255 255 / 14%);
      outline-offset: -2px;
    }

    .item[aria-checked='true'] {
      font-weight: 500;
    }

    .item[aria-disabled='true'] {
      color: rgb(255 255 255 / 64%);
      cursor: default;
    }

    .note {
      padding-left: 16px;
    }

    .sep {
      height: 1px;
      margin: 6px 0;
      background: rgb(255 255 255 / 16%);
    }

    .error {
      margin: 4px 16px 0;
      color: #ffb4ab;
      font: var(--mat-sys-body-small);
    }

    .popup.panel {
      display: flex;
      flex-direction: column;
      gap: 12px;
      width: 280px;
      padding: 16px;
      box-sizing: border-box;
    }

    .panel .error {
      margin: 0;
    }

    .name {
      margin: 0;
      overflow: hidden;
      font: var(--mat-sys-title-small);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .field > span {
      color: rgb(255 255 255 / 72%);
      font: var(--mat-sys-label-medium);
    }

    select {
      padding: 6px;
      border: 1px solid rgb(255 255 255 / 30%);
      border-radius: 8px;
      background: #1c1c20;
      color: #fff;
      font: inherit;
    }

    .footer {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
    }

    .action {
      padding: 6px 14px;
      border: 1px solid rgb(255 255 255 / 40%);
      border-radius: 16px;
      background: transparent;
      color: #fff;
      font: inherit;
      cursor: pointer;
    }

    .action.primary {
      border-color: transparent;
      background: var(--mat-sys-primary);
      color: var(--mat-sys-on-primary);
    }

    .action:disabled {
      opacity: 0.6;
      cursor: default;
    }

    select:focus-visible,
    .action:focus-visible {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }
  `,
})
export class SubtitleMenu {
  /** El video de las pistas: a él se suben los archivos. */
  readonly itemId = input<string | null>(null);
  readonly tracks = input<readonly SubtitleTrack[]>([]);
  /** Id de la pista activa, o null si están desactivados. */
  readonly activeId = input<string | null>(null);
  /** El usuario eligió una pista (su id) o "Desactivados" (null). */
  readonly choose = output<string | null>();
  /** Se subió un archivo: su pista nueva. El reproductor vuelve a pedir la lista y la activa. */
  readonly added = output<SubtitleTrack>();
  /** Se borró una pista subida (su id). */
  readonly removed = output<string>();
  readonly openChange = output<boolean>();

  protected readonly fileTypes = SUBTITLE_FILE_TYPES;
  protected readonly languages = SUBTITLE_LANGUAGES;
  protected readonly open = signal(false);
  /** El archivo elegido para subir: mientras haya uno, el menú muestra el formulario. */
  protected readonly file = signal<File | null>(null);
  /** El idioma que propone el formulario. */
  protected readonly guess = signal('');
  /** Se está subiendo o borrando un archivo. */
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly activeLabel = computed(
    () => this.tracks().find((track) => track.id === this.activeId())?.label ?? 'desactivados',
  );
  /** La pista activa, si es una subida que este usuario puede borrar. */
  protected readonly removable = computed(() => {
    const track = this.tracks().find((candidate) => candidate.id === this.activeId());
    return track?.source === 'uploaded' && track.canDelete ? track : null;
  });

  private readonly api = inject(SubtitlesApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');
  private readonly picker = viewChild<ElementRef<HTMLInputElement>>('picker');
  private readonly language = viewChild<ElementRef<HTMLSelectElement>>('language');
  private readonly upload = viewChild<ElementRef<HTMLButtonElement>>('upload');
  private request: Subscription | undefined;

  constructor() {
    // Otro video: lo que se estaba subiendo o borrando ya no es de este.
    effect(() => {
      this.itemId();
      untracked(() => {
        this.stopRequest();
        this.file.set(null);
        this.error.set(null);
      });
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  protected toggle(): void {
    this.setOpen(!this.open());
  }

  protected pick(id: string | null): void {
    this.choose.emit(id);
    this.setOpen(false);
    this.trigger()?.nativeElement.focus();
  }

  /** "Cargar archivo…": abre el selector de archivos del sistema. */
  protected pickFile(): void {
    this.error.set(null);
    this.picker()?.nativeElement.click();
  }

  protected onFilePicked(event: Event): void {
    const field = event.target as HTMLInputElement;
    const picked = field.files?.[0] ?? null;
    // Se vacía para que elegir el mismo archivo otra vez también avise.
    field.value = '';
    if (!picked) {
      return;
    }
    if (!isSubtitleFileName(picked.name)) {
      this.error.set(
        `«${picked.name}» no es un subtítulo: elige un archivo .srt, .vtt, .ass o .ssa.`,
      );
      return;
    }
    if (picked.size > MAX_SUBTITLE_BYTES) {
      this.error.set('El archivo pesa más de 8 MB.');
      return;
    }
    if (picked.size === 0) {
      this.error.set('El archivo está vacío.');
      return;
    }
    this.error.set(null);
    this.guess.set(guessSubtitleLanguage(picked.name, readSubtitlePreference()));
    this.file.set(picked);
    this.setOpen(true);
    this.focusAfterRender(() => this.language()?.nativeElement);
  }

  protected send(event: Event): void {
    event.preventDefault();
    const file = this.file();
    const itemId = this.itemId();
    if (!file || !itemId || this.busy()) {
      return;
    }
    const language = this.language()?.nativeElement.value ?? this.guess();
    this.busy.set(true);
    this.error.set(null);
    this.request = this.api.upload(itemId, file, language).subscribe({
      next: (track) => {
        this.busy.set(false);
        this.finish();
        this.added.emit(track);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(
          subtitleErrorMessage(error, 'No se pudo subir el archivo. Inténtalo de nuevo.'),
        );
      },
    });
  }

  /** Cancelar (o Escape) en el formulario: vuelve al menú y deja de subir. */
  protected backToList(): void {
    this.stopRequest();
    this.file.set(null);
    this.error.set(null);
    this.focusAfterRender(() => this.upload()?.nativeElement);
  }

  protected remove(track: SubtitleTrack): void {
    const itemId = this.itemId();
    if (!itemId || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.request = this.api.remove(itemId, track.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.finish();
        this.removed.emit(track.id);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(
          subtitleErrorMessage(error, 'No se pudo borrar el archivo. Inténtalo de nuevo.'),
        );
      },
    });
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
    if (this.file()) {
      // En el formulario las flechas son de la lista de idiomas; Escape vuelve al menú.
      if (event.key === 'Escape') {
        event.preventDefault();
        this.backToList();
      }
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
    return Array.from(
      this.host.nativeElement.querySelectorAll<HTMLElement>(
        '[role="menuitemradio"], [role="menuitem"]',
      ),
    );
  }

  /** Terminó una subida o un borrado: se cierra y, si seguía abierto, el foco vuelve al botón CC. */
  private finish(): void {
    const wasOpen = this.open();
    this.setOpen(false);
    if (wasOpen) {
      this.trigger()?.nativeElement.focus();
    }
  }

  private stopRequest(): void {
    this.request?.unsubscribe();
    this.request = undefined;
    this.busy.set(false);
  }

  private focusAfterRender(target: () => HTMLElement | undefined): void {
    afterNextRender(() => target()?.focus(), { injector: this.injector });
  }

  private setOpen(value: boolean): void {
    if (this.open() === value) {
      return;
    }
    this.open.set(value);
    this.openChange.emit(value);
    // Lo que se mostraba la última vez (un formulario a medias o un error viejo) no sigue.
    this.error.set(null);
    if (value) {
      // Al abrirlo, el foco va a la opción activa (o a la primera).
      this.focusAfterRender(() => {
        const items = this.items();
        return items.find((item) => item.getAttribute('aria-checked') === 'true') ?? items[0];
      });
    } else {
      // Una subida en curso sigue y avisa al terminar.
      this.file.set(null);
    }
  }
}
