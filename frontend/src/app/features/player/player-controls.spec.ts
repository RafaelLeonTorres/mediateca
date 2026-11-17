import { TestBed } from '@angular/core/testing';

import { PlayerControls } from './player-controls';

describe('PlayerControls', () => {
  function render(inputs: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(PlayerControls);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    const component = fixture.componentInstance;
    const events = {
      togglePlay: vi.fn(),
      seek: vi.fn(),
      skip: vi.fn(),
      volumeChange: vi.fn(),
      toggleMute: vi.fn(),
      goPrevious: vi.fn(),
      goNext: vi.fn(),
      toggleFullscreen: vi.fn(),
    };
    component.togglePlay.subscribe(events.togglePlay);
    component.seek.subscribe(events.seek);
    component.skip.subscribe(events.skip);
    component.volumeChange.subscribe(events.volumeChange);
    component.toggleMute.subscribe(events.toggleMute);
    component.goPrevious.subscribe(events.goPrevious);
    component.goNext.subscribe(events.goNext);
    component.toggleFullscreen.subscribe(events.toggleFullscreen);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const button = (label: string) => element.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
    const seekBar = () => element.querySelector<HTMLInputElement>('input[aria-label="Barra de avance"]')!;
    return { fixture, element, events, button, seekBar };
  }

  it('el botón de reproducir cambia de nombre según el estado y avisa al pulsarlo', () => {
    const { fixture, events, button } = render();

    button('Reproducir').click();
    expect(events.togglePlay).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('playing', true);
    fixture.detectChanges();
    expect(button('Pausar')).not.toBeNull();
  });

  it('muestra el tiempo transcurrido y la duración', () => {
    const { element } = render({ currentTime: 125, duration: 3725 });

    expect(element.querySelector('.time')!.textContent).toContain('2:05 / 1:02:05');
  });

  it('la barra de avance no mueve el video hasta soltarla y entonces avisa con el segundo', () => {
    const { fixture, element, events, seekBar } = render({ currentTime: 10, duration: 200 });

    seekBar().value = '90';
    seekBar().dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(events.seek).not.toHaveBeenCalled();
    expect(element.querySelector('.time')!.textContent).toContain('1:30 / 3:20');

    seekBar().dispatchEvent(new Event('change'));
    expect(events.seek).toHaveBeenCalledWith(90);
  });

  it('sin duración conocida la barra de avance está deshabilitada', () => {
    const { seekBar } = render({ duration: 0 });

    expect(seekBar().disabled).toBe(true);
  });

  it('las flechas de la barra de avance saltan 10 s y no llegan a los atajos globales', () => {
    const { element, events, seekBar } = render({ duration: 200 });
    const reached = vi.fn();
    document.body.addEventListener('keydown', reached);
    document.body.appendChild(element);

    seekBar().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    seekBar().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true, cancelable: true }));

    expect(events.skip.mock.calls).toEqual([[10], [-10]]);
    expect(reached).not.toHaveBeenCalled();
    document.body.removeEventListener('keydown', reached);
    element.remove();
  });

  it('el volumen y el silencio avisan', () => {
    const { element, events, button } = render({ volume: 0.8 });
    const volume = element.querySelector<HTMLInputElement>('input[aria-label="Volumen"]')!;

    volume.value = '0.3';
    volume.dispatchEvent(new Event('input'));
    button('Silenciar').click();

    expect(events.volumeChange).toHaveBeenCalledWith(0.3);
    expect(events.toggleMute).toHaveBeenCalledTimes(1);
  });

  it('con silencio el botón ofrece quitarlo y el volumen se ve en cero', () => {
    const { element, button } = render({ muted: true, volume: 0.8 });

    expect(button('Quitar el silencio')).not.toBeNull();
    expect(element.querySelector<HTMLInputElement>('input[aria-label="Volumen"]')!.value).toBe('0');
  });

  it('anterior y siguiente están deshabilitados si no hay episodio al que ir', () => {
    const { fixture, events, button } = render({ hasPrevious: false, hasNext: true });

    expect(button('Episodio anterior').disabled).toBe(true);
    button('Episodio siguiente').click();
    expect(events.goNext).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('hasPrevious', true);
    fixture.detectChanges();
    button('Episodio anterior').click();
    expect(events.goPrevious).toHaveBeenCalledTimes(1);
  });

  it('el botón de pantalla completa avisa y cambia de nombre', () => {
    const { fixture, events, button } = render();

    button('Pantalla completa').click();
    expect(events.toggleFullscreen).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('fullscreen', true);
    fixture.detectChanges();
    expect(button('Salir de pantalla completa')).not.toBeNull();
  });

  it('oculta la barra con la clase hidden', () => {
    const { fixture, element } = render({ visible: true });
    expect(element.querySelector('.bar')!.classList).not.toContain('hidden');

    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();

    expect(element.querySelector('.bar')!.classList).toContain('hidden');
  });
});
