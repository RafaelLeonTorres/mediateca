/*
 * Viene de mi proyecto reproductor-video-gpu (src/app/reproductor/enhancer/enhancer-params.ts) y se lleva a Mediateca
 * casi sin cambios.
 * Licencia: código propio. La nitidez adaptativa está inspirada en AMD FidelityFX CAS (licencia MIT), con
 * implementación propia.
 */
/** Núcleo de reescalado. Todos son separables, así que se aplican en dos pases (horizontal y vertical). */
export type ScaleKernel = 'bilinear' | 'bicubic' | 'lanczos2' | 'lanczos3';

export type ViewMode = 'compare' | 'enhanced' | 'original';

export interface EnhancerParams {
  kernel: ScaleKernel;
  /** 0..1 — cuánto se limita el rebote (ringing) del núcleo al rango de los píxeles vecinos. */
  antiRinging: number;
  /** 0..1 — nitidez adaptativa (inspirada en AMD FidelityFX CAS, licencia MIT). */
  sharpen: number;
  /** 0..1 — intensidad del filtro bilateral que reduce ruido sin borrar bordes. 0 lo desactiva. */
  denoise: number;
  /** Ruido de ±½ nivel antes de pasar a 8 bits, para evitar bandas en degradados. */
  dither: boolean;
  /** 0..1 — fracción izquierda del video que se muestra sin mejorar (comparador). 0 = todo mejorado. */
  split: number;
  /** Alto del lienzo en píxeles (720, 1080, 1440, 2160…). 0 = el tamaño real que ocupa en pantalla. */
  outputHeight: number;
}

export const DEFAULT_PARAMS: EnhancerParams = {
  kernel: 'lanczos3',
  antiRinging: 0.8,
  sharpen: 0.6,
  denoise: 0.25,
  dither: true,
  split: 0.5,
  outputHeight: 1080,
};

export interface Size {
  width: number;
  height: number;
}

/** Rectángulo en píxeles de dispositivo dentro del lienzo. */
export interface OutputRect extends Size {
  x: number;
  y: number;
}

export interface EnhancerStats {
  /** Cuadros dibujados por segundo. */
  fps: number;
  /** Milisegundos de GPU por cuadro; null si el navegador no ofrece timestamp-query. */
  gpuMs: number | null;
  videoSize: Size;
  /** Tamaño que ocupa el video dentro del lienzo. */
  outputSize: Size;
  /** Tamaño del lienzo (la resolución real de salida). */
  canvasSize: Size;
}

export interface GpuInfo {
  vendor: string;
  architecture: string;
  device: string;
  description: string;
  /** Adaptador por software (muy lento). */
  isFallback: boolean;
  timestampQuery: boolean;
  maxTextureSize: number;
}

export interface EnhancerEvents {
  onStats?: (stats: EnhancerStats) => void;
  onError?: (message: string) => void;
  /** Se llama cuando cambia el rectángulo donde se dibuja el video dentro del lienzo. */
  onLayout?: (video: OutputRect, canvas: Size) => void;
}
