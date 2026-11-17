import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  SUBTITLES_OFF,
  SubtitlesApi,
  type SubtitleTrack,
  pickInitialTrack,
  pickToggleTrack,
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
