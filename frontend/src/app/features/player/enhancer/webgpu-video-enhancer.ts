/*
 * Viene de mi proyecto reproductor-video-gpu (src/app/reproductor/enhancer/webgpu-video-enhancer.ts) y se lleva a Mediateca
 * casi sin cambios.
 * Cambios: los tipos se importan con `type` (el proyecto usa isolatedModules) y `isFallbackAdapter` se lee
 * sin depender de la versión de los tipos de WebGPU de TypeScript.
 * Licencia: código propio. La nitidez adaptativa está inspirada en AMD FidelityFX CAS (licencia MIT), con
 * implementación propia.
 */
import {
  DEFAULT_PARAMS,
  type EnhancerEvents,
  type EnhancerParams,
  type GpuInfo,
  type OutputRect,
  type ScaleKernel,
  type Size,
} from './enhancer-params';
import { DENOISE_WGSL, FINAL_WGSL, INGEST_WGSL, SCALE_WGSL } from './enhancer-shaders';

const KERNEL_ID: Record<ScaleKernel, number> = { bilinear: 0, bicubic: 1, lanczos2: 2, lanczos3: 3 };

/*
 * Banderas de WebGPU con los valores de la especificación. TypeScript 6 trae los tipos de WebGPU en
 * lib.dom, pero no declara los objetos globales GPUBufferUsage, GPUTextureUsage ni GPUMapMode (en el
 * navegador sí existen). Con constantes propias no hace falta @webgpu/types.
 */
const BUFFER_USAGE = { MAP_READ: 0x0001, COPY_SRC: 0x0004, COPY_DST: 0x0008, UNIFORM: 0x0040, QUERY_RESOLVE: 0x0200 } as const;
const TEXTURE_USAGE = { TEXTURE_BINDING: 0x04, RENDER_ATTACHMENT: 0x10 } as const;
const MAP_MODE_READ = 0x0001;
/** Formato de las texturas intermedias: evita la cuantización entre pases y es filtrable sin extensiones. */
const INTERMEDIATE: GPUTextureFormat = 'rgba16float';
const STATS_INTERVAL_MS = 500;
/** Si requestVideoFrameCallback lleva tanto sin avisar, se dibuja al avanzar currentTime (panel oculto, elemento no compuesto…). */
const VFC_SILENCE_MS = 200;

export class WebGpuUnavailableError extends Error {
  override name = 'WebGpuUnavailableError';
}

interface Pipelines {
  ingest: GPURenderPipeline;
  denoise: GPURenderPipeline;
  scale: GPURenderPipeline;
  final: GPURenderPipeline;
}

interface Views {
  src: GPUTextureView;
  denoised: GPUTextureView;
  mid: GPUTextureView;
  up: GPUTextureView;
}

interface Groups {
  denoise: GPUBindGroup;
  scaleHSrc: GPUBindGroup;
  scaleHDenoised: GPUBindGroup;
  scaleV: GPUBindGroup;
  final: GPUBindGroup;
}

interface Timing {
  querySet: GPUQuerySet;
  resolve: GPUBuffer;
  read: GPUBuffer;
  pending: boolean;
}

/**
 * Mejora un <video> con la GPU usando WebGPU: ingesta → reducción de ruido → escalado separable →
 * nitidez adaptativa → lienzo. No depende de Angular ni de la marca de la GPU.
 */
export class WebGpuVideoEnhancer {
  readonly gpuInfo: GpuInfo;

  private params: EnhancerParams = { ...DEFAULT_PARAMS };
  private running = false;
  private destroyed = false;
  private needsRender = true;
  private layoutDirty = true;
  private rafId = 0;
  private vfcId = 0;
  private hasVfc = false;
  private lastVfcAt = 0;
  private lastMediaTime = -1;
  private frameIndex = 0;
  private lastError = '';

  private pipelines!: Pipelines;
  private views!: Views;
  private groups!: Groups;
  private tex: { src?: GPUTexture; denoised?: GPUTexture; mid?: GPUTexture; up?: GPUTexture } = {};
  private srcSize: Size = { width: 0, height: 0 };
  private out: OutputRect = { x: 0, y: 0, width: 0, height: 0 };
  /** Tamaño real del escenario en píxeles de dispositivo (de él sale la proporción del lienzo). */
  private stage: Size = { width: 0, height: 0 };

  private readonly sampler: GPUSampler;
  private readonly ubo: { denoise: GPUBuffer; scaleH: GPUBuffer; scaleV: GPUBuffer; final: GPUBuffer };
  private readonly timing?: Timing;
  private readonly scratch = { denoise: new Float32Array(4), scale: new ArrayBuffer(16), final: new Float32Array(8) };
  private stats = { frames: 0, since: performance.now(), gpuSum: 0, gpuCount: 0 };

