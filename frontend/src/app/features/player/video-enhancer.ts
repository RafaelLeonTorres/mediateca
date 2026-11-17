import { InjectionToken } from '@angular/core';

import type { EnhancerEvents, EnhancerParams, GpuInfo } from './enhancer/enhancer-params';

/**
 * Lo que el reproductor usa de `WebGpuVideoEnhancer`. Es una interfaz para que las pruebas pongan un doble:
 * jsdom no tiene WebGPU.
 */
export interface VideoEnhancer {
  readonly gpuInfo: GpuInfo;
  setParams(next: Partial<EnhancerParams>): void;
  resize(widthPx: number, heightPx: number): void;
  requestRender(): void;
  start(): void;
  stop(): void;
  destroy(): void;
}

export type VideoEnhancerFactory = (
  canvas: HTMLCanvasElement,
  video: HTMLVideoElement,
  events: EnhancerEvents,
) => Promise<VideoEnhancer>;

/**
 * Cómo se crea el enhancer. El código de WebGPU y sus shaders se descargan solo cuando el usuario pide
 * Mejorado o Comparar por primera vez: en Original no se carga nada.
 */
export const VIDEO_ENHANCER_FACTORY = new InjectionToken<VideoEnhancerFactory>('VIDEO_ENHANCER_FACTORY', {
  providedIn: 'root',
  factory: () => async (canvas, video, events) => {
    const { WebGpuVideoEnhancer } = await import('./enhancer/webgpu-video-enhancer');
    return WebGpuVideoEnhancer.create(canvas, video, events);
  },
});

/** El navegador expone WebGPU. Las pruebas lo reemplazan. */
export const WEBGPU_SUPPORT = new InjectionToken<boolean>('WEBGPU_SUPPORT', {
  providedIn: 'root',
  factory: () => typeof navigator !== 'undefined' && 'gpu' in navigator,
});

/** Por qué el botón Reescalar está deshabilitado. */
export const WEBGPU_UNAVAILABLE_TEXT = 'Tu navegador no tiene WebGPU; usa Chrome o Edge actualizados.';
