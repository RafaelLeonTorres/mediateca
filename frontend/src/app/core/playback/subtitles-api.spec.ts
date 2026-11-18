import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  SUBTITLES_OFF,
  SubtitlesApi,
  type SubtitleTrack,
  guessSubtitleLanguage,
  isSubtitleFileName,
  pickInitialTrack,
  pickToggleTrack,
  subtitleErrorMessage,
} from './subtitles-api';

function track(id: string, language: string | null, extra: Partial<SubtitleTrack> = {}): SubtitleTrack {
  return {
    id,
    label: id,
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

describe('SubtitlesApi', () => {
  let http: HttpTestingController;
  let api: SubtitlesApi;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(SubtitlesApi);
  });

  afterEach(() => http.verify());

  it('pide las pistas del video indicado', async () => {
    const resource = TestBed.runInInjectionContext(() => api.tracks(() => 'v1'));
    TestBed.tick();

    http.expectOne('/api/items/v1/subtitles').flush([track('e2', 'es')]);
    await TestBed.inject(ApplicationRef).whenStable();

    expect(resource.value()).toEqual([track('e2', 'es')]);
  });

  it('sin id no pide nada', () => {
    TestBed.runInInjectionContext(() => api.tracks(() => undefined));
    TestBed.tick();

    http.expectNone(() => true);
  });

  it('sube el archivo tal cual, con su nombre y el idioma elegido', () => {
    const file = new File(['1\n00:00:01,000 --> 00:00:02,000\nHola\n'], 'Mi episodio.es.srt');
    let result: SubtitleTrack | undefined;

    api.upload('v1', file, 'es').subscribe((value) => (result = value));
    const request = http.expectOne((candidate) => candidate.url === '/api/items/v1/subtitles');

    expect(request.request.method).toBe('POST');
    expect(request.request.params.get('name')).toBe('Mi episodio.es.srt');
    expect(request.request.params.get('language')).toBe('es');
    expect(request.request.headers.get('Content-Type')).toBe('application/octet-stream');
    expect(request.request.body).toBe(file);
    request.flush(track('u1', 'es', { source: 'uploaded', canDelete: true }));
    expect(result?.id).toBe('u1');
  });

  it('borra una pista subida', () => {
    let done = false;

    api.remove('v1', 'u1').subscribe(() => (done = true));
    const request = http.expectOne('/api/items/v1/subtitles/u1');

    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
  });
});

describe('guessSubtitleLanguage', () => {
  it.each([
    ['Episodio 01.es.srt', 'es'],
    ['Show.S01E01.eng.forced.ass', 'en'],
    ['capitulo.pt-BR.srt', 'pt'],
    ['Medalist.Lat.srt', 'es'],
    ['[Grupo] Yani Neko - 01 [Spanish].ass', 'es'],
    ['Medalist 02 Español.srt', 'es'],
    ['Ataque.de.Titanes.srt', ''],
    ['Amor en Tokio.srt', ''],
  ])('%s propone "%s"', (name, code) => {
    expect(guessSubtitleLanguage(name)).toBe(code);
  });

  it('si el nombre no dice nada propone el último idioma elegido, si es uno de la lista', () => {
    expect(guessSubtitleLanguage('01.srt', 'ja')).toBe('ja');
    expect(guessSubtitleLanguage('01.srt', SUBTITLES_OFF)).toBe('');
    expect(guessSubtitleLanguage('01.es.srt', 'ja')).toBe('es');
  });
});

describe('isSubtitleFileName', () => {
  it('acepta las mismas extensiones que el servidor', () => {
    expect(['a.srt', 'a.VTT', 'b.ass', 'c.ssa'].every((name) => isSubtitleFileName(name))).toBe(true);
    expect(['a.sub', 'a.txt', 'srt', 'a.srt.zip'].some((name) => isSubtitleFileName(name))).toBe(false);
  });
});

describe('subtitleErrorMessage', () => {
  it('explica por qué falló, con el motivo del servidor si lo trae', () => {
    expect(subtitleErrorMessage(new HttpErrorResponse({ status: 413 }), 'Respaldo.')).toBe(
      'El archivo pesa más de 8 MB.',
    );
    expect(
      subtitleErrorMessage(new HttpErrorResponse({ status: 400, error: { detail: 'Motivo.' } }), 'Respaldo.'),
    ).toBe('Motivo.');
    expect(subtitleErrorMessage(new HttpErrorResponse({ status: 0 }), 'Respaldo.')).toContain(
      'No se pudo conectar',
    );
    expect(subtitleErrorMessage(new HttpErrorResponse({ status: 500 }), 'Respaldo.')).toBe('Respaldo.');
    expect(subtitleErrorMessage(new Error('otro'), 'Respaldo.')).toBe('Respaldo.');
  });
});

describe('pickInitialTrack', () => {
  const es = track('e2', 'es');
  const en = track('e3', 'en');

  it('sin pistas no elige nada', () => {
    expect(pickInitialTrack([], 'es')).toBeNull();
  });

  it('por omisión no activa ninguna', () => {
    expect(pickInitialTrack([es, en], null)).toBeNull();
  });

  it('respeta el idioma recordado', () => {
    expect(pickInitialTrack([es, en], 'en')).toBe(en);
  });

  it('prefiere la pista completa a la de forzados del mismo idioma', () => {
    const forced = track('e4', 'es', { isForced: true });

    expect(pickInitialTrack([forced, es], 'es')).toBe(es);
  });

  it('con "Desactivados" recordado no activa ni una pista forzada o predeterminada', () => {
    const forced = track('e4', 'es', { isForced: true, isDefault: true });

    expect(pickInitialTrack([forced], SUBTITLES_OFF)).toBeNull();
  });

  it('sin idioma recordado activa la forzada y, si no, la predeterminada', () => {
    const forced = track('e4', 'en', { isForced: true });
    const preset = track('e5', 'es', { isDefault: true });

    expect(pickInitialTrack([preset, forced], null)).toBe(forced);
    expect(pickInitialTrack([es, preset], null)).toBe(preset);
  });

  it('si el idioma recordado no existe cae en forzada o predeterminada', () => {
    const preset = track('e5', 'es', { isDefault: true });

    expect(pickInitialTrack([en, preset], 'ja')).toBe(preset);
    expect(pickInitialTrack([en, es], 'ja')).toBeNull();
  });
});

describe('pickToggleTrack', () => {
  it('prefiere el idioma recordado y evita los forzados', () => {
    const forced = track('e1', 'es', { isForced: true });
    const es = track('e2', 'es');
    const en = track('e3', 'en');

    expect(pickToggleTrack([forced, en, es], 'es')).toBe(es);
    expect(pickToggleTrack([forced, en, es], null)).toBe(en);
    expect(pickToggleTrack([forced], null)).toBe(forced);
    expect(pickToggleTrack([], 'es')).toBeNull();
  });
});
