import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { MAX_SUBTITLE_BYTES, type SubtitleTrack } from '../../core/playback/subtitles-api';
import { SUBTITLE_STORAGE_KEY } from './player-preferences';
import { SubtitleMenu } from './subtitle-menu';

describe('SubtitleMenu', () => {
  function track(
    id: string,
    label: string,
    language: string | null = null,
    extra: Partial<SubtitleTrack> = {},
  ): SubtitleTrack {
    return {
      id,
      label,
      language,
      codec: 'subrip',
      source: 'embedded',
      isDefault: false,
      isForced: false,
      url: `/api/items/v1/subtitles/${id}.vtt`,
      canDelete: false,
      ...extra,
    };
  }

  const tracks = [track('e2', 'Español', 'es'), track('e3', 'Inglés', 'en')];
  const srt = '1\n00:00:01,000 --> 00:00:02,000\nHola\n';

  /** El texto de una opción sin el de su ícono (los íconos de Material son texto: "check", "delete"). */
  const text = (element: Element) => {
    const copy = element.cloneNode(true) as Element;
    copy.querySelectorAll('mat-icon').forEach((icon) => icon.remove());
    return copy.textContent?.replace(/\s+/g, ' ').trim();
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    localStorage.clear();
  });

  function render(list: SubtitleTrack[] = tracks, activeId: string | null = null) {
    const fixture = TestBed.createComponent(SubtitleMenu);
    fixture.componentRef.setInput('itemId', 'v1');
    fixture.componentRef.setInput('tracks', list);
    fixture.componentRef.setInput('activeId', activeId);
    const chosen: (string | null)[] = [];
    const opened: boolean[] = [];
    const added: SubtitleTrack[] = [];
    const removed: string[] = [];
    fixture.componentInstance.choose.subscribe((id) => chosen.push(id));
    fixture.componentInstance.openChange.subscribe((open) => opened.push(open));
    fixture.componentInstance.added.subscribe((value) => added.push(value));
    fixture.componentInstance.removed.subscribe((id) => removed.push(id));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = () => element.querySelector<HTMLButtonElement>('button[aria-haspopup="menu"]')!;
    const items = () =>
      Array.from(element.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]'));
    const actions = () => Array.from(element.querySelectorAll<HTMLElement>('[role="menuitem"]'));
    const open = () => {
      trigger().click();
      fixture.detectChanges();
    };
    /** Simula elegir un archivo en el selector del sistema. */
    const pickFile = (file: File) => {
      const input = element.querySelector<HTMLInputElement>('input[type="file"]')!;
      Object.defineProperty(input, 'files', { value: [file], configurable: true });
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const submit = () => {
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      fixture.detectChanges();
    };
    return {
      fixture,
      element,
      trigger,
      items,
      actions,
      open,
      pickFile,
      submit,
      chosen,
      opened,
      added,
      removed,
    };
  }

  const http = () => TestBed.inject(HttpTestingController);
  const uploadRequest = () =>
    http().expectOne(
      (request) => request.method === 'POST' && request.url === '/api/items/v1/subtitles',
    );

  it('sin pistas el botón CC sigue a la vista: el menú lo dice y ofrece cargar un archivo', () => {
    const { element, trigger, items, actions, open } = render([]);

    expect(trigger().getAttribute('aria-label')).toBe('Subtítulos: desactivados');
    open();

    expect(items()).toHaveLength(0);
    expect(actions().map(text)).toEqual(['Este video no tiene subtítulos', 'Cargar archivo…']);
    expect(actions()[0].getAttribute('aria-disabled')).toBe('true');
    expect(element.querySelector('input[type="file"]')!.getAttribute('accept')).toBe(
      '.srt,.vtt,.ass,.ssa',
    );
  });

  it('con pistas muestra el botón CC cerrado y dice que están desactivados', () => {
    const { element, trigger } = render();

    expect(trigger().getAttribute('aria-label')).toBe('Subtítulos: desactivados');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(element.querySelector('[role="menu"]')).toBeNull();
  });

  it('al abrirlo ofrece Desactivados, una opción por pista y Cargar archivo…, con la activa marcada', () => {
    const { trigger, items, actions, open, opened } = render(tracks, 'e3');

    open();

    expect(opened).toEqual([true]);
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(trigger().getAttribute('aria-label')).toBe('Subtítulos: Inglés');
    expect(items().map((item) => item.getAttribute('aria-checked'))).toEqual([
      'false',
      'false',
      'true',
    ]);
    expect(items()[0].textContent).toContain('Desactivados');
    expect(items()[1].textContent).toContain('Español');
    expect(items()[2].textContent).toContain('Inglés');
    expect(actions()).toHaveLength(1);
    expect(actions()[0].textContent).toContain('Cargar archivo…');
  });

  it('elegir una pista avisa con su id y cierra el menú', () => {
    const { fixture, element, items, open, chosen, opened } = render();
    open();

    items()[1].click();
    fixture.detectChanges();

    expect(chosen).toEqual(['e2']);
    expect(opened).toEqual([true, false]);
    expect(element.querySelector('[role="menu"]')).toBeNull();
  });

  it('elegir Desactivados avisa con null', () => {
    const { items, open, chosen } = render(tracks, 'e2');
    open();

    items()[0].click();

    expect(chosen).toEqual([null]);
  });

  it('las flechas recorren las opciones, Escape cierra y devuelve el foco al botón', () => {
    const { fixture, element, trigger, items, actions, open, opened } = render();
    document.body.appendChild(element);
    open();
    items()[0].focus();

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(items()[1]);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    expect(document.activeElement).toBe(actions()[0]);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(items()[0]);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(element.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger());
    expect(opened).toEqual([true, false]);
    element.remove();
  });

  it('las teclas del menú no llegan a los atajos del reproductor', () => {
    const { element } = render();
    const reached = vi.fn();
    document.body.addEventListener('keydown', reached);
    document.body.appendChild(element);

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));

    expect(reached).not.toHaveBeenCalled();
    document.body.removeEventListener('keydown', reached);
    element.remove();
  });

  it('un clic fuera lo cierra', () => {
    const { fixture, element, open } = render();
    open();

    document.body.click();
    fixture.detectChanges();

    expect(element.querySelector('[role="menu"]')).toBeNull();
  });

  describe('cargar un archivo', () => {
    it('Cargar archivo… abre el selector de archivos del sistema', () => {
      const { element, actions, open } = render();
      open();
      const picker = element.querySelector<HTMLInputElement>('input[type="file"]')!;
      const click = vi.spyOn(picker, 'click').mockImplementation(() => undefined);

      actions()[0].click();

      expect(click).toHaveBeenCalledTimes(1);
    });

    it('pide el idioma, propone el que dice el nombre y sube el archivo tal cual', () => {
      const { fixture, element, open, pickFile, submit, added, opened } = render([]);
      open();
      const file = new File([srt], 'Mi episodio.en.srt');

      pickFile(file);

      expect(element.querySelector('[role="menu"]')).toBeNull();
      expect(element.querySelector('form')!.getAttribute('aria-label')).toBe('Agregar subtítulos');
      expect(element.querySelector('.name')!.textContent).toContain('Mi episodio.en.srt');
      const select = element.querySelector<HTMLSelectElement>('select')!;
      expect(select.value).toBe('en');
      expect(element.querySelector('button[type="submit"]')!.textContent).toContain('Agregar');

      select.value = 'ja';
      submit();
      expect(element.querySelector('button[type="submit"]')!.textContent).toContain('Subiendo…');
      const request = uploadRequest();
      expect(request.request.params.get('name')).toBe('Mi episodio.en.srt');
      expect(request.request.params.get('language')).toBe('ja');
      expect(request.request.headers.get('Content-Type')).toBe('application/octet-stream');
      expect(request.request.body).toBe(file);

      const uploaded = track('u1', 'Japonés (subido)', 'ja', {
        source: 'uploaded',
        canDelete: true,
      });
      request.flush(uploaded);
      fixture.detectChanges();

      expect(added).toEqual([uploaded]);
      expect(opened).toEqual([true, false]);
      expect(element.querySelector('form')).toBeNull();
    });

    it('si el nombre no dice el idioma propone el último elegido, y si tampoco hay, ninguno', () => {
      localStorage.setItem(SUBTITLE_STORAGE_KEY, 'pt');
      const { fixture, element, open, pickFile } = render([]);
      open();

      pickFile(new File([srt], 'Yani Neko 01.srt'));
      expect(element.querySelector<HTMLSelectElement>('select')!.value).toBe('pt');

      element.querySelector<HTMLButtonElement>('form button[type="button"]')!.click();
      fixture.detectChanges();
      localStorage.setItem(SUBTITLE_STORAGE_KEY, 'off');
      pickFile(new File([srt], 'Yani Neko 01.srt'));
      expect(element.querySelector<HTMLSelectElement>('select')!.value).toBe('');
    });

    it('un archivo que no es un subtítulo, vacío o muy grande lo dice y no sube nada', () => {
      const { element, open, pickFile } = render();
      open();
      const big = new File([srt], 'grande.srt');
      Object.defineProperty(big, 'size', { value: MAX_SUBTITLE_BYTES + 1 });

      for (const [file, message] of [
        [new File(['x'], 'foto.jpg'), '«foto.jpg» no es un subtítulo'],
        [new File([], 'vacio.srt'), 'El archivo está vacío.'],
        [big, 'El archivo pesa más de 8 MB.'],
      ] as const) {
        pickFile(file);
        expect(element.querySelector('form')).toBeNull();
        expect(element.querySelector('[role="alert"]')!.textContent).toContain(message);
      }
      http().expectNone(() => true);
    });

    it('si el servidor lo rechaza muestra el motivo y deja intentarlo de nuevo', () => {
      const { fixture, element, open, pickFile, submit, added } = render();
      open();
      pickFile(new File(['basura'], '01.srt'));

      submit();
      uploadRequest().flush(
        {
          title: 'No se pudo agregar el subtítulo.',
          detail: 'El archivo no tiene subtítulos que se puedan leer.',
        },
        { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(element.querySelector('form [role="alert"]')!.textContent).toContain(
        'El archivo no tiene subtítulos que se puedan leer.',
      );
      expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(
        false,
      );
      expect(added).toEqual([]);

      submit();
      uploadRequest().flush(null, { status: 413, statusText: 'Payload Too Large' });
      fixture.detectChanges();
      expect(element.querySelector('form [role="alert"]')!.textContent).toContain(
        'El archivo pesa más de 8 MB.',
      );
    });

    it('Cancelar vuelve al menú sin subir nada', () => {
      const { fixture, element, open, pickFile } = render();
      open();
      pickFile(new File([srt], '01.es.srt'));

      element.querySelector<HTMLButtonElement>('form button[type="button"]')!.click();
      fixture.detectChanges();

      expect(element.querySelector('form')).toBeNull();
      expect(element.querySelector('[role="menu"]')).not.toBeNull();
      http().expectNone(() => true);
    });
  });

  describe('borrar un archivo subido', () => {
    const mine = track('u1', 'Español (subido)', 'es', { source: 'uploaded', canDelete: true });
    const theirs = track('u2', 'Inglés (subido)', 'en', { source: 'uploaded', canDelete: false });

    it('con una pista subida activa que el usuario puede borrar, la borra y avisa', () => {
      const { fixture, element, actions, open, removed, opened } = render([...tracks, mine], 'u1');
      open();
      expect(actions().map(text)).toEqual(['Cargar archivo…', 'Borrar el archivo subido']);

      actions()[1].click();
      const request = http().expectOne('/api/items/v1/subtitles/u1');
      expect(request.request.method).toBe('DELETE');
      request.flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();

      expect(removed).toEqual(['u1']);
      expect(opened).toEqual([true, false]);
      expect(element.querySelector('[role="menu"]')).toBeNull();
    });

    it('si no se pudo borrar lo dice y el menú sigue abierto', () => {
      const { fixture, element, actions, open, removed } = render([mine], 'u1');
      open();

      actions()[1].click();
      http()
        .expectOne('/api/items/v1/subtitles/u1')
        .flush(
          { detail: 'Solo quien lo subió o un administrador puede borrarlo.' },
          { status: 403, statusText: 'Forbidden' },
        );
      fixture.detectChanges();

      expect(removed).toEqual([]);
      expect(element.querySelector('[role="alert"]')!.textContent).toContain('Solo quien lo subió');
    });

    it('no ofrece borrar una pista del archivo ni una subida que no puede borrar', () => {
      for (const activeId of ['e2', 'u2', null]) {
        const { actions, open } = render([...tracks, theirs], activeId);
        open();

        expect(actions().map(text)).toEqual(['Cargar archivo…']);
      }
    });
  });
});
