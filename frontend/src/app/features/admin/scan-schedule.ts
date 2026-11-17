import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormField, form, maxLength, required, submit } from '@angular/forms/signals';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatSlideToggle } from '@angular/material/slide-toggle';

import { AdminApi, type ScanSchedule } from '../../core/admin/admin-api';
import { describeError } from '../../shared/describe-error';

/** Valor de la opción "Personalizado": el horario es una expresión cron escrita a mano. */
export const CUSTOM_PRESET = 'custom';

/** Largo máximo de la expresión (el mismo que valida la API). */
const MAX_CRON_LENGTH = 120;

export interface SchedulePreset {
  value: string;
  label: string;
  /** Expresión cron de Quartz, con segundos. */
  cron: string;
}

/** Frecuencias que se pueden elegir sin escribir el cron. */
export const SCHEDULE_PRESETS: readonly SchedulePreset[] = [
  { value: 'hourly', label: 'Cada hora', cron: '0 0 * * * ?' },
  { value: 'every-6-hours', label: 'Cada 6 horas', cron: '0 0 */6 * * ?' },
  { value: 'every-12-hours', label: 'Cada 12 horas', cron: '0 0 */12 * * ?' },
  { value: 'daily', label: 'Diario a las 3:00', cron: '0 0 3 * * ?' },
  { value: 'weekly', label: 'Semanal, domingo a las 3:00', cron: '0 0 3 ? * SUN' },
];

/** Opción que corresponde a un cron: la que coincide exactamente o, si no hay, "Personalizado". */
export function presetFor(cronExpression: string): string {
  const cron = cronExpression.trim();
  return SCHEDULE_PRESETS.find((preset) => preset.cron === cron)?.value ?? CUSTOM_PRESET;
}

/**
 * Fecha y hora de una ejecución, en español de México y en la zona horaria del servidor. Si el
 * navegador no conoce esa zona (por ejemplo, un servidor Windows), usa la del navegador.
 */
export function formatRun(iso: string, timeZone: string): string {
  const options: Intl.DateTimeFormatOptions = { dateStyle: 'full', timeStyle: 'short' };
  const date = new Date(iso);
  try {
    return new Intl.DateTimeFormat('es-MX', { ...options, timeZone }).format(date);
  } catch {
    return new Intl.DateTimeFormat('es-MX', options).format(date);
  }
}

interface ScheduleModel {
  preset: string;
  cron: string;
}

