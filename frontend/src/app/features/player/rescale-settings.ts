import {
  DEFAULT_PARAMS,
  type EnhancerParams,
  type ScaleKernel,
  type ViewMode,
} from './enhancer/enhancer-params';

/**
 * Lo que el usuario elige en el panel "Reescalar" y se recuerda en localStorage. Los porcentajes van de 0 a 1,
 * como en el enhancer. La posición del divisor del comparador no se guarda: empieza en el centro.
 */
export interface RescaleSettings {
  view: ViewMode;
  /** Alto del lienzo en píxeles; 0 = el tamaño real que ocupa el escenario en pantalla. */
  outputHeight: number;
  kernel: ScaleKernel;
  sharpen: number;
  denoise: number;
  antiRinging: number;
  dither: boolean;
}

/** Por omisión empieza en Original: sin trabajo de GPU hasta que el usuario lo pida. */
export const DEFAULT_RESCALE: RescaleSettings = {
  view: 'original',
  outputHeight: DEFAULT_PARAMS.outputHeight,
  kernel: DEFAULT_PARAMS.kernel,
  sharpen: DEFAULT_PARAMS.sharpen,
  denoise: DEFAULT_PARAMS.denoise,
  antiRinging: DEFAULT_PARAMS.antiRinging,
  dither: DEFAULT_PARAMS.dither,
};

export const VIEW_OPTIONS: readonly { id: ViewMode; label: string }[] = [
  { id: 'original', label: 'Original' },
  { id: 'enhanced', label: 'Mejorado' },
  { id: 'compare', label: 'Comparar' },
];

/** Resolución de salida del lienzo. 0 = Pantalla. */
export const OUTPUT_OPTIONS: readonly { height: number; label: string }[] = [
  { height: 0, label: 'Pantalla' },
  { height: 720, label: '720p' },
  { height: 1080, label: '1080p' },
  { height: 1440, label: '1440p' },
  { height: 2160, label: '4K' },
];

export const KERNEL_OPTIONS: readonly { id: ScaleKernel; label: string }[] = [
  { id: 'bilinear', label: 'Bilineal' },
  { id: 'bicubic', label: 'Bicúbico' },
  { id: 'lanczos2', label: 'Lanczos 2' },
  { id: 'lanczos3', label: 'Lanczos 3' },
];

/** Posición inicial del divisor del comparador: la mitad. */
export const DEFAULT_SPLIT = DEFAULT_PARAMS.split;

/**
 * Parámetros del enhancer. `split` es la fracción izquierda que se muestra sin mejorar: todo en Original,
 * nada en Mejorado y la posición del divisor en Comparar.
 */
export function toEnhancerParams(settings: RescaleSettings, split: number): EnhancerParams {
  return {
    kernel: settings.kernel,
    antiRinging: settings.antiRinging,
    sharpen: settings.sharpen,
    denoise: settings.denoise,
    dither: settings.dither,
    outputHeight: settings.outputHeight,
    split: settings.view === 'original' ? 1 : settings.view === 'enhanced' ? 0 : split,
  };
}

const unit = (value: unknown, fallback: number): number =>
  typeof value === 'number' && Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : fallback;

/**
 * Lo que se leyó de localStorage (puede venir dañado o de una versión vieja) convertido en ajustes válidos:
 * cada campo que no sirve vuelve a su valor por omisión.
 */
export function sanitizeRescale(raw: unknown): RescaleSettings {
  if (typeof raw !== 'object' || raw === null) {
    return { ...DEFAULT_RESCALE };
  }
  const value = raw as Record<string, unknown>;
  const view = VIEW_OPTIONS.find((option) => option.id === value['view'])?.id;
  const kernel = KERNEL_OPTIONS.find((option) => option.id === value['kernel'])?.id;
  const output = OUTPUT_OPTIONS.find((option) => option.height === value['outputHeight'])?.height;
  return {
    view: view ?? DEFAULT_RESCALE.view,
    outputHeight: output ?? DEFAULT_RESCALE.outputHeight,
    kernel: kernel ?? DEFAULT_RESCALE.kernel,
    sharpen: unit(value['sharpen'], DEFAULT_RESCALE.sharpen),
    denoise: unit(value['denoise'], DEFAULT_RESCALE.denoise),
    antiRinging: unit(value['antiRinging'], DEFAULT_RESCALE.antiRinging),
    dither: typeof value['dither'] === 'boolean' ? value['dither'] : DEFAULT_RESCALE.dither,
  };
}
