import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { App } from './app';
import { SessionStore } from './core/auth/session-store';

describe('App', () => {
  function render(apiError: boolean) {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: { apiError: signal(apiError) } },
      ],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('muestra la app cuando la API responde', () => {
    const element = render(false);

    expect(element.querySelector('router-outlet')).not.toBeNull();
  });

  it('avisa cuando la API no responde', () => {
    const element = render(true);

    expect(element.textContent).toContain('No hay conexión con la API');
    expect(element.querySelector('router-outlet')).toBeNull();
  });
});
