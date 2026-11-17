import { TestBed } from '@angular/core/testing';

import { DEFAULT_RESCALE, type RescaleSettings } from './rescale-settings';
import { RescalePanel } from './rescale-panel';

describe('RescalePanel', () => {
  function render(settings: RescaleSettings = DEFAULT_RESCALE, supported = true, info: string | null = null) {
    const fixture = TestBed.createComponent(RescalePanel);
    fixture.componentRef.setInput('settings', settings);
    fixture.componentRef.setInput('supported', supported);
    fixture.componentRef.setInput('info', info);
    const changes: RescaleSettings[] = [];
    let resets = 0;
    fixture.componentInstance.settingsChange.subscribe((next) => changes.push(next));
    fixture.componentInstance.reset.subscribe(() => resets++);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = () => element.querySelector<HTMLButtonElement>('button[aria-haspopup="dialog"]')!;
    const open = () => {
      trigger().click();
      fixture.detectChanges();
    };
    return { fixture, element, trigger, open, changes, resets: () => resets };
  }

  it('sin WebGPU el botón está deshabilitado, con el motivo a la vista y en el texto para lectores', () => {
    const { element, trigger, open } = render(DEFAULT_RESCALE, false);

    expect(trigger().disabled).toBe(true);
    expect(element.querySelector('.hint')!.getAttribute('title')).toBe(
      'Tu navegador no tiene WebGPU; usa Chrome o Edge actualizados.',
    );
    expect(element.querySelector('[role="note"]')!.textContent).toContain('usa Chrome o Edge actualizados');
    open();
    expect(element.querySelector('[role="dialog"]')).toBeNull();
  });

  it('con WebGPU el botón se abre y muestra todas las opciones en español', () => {
    const { element, trigger, open } = render();

    expect(trigger().disabled).toBe(false);
    open();

    const panel = element.querySelector('[role="dialog"]')!;
    const text = panel.textContent!;
    for (const label of [
      'Original', 'Mejorado', 'Comparar', 'Resolución de salida', 'Pantalla', '720p', '1080p', '1440p', '4K',
      'Escalado', 'Bilineal', 'Bicúbico', 'Lanczos 2', 'Lanczos 3', 'Nitidez adaptativa', 'Reducción de ruido',
      'Anti-ringing', 'Dithering', 'Restablecer',
    ]) {
      expect(text).toContain(label);
    }
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
  });

  it('muestra la vista actual marcada', () => {
    const { element, open } = render({ ...DEFAULT_RESCALE, view: 'compare' });
    open();

    const radios = Array.from(element.querySelectorAll<HTMLInputElement>('fieldset input'));
    expect(radios.map((radio) => radio.checked)).toEqual([false, false, true]);
  });

  it('cambiar la vista avisa con los ajustes nuevos', () => {
    const { element, open, changes } = render();
    open();

    element.querySelectorAll<HTMLInputElement>('fieldset input')[1].click();

    expect(changes).toEqual([{ ...DEFAULT_RESCALE, view: 'enhanced' }]);
  });

  it('cambiar resolución y escalado avisa y descarta valores que no existen', () => {
    const { element, open, changes } = render();
    open();
    const [output, kernel] = Array.from(element.querySelectorAll<HTMLSelectElement>('select'));

    output.value = '2160';
    output.dispatchEvent(new Event('change'));
    kernel.value = 'bicubic';
    kernel.dispatchEvent(new Event('change'));

    expect(changes).toEqual([
      { ...DEFAULT_RESCALE, outputHeight: 2160 },
      { ...DEFAULT_RESCALE, kernel: 'bicubic' },
    ]);
  });

  it('los controles deslizantes van de 0 a 100 % y avisan en fracciones', () => {
    const { element, open, changes } = render({ ...DEFAULT_RESCALE, sharpen: 0.4 });
    open();
    const sliders = Array.from(element.querySelectorAll<HTMLInputElement>('input[type="range"]'));

    expect(sliders).toHaveLength(3);
    expect(sliders.every((slider) => slider.min === '0' && slider.max === '100')).toBe(true);
    expect(sliders[0].value).toBe('40');
    expect(element.textContent).toContain('40 %');

    sliders[1].value = '25';
    sliders[1].dispatchEvent(new Event('input'));

    expect(changes).toEqual([{ ...DEFAULT_RESCALE, sharpen: 0.4, denoise: 0.25 }]);
  });

  it('el dithering se activa y desactiva', () => {
    const { element, open, changes } = render({ ...DEFAULT_RESCALE, dither: true });
    open();

    element.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();

    expect(changes[0].dither).toBe(false);
  });

  it('Restablecer avisa sin cambiar nada por su cuenta', () => {
    const { element, open, changes, resets } = render({ ...DEFAULT_RESCALE, sharpen: 1 });
    open();

    Array.from(element.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.includes('Restablecer'))!
      .click();

    expect(resets()).toBe(1);
    expect(changes).toEqual([]);
  });

  it('muestra los datos de la GPU cuando los hay', () => {
    const { element, open } = render(DEFAULT_RESCALE, true, 'nvidia ampere · 60 cuadros/s');
    open();

    expect(element.querySelector('.info')!.textContent).toContain('nvidia ampere · 60 cuadros/s');
  });

  it('Escape cierra el panel y no llega a los atajos del reproductor', () => {
    const { fixture, element, trigger, open } = render();
    const reached = vi.fn();
    document.body.addEventListener('keydown', reached);
    document.body.appendChild(element);
    open();

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(reached).not.toHaveBeenCalled();
    document.body.removeEventListener('keydown', reached);
    element.remove();
  });
});
