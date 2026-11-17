import type { EnhancerEvents, EnhancerParams, GpuInfo } from './enhancer/enhancer-params';
import { EnhancerSession } from './enhancer-session';
import { toEnhancerParams, DEFAULT_RESCALE } from './rescale-settings';
import type { VideoEnhancer } from './video-enhancer';

describe('EnhancerSession', () => {
  const gpu: GpuInfo = {
    vendor: 'amd',
    architecture: 'rdna',
    device: '',
    description: '',
    isFallback: false,
    timestampQuery: false,
    maxTextureSize: 8192,
  };
  const params: EnhancerParams = toEnhancerParams({ ...DEFAULT_RESCALE, view: 'enhanced' }, 0.5);

  function setup(create?: () => Promise<VideoEnhancer>) {
    const enhancer: VideoEnhancer = {
      gpuInfo: gpu,
      setParams: vi.fn(),
      resize: vi.fn(),
      requestRender: vi.fn(),
      start: vi.fn(),
      stop: vi.fn(),
      destroy: vi.fn(),
    };
    let events: EnhancerEvents = {};
    const factory = vi.fn(async (_canvas: HTMLCanvasElement, _video: HTMLVideoElement, given: EnhancerEvents) => {
      events = given;
      return create ? create() : enhancer;
    });
    const session = new EnhancerSession(
      document.createElement('canvas'),
      document.createElement('video'),
      document.createElement('div'),
      factory,
      { onReady: vi.fn(), onError: vi.fn() },
    );
    return { enhancer, factory, session, events: () => events };
  }

  it('en Original no crea nada: ni siquiera pide el dispositivo de GPU', async () => {
    const { factory, session } = setup();

    await session.apply('original', params);

    expect(factory).not.toHaveBeenCalled();
    expect(session.created).toBe(false);
  });

  it('crea el enhancer la primera vez que se pide Mejorado y lo arranca con los parámetros', async () => {
    const { enhancer, factory, session } = setup();

    await session.apply('enhanced', params);

    expect(factory).toHaveBeenCalledTimes(1);
    expect(session.created).toBe(true);
    expect(enhancer.setParams).toHaveBeenCalledWith(params);
    expect(enhancer.start).toHaveBeenCalledTimes(1);
    expect(enhancer.resize).toHaveBeenCalled();
  });

  it('no lo vuelve a crear al cambiar de vista y lo detiene al volver a Original', async () => {
    const { enhancer, factory, session } = setup();
    await session.apply('enhanced', params);

    await session.apply('original', params);
    expect(enhancer.stop).toHaveBeenCalledTimes(1);
    expect(enhancer.destroy).not.toHaveBeenCalled();

    await session.apply('compare', params);
    expect(factory).toHaveBeenCalledTimes(1);
    expect(enhancer.start).toHaveBeenCalledTimes(2);
  });

  it('dos peticiones seguidas mientras se crea solo crean un enhancer', async () => {
    const { factory, session } = setup();

    await Promise.all([session.apply('enhanced', params), session.apply('compare', params)]);

    expect(factory).toHaveBeenCalledTimes(1);
  });

  it('si se vuelve a Original mientras se crea, al terminar queda detenido', async () => {
    const { enhancer, session } = setup();

    const pending = session.apply('enhanced', params);
    await session.apply('original', params);
    await pending;

    expect(enhancer.start).not.toHaveBeenCalled();
    expect(enhancer.stop).toHaveBeenCalled();
  });

  it('avisa del error si no se puede crear, sin rechazar', async () => {
    const onError = vi.fn();
    const session = new EnhancerSession(
      document.createElement('canvas'),
      document.createElement('video'),
      document.createElement('div'),
      () => Promise.reject(new Error('Sin adaptador')),
      { onError },
    );

    await expect(session.apply('enhanced', params)).resolves.toBeUndefined();

    expect(onError).toHaveBeenCalledWith('Sin adaptador');
    expect(session.created).toBe(false);
  });

  it('destruir libera el enhancer y, si se destruye mientras se crea, lo libera al terminar', async () => {
    const { enhancer, session } = setup();
    await session.apply('enhanced', params);
    session.destroy();
    expect(enhancer.destroy).toHaveBeenCalledTimes(1);

    const second = setup();
    const pending = second.session.apply('enhanced', params);
    second.session.destroy();
    await pending;
    expect(second.enhancer.destroy).toHaveBeenCalledTimes(1);
    expect(second.enhancer.start).not.toHaveBeenCalled();
  });
});
