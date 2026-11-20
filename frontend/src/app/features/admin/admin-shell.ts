import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

/** Marco de la administración: las secciones (Bibliotecas y Usuarios) como pestañas sobre cada pantalla. */
@Component({
  selector: 'app-admin-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <nav class="tabs" aria-label="Administración">
      <a routerLink="/admin/bibliotecas" routerLinkActive="active" ariaCurrentWhenActive="page">
        Bibliotecas
      </a>
      <a routerLink="/admin/usuarios" routerLinkActive="active" ariaCurrentWhenActive="page">
        Usuarios
      </a>
    </nav>
    <router-outlet />
  `,
  styles: `
    /*
     * La línea de abajo es una sombra interior y no un borde: así la raya de la pestaña activa la tapa sin
     * salirse del contenedor. Con un margen negativo se salía un píxel y, como el contenedor se desplaza a lo
     * ancho, Windows mostraba una barra de desplazamiento vertical.
     */
    .tabs {
      display: flex;
      gap: 4px;
      margin-bottom: 24px;
      overflow-x: auto;
      box-shadow: inset 0 -1px 0 var(--mat-sys-outline-variant);
    }

    .tabs a {
      display: inline-flex;
      align-items: center;
      min-height: 48px;
      padding: 0 20px;
      border-bottom: 3px solid transparent;
      border-radius: var(--mat-sys-corner-small) var(--mat-sys-corner-small) 0 0;
      /* El fondo al pasar el ratón no tapa la línea de abajo. */
      background-clip: padding-box;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-title-small);
      text-decoration: none;
      outline: none;
      transition: color 150ms ease;
    }

    .tabs a:hover {
      color: var(--mat-sys-on-surface);
      background: var(--mat-sys-surface-container-low);
    }

    .tabs a.active {
      color: var(--mat-sys-primary);
      border-bottom-color: var(--mat-sys-primary);
    }

    .tabs a:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: -3px;
    }

    @media (prefers-reduced-motion: reduce) {
      .tabs a {
        transition: none;
      }
    }
  `,
})
export class AdminShell {}
