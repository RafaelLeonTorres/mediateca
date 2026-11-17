import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { ScanSchedule } from '../../core/admin/admin-api';
import {
  CUSTOM_PRESET,
  SCHEDULE_PRESETS,
  ScanScheduleSection,
  formatRun,
  presetFor,
} from './scan-schedule';

const schedule = (overrides: Partial<ScanSchedule> = {}): ScanSchedule => ({
  enabled: true,
  cronExpression: '0 0 3 * * ?',
  timeZone: 'UTC',
  nextRuns: ['2026-11-18T03:00:00Z', '2026-11-19T03:00:00Z', '2026-11-20T03:00:00Z'],
  updatedAt: '2026-11-17T12:00:00Z',
  ...overrides,
});

describe('presetFor', () => {
  it('reconoce cada frecuencia por su cron exacto', () => {
    for (const preset of SCHEDULE_PRESETS) {
      expect(presetFor(preset.cron)).toBe(preset.value);
    }
    expect(presetFor('  0 0 3 * * ?  ')).toBe('daily');
  });

  it('un cron que no coincide con ninguna frecuencia es personalizado', () => {
    expect(presetFor('0 15 4 * * ?')).toBe(CUSTOM_PRESET);
    expect(presetFor('')).toBe(CUSTOM_PRESET);
  });

  it('ofrece las frecuencias acordadas', () => {
    expect(SCHEDULE_PRESETS.map((preset) => preset.label)).toEqual([
      'Cada hora',
      'Cada 6 horas',
      'Cada 12 horas',
      'Diario a las 3:00',
      'Semanal, domingo a las 3:00',
    ]);
    expect(SCHEDULE_PRESETS.map((preset) => preset.cron)).toEqual([
      '0 0 * * * ?',
      '0 0 */6 * * ?',
      '0 0 */12 * * ?',
      '0 0 3 * * ?',
      '0 0 3 ? * SUN',
    ]);
  });
});

describe('formatRun', () => {
  it('da el formato en español de México', () => {
    const text = formatRun('2026-11-18T03:00:00Z', 'UTC');

    expect(text).toContain('noviembre');
    expect(text).toContain('2026');
  });

  it('usa la zona del servidor para la hora', () => {
    const utc = formatRun('2026-11-18T03:00:00Z', 'UTC');
    const mexico = formatRun('2026-11-18T03:00:00Z', 'America/Mexico_City');

    // Las 3:00 UTC son las 21:00 del día anterior en la Ciudad de México.
    expect(mexico).not.toBe(utc);
    expect(mexico).toContain('17 de noviembre');
  });

  it('con una zona que el navegador no conoce usa la del navegador y no falla', () => {
    expect(() => formatRun('2026-11-18T03:00:00Z', 'Central Standard Time (Mexico)')).not.toThrow();
  });
});

