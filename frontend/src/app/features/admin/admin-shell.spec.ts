import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AdminShell } from './admin-shell';

describe('AdminShell', () => {
  function render() {
    TestBed.configureTestingModule({
      imports: [AdminShell],
      providers: [
        provideRouter([
          { path: 'admin/bibliotecas', children: [] },
          { path: 'admin/usuarios', children: [] },
        ]),
      ],
    });
    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const link = (name: string) =>
      Array.from(element.querySelectorAll<HTMLAnchorElement>('nav a')).find((a) =>
        (a.textContent ?? '').includes(name),
      )!;
    return { fixture, element, link };
  }

  it('ofrece las dos secciones de la administración', () => {
    const { element, link } = render();

    expect(element.querySelector('nav')?.getAttribute('aria-label')).toBe('Administración');
    expect(link('Bibliotecas').getAttribute('href')).toBe('/admin/bibliotecas');
    expect(link('Usuarios').getAttribute('href')).toBe('/admin/usuarios');
    expect(element.querySelector('router-outlet')).not.toBeNull();
  });

  it('marca la sección actual con aria-current', async () => {
    const { fixture, link } = render();

    await TestBed.inject(Router).navigateByUrl('/admin/usuarios');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(link('Usuarios').getAttribute('aria-current')).toBe('page');
    expect(link('Usuarios').classList).toContain('active');
    expect(link('Bibliotecas').getAttribute('aria-current')).toBeNull();
  });
});
