import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, linkedSignal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatOption, MatSelect } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { SessionStore } from '../../core/auth/session-store';
import {
  EMPTY_ITEMS_QUERY,
  ITEM_SORT_LABELS,
  ITEMS_PAGE_SIZE,
  LIBRARY_TYPE_LABELS,
  LibrariesApi,
  type ItemSort,
  type ItemsQuery,
} from '../../core/libraries/libraries-api';
import { PosterCard } from '../../shared/poster-card/poster-card';

/** Espera, en milisegundos, antes de buscar mientras se escribe. */
export const SEARCH_DEBOUNCE_MS = 300;

/** La API responde 404 tanto si la biblioteca no existe como si el usuario no tiene acceso a ella. */
function isNotFound(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404;
}

/** Cuadrícula de pósters de una biblioteca, con búsqueda, filtros, orden y paginación. */
@Component({
  selector: 'app-library-page',
  imports: [
    MatButton,
    MatFormField,
    MatIcon,
    MatInput,
    MatLabel,
    MatOption,
    MatProgressBar,
    MatSelect,
    PosterCard,
    RouterLink,
  ],
  template: `
    @if (notFound()) {
      <section class="empty" aria-labelledby="missing-title">
        <h1 id="missing-title">No se encontró la biblioteca</h1>
        <p>Puede que se haya eliminado o que no tengas acceso.</p>
        <a matButton="filled" routerLink="/">Volver al inicio</a>
      </section>
    } @else {
      <header class="head">
        <h1>{{ library()?.name ?? 'Biblioteca' }}</h1>
        @if (library(); as current) {
          <p class="type">{{ typeLabel(current.type) }}</p>
        }
      </header>

      <form class="filters" role="search" (submit)="$event.preventDefault()">
        <mat-form-field appearance="outline" class="search">
          <mat-label>Buscar por título</mat-label>
          <input
            matInput
            type="search"
            autocomplete="off"
            [value]="searchText()"
            (input)="onSearch($any($event.target).value)"
          />
          <mat-icon matSuffix aria-hidden="true">search</mat-icon>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Género</mat-label>
          <mat-select [value]="query().genre" (selectionChange)="setGenre($event.value)">
            <mat-option value="">Todos</mat-option>
            @for (genre of genres(); track genre) {
              <mat-option [value]="genre">{{ genre }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Año</mat-label>
          <mat-select [value]="yearValue()" (selectionChange)="setYear($event.value)">
            <mat-option value="">Todos</mat-option>
            @for (year of years(); track year) {
              <mat-option [value]="year.toString()">{{ year }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Ordenar por</mat-label>
          <mat-select [value]="query().sort" (selectionChange)="setSort($event.value)">
            @for (option of sortOptions; track option.value) {
              <mat-option [value]="option.value">{{ option.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </form>

      @if (page.isLoading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Cargando elementos" />
      }

      @if (page.error()) {
        <p class="error" role="alert">No se pudo cargar la biblioteca. Inténtalo de nuevo.</p>
      } @else if (ready()) {
        @if (items().length > 0) {
          <p class="count" aria-live="polite">
            {{ total() }} {{ total() === 1 ? 'resultado' : 'resultados' }}
          </p>
          <ul class="grid">
            @for (item of items(); track item.id) {
              <li>
                <app-poster-card
                  [id]="item.id"
                  [title]="item.title"
                  [kind]="item.kind"
                  [subtitle]="item.artist ?? item.year?.toString() ?? ''"
                  [hasImage]="item.hasImage"
                />
              </li>
            }
          </ul>

          @if (totalPages() > 1) {
            <nav class="pager" aria-label="Paginación">
              <button
                matButton="outlined"
                type="button"
                [disabled]="query().page <= 1"
                (click)="goTo(query().page - 1)"
              >
                Anterior
              </button>
              <span aria-live="polite">Página {{ query().page }} de {{ totalPages() }}</span>
              <button
                matButton="outlined"
                type="button"
                [disabled]="query().page >= totalPages()"
                (click)="goTo(query().page + 1)"
              >
                Siguiente
              </button>
            </nav>
          }
        } @else if (hasFilters()) {
          <section class="empty" aria-labelledby="empty-title">
            <h2 id="empty-title">Sin resultados</h2>
            <p>Ningún elemento coincide con la búsqueda o los filtros.</p>
            <button matButton="filled" type="button" (click)="clearFilters()">Quitar filtros</button>
          </section>
        } @else {
          <section class="empty" aria-labelledby="empty-title">
            <h2 id="empty-title">Esta biblioteca está vacía</h2>
            @if (session.user()?.isAdmin) {
              <p>Escanea la biblioteca para que Mediateca encuentre tus archivos.</p>
              <a matButton="filled" routerLink="/admin/bibliotecas">Ir a la administración</a>
            } @else {
              <p>Todavía no hay nada aquí. Pídele al administrador que escanee la biblioteca.</p>
            }
          </section>
        }
      }
    }
  `,
  styleUrls: ['../../shared/poster-grid.scss'],
  styles: `
    .head {
      margin-bottom: 16px;
    }

    h1 {
      margin: 0;
      font: var(--mat-sys-headline-medium);
    }

    .type {
      margin: 4px 0 0;
      color: var(--mat-sys-on-surface-variant);
    }

    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 0 12px;
    }

    .filters .search {
      flex: 1 1 260px;
    }

    .filters mat-form-field:not(.search) {
      flex: 0 1 180px;
    }

    .count {
      margin: 8px 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }

    .error {
      color: var(--mat-sys-error);
    }

    .pager {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 16px;
      margin-top: 32px;
    }

    .empty {
      padding: 32px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container-low);
    }

    .empty h1,
    .empty h2 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-medium);
    }

    .empty p {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class LibraryPage {
  /** Id de la biblioteca, desde la ruta /bibliotecas/:id. */
  readonly id = input.required<string>();

  protected readonly session = inject(SessionStore);
  private readonly api = inject(LibrariesApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly sortOptions = (Object.keys(ITEM_SORT_LABELS) as ItemSort[]).map((value) => ({
    value,
    label: ITEM_SORT_LABELS[value],
  }));

  /** Al cambiar de biblioteca se empieza de cero. */
  protected readonly query = linkedSignal<ItemsQuery>(() => {
    this.id();
    return { ...EMPTY_ITEMS_QUERY };
  });
  /** Lo que se ve en la caja de búsqueda; la consulta se actualiza después de una pausa. */
  protected readonly searchText = linkedSignal<string>(() => {
    this.id();
    return '';
  });

  private readonly libraries = this.api.libraries();
  protected readonly filters = this.api.filters(() => this.id());
  protected readonly page = this.api.items(
    () => this.id(),
    () => this.query(),
  );

  // value() lanza si el recurso está en error (por ejemplo, un 404): primero se pregunta si tiene valor.
  protected readonly library = computed(() =>
    this.libraries.hasValue() ? this.libraries.value()?.find((l) => l.id === this.id()) : undefined,
  );
  protected readonly genres = computed(() =>
    this.filters.hasValue() ? (this.filters.value()?.genres ?? []) : [],
  );
  protected readonly years = computed(() =>
    this.filters.hasValue() ? (this.filters.value()?.years ?? []) : [],
  );
  private readonly pageValue = computed(() => (this.page.hasValue() ? this.page.value() : undefined));
  protected readonly ready = computed(() => this.pageValue() !== undefined);
  protected readonly items = computed(() => this.pageValue()?.items ?? []);
  protected readonly total = computed(() => this.pageValue()?.total ?? 0);
  /** La biblioteca no existe o no es de las que puede ver este usuario. */
  protected readonly notFound = computed(
    () => isNotFound(this.page.error()) || isNotFound(this.filters.error()),
  );
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.total() / ITEMS_PAGE_SIZE)),
  );
  protected readonly yearValue = computed(() => this.query().year?.toString() ?? '');
  protected readonly hasFilters = computed(() => {
    const q = this.query();
    return q.search.trim() !== '' || q.genre !== '' || q.year !== null;
  });

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.timer));
  }

  protected typeLabel(type: keyof typeof LIBRARY_TYPE_LABELS): string {
    return LIBRARY_TYPE_LABELS[type];
  }

  protected onSearch(text: string): void {
    this.searchText.set(text);
    clearTimeout(this.timer);
    this.timer = setTimeout(
      () => this.query.update((q) => ({ ...q, search: text, page: 1 })),
      SEARCH_DEBOUNCE_MS,
    );
  }

  protected setGenre(genre: string): void {
    this.query.update((q) => ({ ...q, genre, page: 1 }));
  }

  protected setYear(year: string): void {
    this.query.update((q) => ({ ...q, year: year ? Number(year) : null, page: 1 }));
  }

  protected setSort(sort: ItemSort): void {
    this.query.update((q) => ({ ...q, sort, page: 1 }));
  }

  protected goTo(pageNumber: number): void {
    this.query.update((q) => ({
      ...q,
      page: Math.min(Math.max(1, pageNumber), this.totalPages()),
    }));
    document.documentElement.scrollTop = 0;
  }

  protected clearFilters(): void {
    clearTimeout(this.timer);
    this.searchText.set('');
    this.query.update((q) => ({ ...q, search: '', genre: '', year: null, page: 1 }));
  }
}
