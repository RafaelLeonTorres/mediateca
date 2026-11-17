import { DEFAULT_PARAMS } from './enhancer/enhancer-params';
import {
  RESCALE_STORAGE_KEY,
  SUBTITLE_STORAGE_KEY,
  readRescaleSettings,
  readSubtitlePreference,
  writeRescaleSettings,
  writeSubtitlePreference,
} from './player-preferences';
import { DEFAULT_RESCALE, DEFAULT_SPLIT, sanitizeRescale, toEnhancerParams } from './rescale-settings';

describe('ajustes de reescalado', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('por omisión empieza en Original', () => {
    expect(DEFAULT_RESCALE.view).toBe('original');
  });

  it('el divisor del comparador y los parámetros dependen de la vista', () => {
    const base = { ...DEFAULT_RESCALE, sharpen: 0.7, outputHeight: 1440 };

    expect(toEnhancerParams({ ...base, view: 'original' }, 0.3).split).toBe(1);
    expect(toEnhancerParams({ ...base, view: 'enhanced' }, 0.3).split).toBe(0);
    expect(toEnhancerParams({ ...base, view: 'compare' }, 0.3).split).toBe(0.3);
    expect(toEnhancerParams(base, DEFAULT_SPLIT)).toMatchObject({ sharpen: 0.7, outputHeight: 1440 });
    expect(DEFAULT_SPLIT).toBe(DEFAULT_PARAMS.split);
  });

  it('sanea lo guardado: lo que no sirve vuelve a su valor por omisión', () => {
    expect(sanitizeRescale(null)).toEqual(DEFAULT_RESCALE);
    expect(sanitizeRescale('hola')).toEqual(DEFAULT_RESCALE);
    expect(
      sanitizeRescale({ view: 'raro', outputHeight: 999, kernel: 'x', sharpen: 5, denoise: -1, dither: 'sí' }),
    ).toEqual({ ...DEFAULT_RESCALE, sharpen: 1, denoise: 0 });
    expect(sanitizeRescale({ view: 'compare', kernel: 'lanczos2', outputHeight: 2160, dither: false })).toMatchObject({
      view: 'compare',
      kernel: 'lanczos2',
      outputHeight: 2160,
      dither: false,
    });
  });

  describe('localStorage', () => {
    it('guarda y recupera lo último que se eligió', () => {
      writeRescaleSettings({ ...DEFAULT_RESCALE, view: 'enhanced', kernel: 'bicubic' });

      expect(readRescaleSettings()).toMatchObject({ view: 'enhanced', kernel: 'bicubic' });
      expect(localStorage.getItem(RESCALE_STORAGE_KEY)).toContain('enhanced');
    });

    it('sin nada guardado o con datos dañados usa los valores por omisión', () => {
      expect(readRescaleSettings()).toEqual(DEFAULT_RESCALE);

      localStorage.setItem(RESCALE_STORAGE_KEY, '{no es json');
      expect(readRescaleSettings()).toEqual(DEFAULT_RESCALE);
    });

    it('si localStorage lanza, nada falla y no se recuerda nada', () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('bloqueado');
      });
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('lleno');
      });

      expect(() => writeRescaleSettings(DEFAULT_RESCALE)).not.toThrow();
      expect(() => writeSubtitlePreference('es')).not.toThrow();
      expect(readRescaleSettings()).toEqual(DEFAULT_RESCALE);
      expect(readSubtitlePreference()).toBeNull();
    });

    it('la preferencia de subtítulos se guarda y se borra con null', () => {
      expect(readSubtitlePreference()).toBeNull();

      writeSubtitlePreference('es');
      expect(localStorage.getItem(SUBTITLE_STORAGE_KEY)).toBe('es');
      expect(readSubtitlePreference()).toBe('es');

      writeSubtitlePreference(null);
      expect(readSubtitlePreference()).toBeNull();
    });
  });
});