/** Sección "Escaneo automático" de la administración: activar el escaneo periódico y elegir cuándo corre. */
@Component({
  selector: 'app-scan-schedule',
  imports: [
    FormField,
    MatButton,
    MatError,
    MatFormField,
    MatHint,
    MatInput,
    MatLabel,
    MatProgressBar,
    MatSlideToggle,
  ],
  template: `
    <section class="schedule" aria-labelledby="schedule-title">
      <h2 id="schedule-title">Escaneo automático</h2>
      <p class="intro">
        El worker revisa solo las carpetas de tus bibliotecas con este horario y agrega, actualiza o
        quita lo que cambió.
      </p>

      @if (schedule.isLoading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Cargando el horario" />
      }

      @if (schedule.error()) {
        <p class="error" role="alert">No se pudo cargar el horario del escaneo automático.</p>
      } @else if (description(); as current) {
        <form class="form" novalidate (submit)="$event.preventDefault(); save()">
          <mat-slide-toggle [checked]="enabled()" (change)="enabled.set($event.checked)">
            Revisar cambios automáticamente
          </mat-slide-toggle>

          <mat-form-field appearance="outline">
            <mat-label>Frecuencia</mat-label>
            <select matNativeControl [formField]="scheduleForm.preset">
              @for (option of presets; track option.value) {
                <option [value]="option.value">{{ option.label }}</option>
              }
              <option [value]="customValue">Personalizado</option>
            </select>
          </mat-form-field>

          @if (model().preset === customValue) {
            <mat-form-field appearance="outline">
              <mat-label>Expresión cron de Quartz</mat-label>
              <input
                matInput
                [formField]="scheduleForm.cron"
                autocomplete="off"
                autocapitalize="none"
                spellcheck="false"
              />
              <mat-hint>
                Formato de Quartz, con segundos: segundo minuto hora día mes día-de-la-semana. Ejemplo:
                0 0 3 ? * SUN (domingos a las 3:00).
              </mat-hint>
              <mat-error>Escribe la expresión del horario.</mat-error>
            </mat-form-field>
          }

          @if (error()) {
            <p class="error" role="alert">{{ error() }}</p>
          }
          @if (message()) {
            <p class="success" role="status">{{ message() }}</p>
          }

          <div class="actions">
            <button matButton="filled" type="submit" [disabled]="saving()">Guardar horario</button>
          </div>
        </form>

        <div class="next">
          <p class="zone">
            Zona horaria del servidor: <strong>{{ current.timeZone }}</strong>
          </p>
          @if (!current.enabled) {
            <p class="muted">
              El escaneo automático está apagado. Sigues pudiendo escanear a mano cada biblioteca.
            </p>
          } @else if (current.nextRuns.length > 0) {
            <h3 id="next-runs-title">Próximos escaneos</h3>
            <ol class="runs" aria-labelledby="next-runs-title">
              <!-- Por posición: al cambiar el horario cambian las tres fechas y solo se actualiza el texto. -->
              @for (run of current.nextRuns; track $index) {
                <li>{{ formatRun(run, current.timeZone) }}</li>
              }
            </ol>
          }
        </div>
      }
    </section>
  `,
  styles: `
    .schedule {
      margin-top: 32px;
      padding: 20px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container);
    }

    h2 {
      margin: 0;
      font: var(--mat-sys-title-large);
    }

    h3 {
      margin: 16px 0 8px;
      font: var(--mat-sys-title-small);
    }

    .intro,
    .muted,
    .zone {
      color: var(--mat-sys-on-surface-variant);
    }

    .intro {
      margin: 4px 0 16px;
    }

    .form {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 16px;
    }

    .form mat-form-field {
      width: min(480px, 100%);
    }

    .actions {
      display: flex;
      gap: 8px;
    }

    .next {
      margin-top: 8px;
    }

    .next p {
      margin: 8px 0 0;
    }

    .runs {
      margin: 0;
      padding-left: 20px;
    }

    .error {
      margin: 0;
      color: var(--mat-sys-error);
    }

    .success {
      margin: 0;
      color: var(--mat-sys-primary);
    }
  `,
})
export class ScanScheduleSection {
  private readonly api = inject(AdminApi);

  protected readonly schedule = this.api.scanSchedule();
  protected readonly presets = SCHEDULE_PRESETS;
  protected readonly customValue = CUSTOM_PRESET;
  protected readonly formatRun = formatRun;

  protected readonly enabled = signal(true);

  // Signal Forms: el modelo es una señal y el formulario es un árbol de campos sobre ella.
  protected readonly model = signal<ScheduleModel>({ preset: 'daily', cron: '' });
  protected readonly scheduleForm = form(this.model, (path) => {
    // El cron escrito a mano solo importa cuando se eligió "Personalizado".
    required(path.cron, { when: ({ valueOf }) => valueOf(path.preset) === CUSTOM_PRESET });
    maxLength(path.cron, MAX_CRON_LENGTH);
  });

  /** Lo último que guardó esta pantalla; mientras no guarde nada, lo que trajo la API. */
  private readonly saved = signal<ScanSchedule | null>(null);
  protected readonly description = computed<ScanSchedule | null>(
    () => this.saved() ?? (this.schedule.hasValue() ? (this.schedule.value() ?? null) : null),
  );

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  constructor() {
    // Al llegar el horario guardado, el formulario se llena con él.
    effect(() => {
      if (this.schedule.hasValue()) {
        const value = this.schedule.value();
        if (value) {
          untracked(() => this.fill(value));
        }
      }
    });
  }

  protected async save(): Promise<void> {
    this.error.set(null);
    this.message.set(null);
    await submit(this.scheduleForm, async () => {
      this.saving.set(true);
      try {
        const result = await this.api.updateScanSchedule({
          enabled: this.enabled(),
          cronExpression: this.cronToSave(),
        });
        this.saved.set(result);
        this.fill(result);
        this.message.set('Horario guardado. El worker lo aplica en menos de un minuto.');
      } catch (error) {
        this.error.set(describeError(error));
      } finally {
        this.saving.set(false);
      }
    });
  }

  private fill(value: ScanSchedule): void {
    const preset = presetFor(value.cronExpression);
    this.enabled.set(value.enabled);
    this.model.set({ preset, cron: preset === CUSTOM_PRESET ? value.cronExpression : '' });
  }

  private cronToSave(): string {
    const { preset, cron } = this.model();
    return SCHEDULE_PRESETS.find((option) => option.value === preset)?.cron ?? cron.trim();
  }
}
