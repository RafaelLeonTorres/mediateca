import type { EnhancerEvents, EnhancerParams, GpuInfo, ViewMode } from './enhancer/enhancer-params';
import type { VideoEnhancer, VideoEnhancerFactory } from './video-enhancer';

export interface EnhancerSessionEvents extends EnhancerEvents {
  /** El enhancer ya está creado: trae los datos de la GPU. */
  onReady?: (info: GpuInfo) => void;
}

/**
 * El enhancer de un `<video>` y su ciclo de vida. En Original no hace nada (ni siquiera crea el dispositivo de
 * GPU); al pedir Mejorado o Comparar lo crea la primera vez, lo arranca y sigue el tamaño del escenario. Al
 * volver a Original lo detiene, sin destruirlo, para que volver a Mejorado sea inmediato. Si pide algo mientras
 * se está creando, al terminar se aplica lo último que pidió.
 */
export class EnhancerSession {
  private enhancer?: VideoEnhancer;
  private creating?: Promise<void>;
  private observer?: ResizeObserver;
  private destroyed = false;
  private latest?: { view: ViewMode; params: EnhancerParams };

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly video: HTMLVideoElement,
    private readonly stage: HTMLElement,
    private readonly factory: VideoEnhancerFactory,
    private readonly events: EnhancerSessionEvents = {},
  ) {}

  /** Ya se creó el enhancer (es decir, alguna vez se pidió Mejorado o Comparar). */
  get created(): boolean {
    return this.enhancer !== undefined;
  }

  /** Aplica la vista y los parámetros. Nunca rechaza: los errores salen por `onError`. */
  apply(view: ViewMode, params: EnhancerParams): Promise<void> {
    this.latest = { view, params };
    if (this.destroyed) {
      return Promise.resolve();
    }
    if (view === 'original') {
      // Si todavía se está creando, `flush` verá que lo último pedido es Original y lo dejará detenido.
      this.enhancer?.stop();
      return Promise.resolve();
    }
    if (!this.enhancer) {
      this.creating ??= this.create();
      return this.creating;
    }
    this.flush();
    return Promise.resolve();
  }

  destroy(): void {
    this.destroyed = true;
    this.observer?.disconnect();
    this.observer = undefined;
    this.enhancer?.destroy();
    this.enhancer = undefined;
  }

  private async create(): Promise<void> {
    try {
      const enhancer = await this.factory(this.canvas, this.video, {
        onStats: (stats) => this.events.onStats?.(stats),
        onError: (message) => this.events.onError?.(message),
        onLayout: (rect, canvas) => this.events.onLayout?.(rect, canvas),
      });
      if (this.destroyed) {
        enhancer.destroy();
        return;
      }
      this.enhancer = enhancer;
      this.events.onReady?.(enhancer.gpuInfo);
      this.observeStage();
    } catch (error) {
      this.events.onError?.(error instanceof Error ? error.message : String(error));
      return;
    } finally {
      this.creating = undefined;
    }
    this.flush();
  }

  /** Aplica lo último que se pidió al enhancer ya creado. */
  private flush(): void {
    const { enhancer, latest } = this;
    if (!enhancer || !latest || this.destroyed) {
      return;
    }
    if (latest.view === 'original') {
      enhancer.stop();
      return;
    }
    enhancer.setParams(latest.params);
    enhancer.start();
    enhancer.requestRender();
  }

  /** El lienzo toma el tamaño real del escenario en píxeles de dispositivo (también al pasar a pantalla completa). */
  private observeStage(): void {
    const ratio = () => window.devicePixelRatio || 1;
    const initial = this.stage.getBoundingClientRect();
    this.enhancer?.resize(initial.width * ratio(), initial.height * ratio());
    if (typeof ResizeObserver === 'undefined') {
      return;
    }
    this.observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (!entry) {
        return;
      }
      const device = entry.devicePixelContentBoxSize?.[0];
      this.enhancer?.resize(
        device ? device.inlineSize : entry.contentRect.width * ratio(),
        device ? device.blockSize : entry.contentRect.height * ratio(),
      );
    });
    try {
      this.observer.observe(this.stage, { box: 'device-pixel-content-box' });
    } catch {
      // Firefox y Safari no admiten device-pixel-content-box.
      this.observer.observe(this.stage);
    }
  }
}