describe('ScanScheduleSection', () => {
  function render() {
    TestBed.configureTestingModule({
      imports: [ScanScheduleSection],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(ScanScheduleSection);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    TestBed.tick();
    const element = fixture.nativeElement as HTMLElement;

    /** Deja que la pantalla se actualice con lo que ya llegó. */
    const settle = async () => {
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
      TestBed.tick();
      await fixture.whenStable();
      fixture.detectChanges();
    };

    /** Responde el GET del horario y deja que el formulario se llene. */
    const load = async (value: ScanSchedule) => {
      http.expectOne('/api/admin/scan-schedule').flush(value);
      // La respuesta llega al recurso de forma asíncrona.
      await TestBed.inject(ApplicationRef).whenStable();
      await settle();
    };

    /** Deja pasar los pasos asíncronos del envío del formulario hasta que sale la petición. */
    const submitForm = async () => {
      element
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
      await new Promise<void>((resolve) => setTimeout(resolve));
    };

    const select = () => element.querySelector<HTMLSelectElement>('select')!;
    const choose = async (value: string) => {
      select().value = value;
      select().dispatchEvent(new Event('input', { bubbles: true }));
      select().dispatchEvent(new Event('change', { bubbles: true }));
      await settle();
    };
    const toggle = () => element.querySelector<HTMLElement>('[role="switch"]')!;
    const cronInput = () => element.querySelector<HTMLInputElement>('input.mat-mdc-input-element');
    const text = () => (element.textContent ?? '').replace(/\s+/g, ' ');

    return { fixture, http, element, load, settle, submitForm, select, choose, toggle, cronInput, text };
  }

  it('muestra el horario guardado con su zona horaria y las próximas ejecuciones', async () => {
    const { element, load, select, toggle, cronInput, text } = render();

    await load(schedule());

    expect(text()).toContain('Escaneo automático');
    expect(text()).toContain('Revisar cambios automáticamente');
    expect(toggle().getAttribute('aria-checked')).toBe('true');
    expect(select().value).toBe('daily');
    expect(cronInput()).toBeNull();
    expect(text()).toContain('Zona horaria del servidor: UTC');
    const runs = element.querySelectorAll('ol.runs li');
    expect(runs.length).toBe(3);
    expect(runs[0].textContent).toContain('noviembre');
  });

  it('un cron que no es de las frecuencias se muestra como personalizado', async () => {
    const { load, select, cronInput, text } = render();

    await load(schedule({ cronExpression: '0 15 4 * * ?' }));

    expect(select().value).toBe('custom');
    expect(cronInput()?.value).toBe('0 15 4 * * ?');
    expect(text()).toContain('Quartz');
  });

  it('desactivado no muestra próximas ejecuciones', async () => {
    const { element, load, toggle, text } = render();

    await load(schedule({ enabled: false, nextRuns: [] }));

    expect(toggle().getAttribute('aria-checked')).toBe('false');
    expect(text()).toContain('El escaneo automático está apagado');
    expect(element.querySelector('ol.runs')).toBeNull();
  });

  it('guarda la frecuencia elegida y avisa que el worker la aplica en menos de un minuto', async () => {
    const { http, load, choose, submitForm, settle, text } = render();
    await load(schedule());

    await choose('hourly');
    await submitForm();

    const request = http.expectOne('/api/admin/scan-schedule');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ enabled: true, cronExpression: '0 0 * * * ?' });
    request.flush(
      schedule({
        cronExpression: '0 0 * * * ?',
        nextRuns: ['2026-11-17T13:00:00Z', '2026-11-17T14:00:00Z', '2026-11-17T15:00:00Z'],
      }),
    );
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();

    expect(text()).toContain('Horario guardado. El worker lo aplica en menos de un minuto.');
  });

  it('apagar el interruptor se guarda con enabled en false', async () => {
    const { http, load, toggle, submitForm, settle } = render();
    await load(schedule());

    toggle().click();
    await settle();
    await submitForm();

    const request = http.expectOne('/api/admin/scan-schedule');
    expect(request.request.body).toEqual({ enabled: false, cronExpression: '0 0 3 * * ?' });
    request.flush(schedule({ enabled: false, nextRuns: [] }));
  });

  it('guarda el cron personalizado que se escribe', async () => {
    const { http, load, choose, cronInput, settle, submitForm } = render();
    await load(schedule());

    await choose('custom');
    const input = cronInput()!;
    input.value = '  0 30 2 ? * MON  ';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    await submitForm();

    const request = http.expectOne('/api/admin/scan-schedule');
    expect(request.request.body).toEqual({ enabled: true, cronExpression: '0 30 2 ? * MON' });
    request.flush(schedule({ cronExpression: '0 30 2 ? * MON' }));
  });

  it('no manda nada si se eligió personalizado y el cron está vacío', async () => {
    const { http, load, choose, submitForm } = render();
    await load(schedule());

    await choose('custom');
    await submitForm();

    http.expectNone('/api/admin/scan-schedule');
  });

  it('muestra los mensajes de validación de la API', async () => {
    const { http, load, choose, submitForm, settle, text } = render();
    await load(schedule());

    await choose('hourly');
    await submitForm();
    http
      .expectOne('/api/admin/scan-schedule')
      .flush(
        { errors: { cronExpression: ['Elige un horario con al menos 5 minutos entre escaneos.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await settle();

    expect(text()).toContain('Elige un horario con al menos 5 minutos entre escaneos.');
    expect(text()).not.toContain('Horario guardado');
  });

  it('avisa si no se pudo cargar el horario', async () => {
    const { http, settle, text } = render();

    http
      .expectOne('/api/admin/scan-schedule')
      .flush(null, { status: 500, statusText: 'Server Error' });
    await TestBed.inject(ApplicationRef).whenStable();
    await settle();

    expect(text()).toContain('No se pudo cargar el horario del escaneo automático.');
  });
});