  private constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly video: HTMLVideoElement,
    private readonly device: GPUDevice,
    private readonly context: GPUCanvasContext,
    private readonly format: GPUTextureFormat,
    info: GPUAdapterInfo,
    private readonly events: EnhancerEvents,
  ) {
    this.gpuInfo = {
      vendor: info.vendor,
      architecture: info.architecture,
      device: info.device,
      description: info.description,
      // GPUAdapterInfo.isFallbackAdapter no está en todas las versiones de lib.dom.d.ts: se lee sin depender del tipo.
      isFallback: 'isFallbackAdapter' in info && info.isFallbackAdapter === true,
      timestampQuery: device.features.has('timestamp-query'),
      maxTextureSize: device.limits.maxTextureDimension2D,
    };
    this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
    const uniform = (label: string, size: number) =>
      device.createBuffer({ label, size, usage: BUFFER_USAGE.UNIFORM | BUFFER_USAGE.COPY_DST });
    this.ubo = {
      denoise: uniform('ubo-denoise', 16),
      scaleH: uniform('ubo-scale-h', 16),
      scaleV: uniform('ubo-scale-v', 16),
      final: uniform('ubo-final', 32),
    };
    if (this.gpuInfo.timestampQuery) {
      this.timing = {
        querySet: device.createQuerySet({ type: 'timestamp', count: 2 }),
        resolve: device.createBuffer({ size: 16, usage: BUFFER_USAGE.QUERY_RESOLVE | BUFFER_USAGE.COPY_SRC }),
        read: device.createBuffer({ size: 16, usage: BUFFER_USAGE.COPY_DST | BUFFER_USAGE.MAP_READ }),
        pending: false,
      };
    }
    device.addEventListener('uncapturederror', (e) => this.report((e as GPUUncapturedErrorEvent).error.message));
    void device.lost.then((lost) => {
      if (lost.reason !== 'destroyed') this.fail(`Se perdió el dispositivo GPU: ${lost.message || lost.reason}`);
    });
  }

  /** Crea el dispositivo, compila los shaders y deja el pipeline listo. Lanza WebGpuUnavailableError si no hay WebGPU. */
  static async create(
    canvas: HTMLCanvasElement,
    video: HTMLVideoElement,
    events: EnhancerEvents = {},
  ): Promise<WebGpuVideoEnhancer> {
    if (!('gpu' in navigator)) {
      throw new WebGpuUnavailableError('Este navegador no expone WebGPU (navigator.gpu no existe).');
    }
    const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
    if (!adapter) throw new WebGpuUnavailableError('No se encontró ningún adaptador WebGPU compatible.');
    const requiredFeatures: GPUFeatureName[] = adapter.features.has('timestamp-query') ? ['timestamp-query'] : [];
    const device = await adapter.requestDevice({ requiredFeatures });
    // lib.dom de TypeScript 6 no tiene la sobrecarga de getContext('webgpu'): devuelve el tipo genérico.
    const context = canvas.getContext('webgpu') as GPUCanvasContext | null;
    if (!context) throw new WebGpuUnavailableError('El lienzo no pudo crear un contexto WebGPU.');
    const format = navigator.gpu.getPreferredCanvasFormat();
    context.configure({ device, format, alphaMode: 'opaque' });

    const enhancer = new WebGpuVideoEnhancer(canvas, video, device, context, format, adapter.info, events);
    await enhancer.buildPipelines();
    return enhancer;
  }

  // ───────────────────────── API pública ─────────────────────────

  setParams(next: Partial<EnhancerParams>): void {
    const clamp01 = (n: number) => Math.min(1, Math.max(0, n));
    const p = { ...this.params, ...next };
    const cambiaSalida = Math.round(p.outputHeight) !== this.params.outputHeight;
    this.params = {
      ...p,
      antiRinging: clamp01(p.antiRinging),
      sharpen: clamp01(p.sharpen),
      denoise: clamp01(p.denoise),
      split: clamp01(p.split),
      outputHeight: Math.max(0, Math.round(p.outputHeight)),
    };
    if (cambiaSalida) this.applyCanvasSize();
    this.needsRender = true;
  }

  /** Tamaño del escenario en píxeles de dispositivo. El lienzo toma ese tamaño o el alto de salida elegido. */
  resize(widthPx: number, heightPx: number): void {
    this.stage = { width: Math.max(1, Math.round(widthPx)), height: Math.max(1, Math.round(heightPx)) };
    this.applyCanvasSize();
  }

  /**
   * Fija la resolución del lienzo: la de pantalla (outputHeight = 0) o un alto fijo (720p, 1080p, 4K…) con la
   * proporción del escenario. Si el escenario es 16:9 el resultado es exacto (1920×1080); el navegador escala el
   * lienzo al tamaño que ocupa en pantalla.
   */
  private applyCanvasSize(): void {
    if (this.stage.width === 0) return;
    const max = this.device.limits.maxTextureDimension2D;
    let w = this.stage.width;
    let h = this.stage.height;
    const alto = this.params.outputHeight;
    if (alto > 0) {
      let proporcion = this.stage.width / this.stage.height;
      if (Math.abs(proporcion - 16 / 9) / (16 / 9) < 0.01) proporcion = 16 / 9;
      h = alto;
      w = Math.round(alto * proporcion);
    }
    w = Math.max(1, Math.min(max, w));
    h = Math.max(1, Math.min(max, h));
    if (w === this.canvas.width && h === this.canvas.height) return;
    this.canvas.width = w;
    this.canvas.height = h;
    this.layoutDirty = true;
    this.needsRender = true;
  }

  requestRender(): void {
    this.needsRender = true;
  }

  start(): void {
    if (this.running || this.destroyed) return;
    this.running = true;
    this.watchVideoFrames();
    const tick = () => {
      if (!this.running) return;
      this.rafId = requestAnimationFrame(tick);
      const t = this.video.currentTime;
      const vfcSilencioso = !this.hasVfc || performance.now() - this.lastVfcAt > VFC_SILENCE_MS;
      const changed = vfcSilencioso && t !== this.lastMediaTime;
      if (this.needsRender || changed) {
        this.needsRender = false;
        this.lastMediaTime = t;
        this.render();
      }
    };
    this.rafId = requestAnimationFrame(tick);
  }

  stop(): void {
    this.running = false;
    cancelAnimationFrame(this.rafId);
    if (this.hasVfc) this.video.cancelVideoFrameCallback(this.vfcId);
    this.hasVfc = false;
  }

  destroy(): void {
    if (this.destroyed) return;
    this.stop();
    this.destroyed = true;
    Object.values(this.tex).forEach((t) => t?.destroy());
    Object.values(this.ubo).forEach((b) => b.destroy());
    this.timing?.querySet.destroy();
    this.timing?.resolve.destroy();
    this.timing?.read.destroy();
    this.device.destroy();
  }

  // ───────────────────────── construcción ─────────────────────────

  private async buildPipelines(): Promise<void> {
    const make = async (label: string, code: string, format: GPUTextureFormat) => {
      const module = this.device.createShaderModule({ label, code });
      const info = await module.getCompilationInfo();
      const errors = info.messages.filter((m) => m.type === 'error');
      if (errors.length) {
        throw new Error(`Error de WGSL en «${label}»: ` + errors.map((m) => `línea ${m.lineNum}: ${m.message}`).join(' | '));
      }
      return this.device.createRenderPipelineAsync({
        label,
        layout: 'auto',
        vertex: { module, entryPoint: 'vs' },
        fragment: { module, entryPoint: 'fs', targets: [{ format }] },
        primitive: { topology: 'triangle-list' },
      });
    };
    const [ingest, denoise, scale, final] = await Promise.all([
      make('ingest', INGEST_WGSL, INTERMEDIATE),
      make('denoise', DENOISE_WGSL, INTERMEDIATE),
      make('scale', SCALE_WGSL, INTERMEDIATE),
      make('final', FINAL_WGSL, this.format),
    ]);
    this.pipelines = { ingest, denoise, scale, final };
  }

  private makeTexture(label: string, width: number, height: number): GPUTexture {
    return this.device.createTexture({
      label,
      size: [width, height],
      format: INTERMEDIATE,
      usage: TEXTURE_USAGE.TEXTURE_BINDING | TEXTURE_USAGE.RENDER_ATTACHMENT,
    });
  }

  /** Calcula dónde cabe el video dentro del lienzo conservando la proporción (barras negras). */
  private fitRect(videoW: number, videoH: number): OutputRect {
    const cw = this.canvas.width;
    const ch = this.canvas.height;
    const ar = videoW / videoH;
    let width = cw;
    let height = Math.round(cw / ar);
    if (height > ch) {
      height = ch;
      width = Math.round(ch * ar);
    }
    width = Math.max(1, width);
    height = Math.max(1, height);
    return { x: Math.floor((cw - width) / 2), y: Math.floor((ch - height) / 2), width, height };
  }

  /** (Re)crea texturas y grupos de enlace cuando cambia el tamaño del video o del lienzo. */
  private ensureResources(): boolean {
    const vw = this.video.videoWidth;
    const vh = this.video.videoHeight;
    if (!vw || !vh) return false;
    const limit = this.device.limits.maxTextureDimension2D;
    if (vw > limit || vh > limit) {
      this.fail(`El video (${vw}×${vh}) supera el límite de textura de la GPU (${limit}).`);
      return false;
    }
    const rect = this.fitRect(vw, vh);
    const srcChanged = vw !== this.srcSize.width || vh !== this.srcSize.height;
    const outChanged = rect.width !== this.out.width || rect.height !== this.out.height;
    const moved = rect.x !== this.out.x || rect.y !== this.out.y;
    if (!srcChanged && !outChanged && !moved && !this.layoutDirty) return true;

    if (srcChanged) {
      this.tex.src?.destroy();
      this.tex.denoised?.destroy();
      this.tex.src = this.makeTexture('src', vw, vh);
      this.tex.denoised = this.makeTexture('denoised', vw, vh);
      this.srcSize = { width: vw, height: vh };
    }
    if (srcChanged || outChanged) {
      this.tex.mid?.destroy();
      this.tex.up?.destroy();
      this.tex.mid = this.makeTexture('mid', rect.width, vh);
      this.tex.up = this.makeTexture('up', rect.width, rect.height);
    }
    this.out = rect;
    this.buildBindGroups();
    this.layoutDirty = false;
    this.events.onLayout?.(rect, { width: this.canvas.width, height: this.canvas.height });
    return true;
  }

  private buildBindGroups(): void {
    const d = this.device;
    const p = this.pipelines;
    const t = this.tex;
    this.views = {
      src: t.src!.createView(),
      denoised: t.denoised!.createView(),
      mid: t.mid!.createView(),
      up: t.up!.createView(),
    };
    const v = this.views;
    const scaleLayout = p.scale.getBindGroupLayout(0);
    this.groups = {
      denoise: d.createBindGroup({
        layout: p.denoise.getBindGroupLayout(0),
        entries: [
          { binding: 0, resource: v.src },
          { binding: 1, resource: { buffer: this.ubo.denoise } },
        ],
      }),
      scaleHSrc: d.createBindGroup({
        layout: scaleLayout,
        entries: [
          { binding: 0, resource: v.src },
          { binding: 1, resource: { buffer: this.ubo.scaleH } },
        ],
      }),
      scaleHDenoised: d.createBindGroup({
        layout: scaleLayout,
        entries: [
          { binding: 0, resource: v.denoised },
          { binding: 1, resource: { buffer: this.ubo.scaleH } },
        ],
      }),
      scaleV: d.createBindGroup({
        layout: scaleLayout,
        entries: [
          { binding: 0, resource: v.mid },
          { binding: 1, resource: { buffer: this.ubo.scaleV } },
        ],
      }),
      final: d.createBindGroup({
        layout: p.final.getBindGroupLayout(0),
        entries: [
          { binding: 0, resource: v.up },
          { binding: 1, resource: v.src },
          { binding: 2, resource: this.sampler },
          { binding: 3, resource: { buffer: this.ubo.final } },
        ],
      }),
    };
  }

  private writeUniforms(): void {
    const q = this.device.queue;
    const p = this.params;
    this.scratch.denoise[0] = p.denoise;
    q.writeBuffer(this.ubo.denoise, 0, this.scratch.denoise);

    const u32 = new Uint32Array(this.scratch.scale);
    const f32 = new Float32Array(this.scratch.scale);
    u32[0] = 0;
    u32[1] = KERNEL_ID[p.kernel];
    f32[2] = p.antiRinging;
    f32[3] = this.out.width;
    q.writeBuffer(this.ubo.scaleH, 0, this.scratch.scale);
    u32[0] = 1;
    f32[3] = this.out.height;
    q.writeBuffer(this.ubo.scaleV, 0, this.scratch.scale);

    const f = this.scratch.final;
    f[0] = this.out.x;
    f[1] = this.out.y;
    f[2] = p.split;
    f[3] = p.sharpen;
    f[4] = p.dither ? 1 : 0;
    f[5] = this.frameIndex % 64;
    q.writeBuffer(this.ubo.final, 0, f);
  }

  // ───────────────────────── dibujo ─────────────────────────

  private watchVideoFrames(): void {
    if (typeof this.video.requestVideoFrameCallback !== 'function') return;
    this.hasVfc = true;
    const onFrame = () => {
      this.needsRender = true;
      this.lastVfcAt = performance.now();
      this.vfcId = this.video.requestVideoFrameCallback(onFrame);
    };
    this.vfcId = this.video.requestVideoFrameCallback(onFrame);
  }

  private runPass(
    encoder: GPUCommandEncoder,
    view: GPUTextureView,
    pipeline: GPURenderPipeline,
    group: GPUBindGroup,
    extra: { viewport?: OutputRect; timestampWrites?: GPURenderPassTimestampWrites } = {},
  ): void {
    const pass = encoder.beginRenderPass({
      colorAttachments: [{ view, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }],
      timestampWrites: extra.timestampWrites,
    });
    pass.setPipeline(pipeline);
    pass.setBindGroup(0, group);
    if (extra.viewport) {
      const v = extra.viewport;
      pass.setViewport(v.x, v.y, v.width, v.height, 0, 1);
    }
    pass.draw(3);
    pass.end();
  }

  private render(): void {
    if (this.destroyed || this.video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) return;
    if (!this.ensureResources()) return;
    this.frameIndex++;
    this.writeUniforms();

    let external: GPUExternalTexture;
    try {
      external = this.device.importExternalTexture({ source: this.video });
    } catch (e) {
      this.fail(
        `La GPU no puede leer este video: ${e instanceof Error ? e.message : String(e)}. ` +
          'Si es una URL externa, el servidor debe enviar cabeceras CORS.',
      );
      return;
    }
    const ingestGroup = this.device.createBindGroup({
      layout: this.pipelines.ingest.getBindGroupLayout(0),
      entries: [
        { binding: 0, resource: this.sampler },
        { binding: 1, resource: external },
      ],
    });

    const timing = this.timing && !this.timing.pending ? this.timing : undefined;
    const encoder = this.device.createCommandEncoder({ label: 'frame' });
    const useDenoise = this.params.denoise > 0.001;

    this.runPass(encoder, this.views.src, this.pipelines.ingest, ingestGroup, {
      timestampWrites: timing ? { querySet: timing.querySet, beginningOfPassWriteIndex: 0 } : undefined,
    });
    if (useDenoise) this.runPass(encoder, this.views.denoised, this.pipelines.denoise, this.groups.denoise);
    this.runPass(
      encoder,
      this.views.mid,
      this.pipelines.scale,
      useDenoise ? this.groups.scaleHDenoised : this.groups.scaleHSrc,
    );
    this.runPass(encoder, this.views.up, this.pipelines.scale, this.groups.scaleV);
    this.runPass(encoder, this.context.getCurrentTexture().createView(), this.pipelines.final, this.groups.final, {
      viewport: this.out,
      timestampWrites: timing ? { querySet: timing.querySet, endOfPassWriteIndex: 1 } : undefined,
    });

    if (timing) {
      encoder.resolveQuerySet(timing.querySet, 0, 2, timing.resolve, 0);
      encoder.copyBufferToBuffer(timing.resolve, 0, timing.read, 0, 16);
    }
    this.device.queue.submit([encoder.finish()]);
    if (timing) this.readTiming(timing);
    this.tickStats();
  }

  private readTiming(timing: Timing): void {
    timing.pending = true;
    timing.read.mapAsync(MAP_MODE_READ).then(
      () => {
        const t = new BigUint64Array(timing.read.getMappedRange().slice(0));
        timing.read.unmap();
        timing.pending = false;
        const ms = Number(t[1] - t[0]) / 1e6;
        if (Number.isFinite(ms) && ms >= 0) {
          this.stats.gpuSum += ms;
          this.stats.gpuCount++;
        }
      },
      () => {
        timing.pending = false;
      },
    );
  }

  private tickStats(): void {
    this.stats.frames++;
    const now = performance.now();
    const dt = now - this.stats.since;
    if (dt < STATS_INTERVAL_MS) return;
    this.events.onStats?.({
      fps: Math.round((this.stats.frames * 1000) / dt),
      gpuMs: this.stats.gpuCount ? this.stats.gpuSum / this.stats.gpuCount : null,
      videoSize: this.srcSize,
      outputSize: { width: this.out.width, height: this.out.height },
      canvasSize: { width: this.canvas.width, height: this.canvas.height },
    });
    this.stats = { frames: 0, since: now, gpuSum: 0, gpuCount: 0 };
  }

  // ───────────────────────── errores ─────────────────────────

  private report(message: string): void {
    if (message === this.lastError) return;
    this.lastError = message;
    this.events.onError?.(message);
  }

  private fail(message: string): void {
    this.stop();
    this.report(message);
  }
}
