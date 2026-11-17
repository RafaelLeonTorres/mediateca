/*
 * Viene de mi proyecto reproductor-video-gpu (src/app/reproductor/enhancer/enhancer-shaders.ts) y se lleva a Mediateca
 * casi sin cambios.
 * Licencia: código propio. La nitidez adaptativa está inspirada en AMD FidelityFX CAS (licencia MIT), con
 * implementación propia.
 */
/**
 * Código WGSL de cada pase del pipeline. Todos los pases dibujan un triángulo que cubre el destino,
 * así que comparten el mismo vertex shader. Ver docs: ARQUITECTURA.md, sección 3.
 */

const VERTEX = /* wgsl */ `
struct VsOut {
  @builtin(position) pos: vec4f,
  @location(0) uv: vec2f,
};

@vertex
fn vs(@builtin(vertex_index) i: u32) -> VsOut {
  let x = f32((i << 1u) & 2u);
  let y = f32(i & 2u);
  var o: VsOut;
  o.pos = vec4f(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
  o.uv = vec2f(x, 1.0 - y);
  return o;
}
`;

/** Pase 0 — copia el cuadro del <video> (textura externa) a una textura normal RGBA16F. */
export const INGEST_WGSL = /* wgsl */ `${VERTEX}
@group(0) @binding(0) var samp: sampler;
@group(0) @binding(1) var ext: texture_external;

@fragment
fn fs(in: VsOut) -> @location(0) vec4f {
  return vec4f(textureSampleBaseClampToEdge(ext, samp, in.uv).rgb, 1.0);
}
`;

/** Pase 1 — filtro bilateral 5×5: promedia vecinos parecidos y respeta los bordes. */
export const DENOISE_WGSL = /* wgsl */ `${VERTEX}
struct DenoiseParams { amount: f32, p0: f32, p1: f32, p2: f32 };
@group(0) @binding(0) var src: texture_2d<f32>;
@group(0) @binding(1) var<uniform> P: DenoiseParams;

@fragment
fn fs(in: VsOut) -> @location(0) vec4f {
  let dims = vec2i(textureDimensions(src));
  let p = vec2i(in.pos.xy);
  let c = textureLoad(src, p, 0).rgb;
  let sigmaR = mix(0.015, 0.10, P.amount);
  let inv2sr2 = 1.0 / (2.0 * sigmaR * sigmaR);
  var acc = vec3f(0.0);
  var wsum = 0.0;
  for (var y = -2; y <= 2; y++) {
    for (var x = -2; x <= 2; x++) {
      let s = textureLoad(src, clamp(p + vec2i(x, y), vec2i(0), dims - vec2i(1)), 0).rgb;
      let dc = s - c;
      // peso espacial (sigma = 1.5 px) por peso de similitud de color
      let w = exp(-f32(x * x + y * y) * 0.2222222 - dot(dc, dc) * inv2sr2);
      acc += s * w;
      wsum += w;
    }
  }
  return vec4f(acc / wsum, 1.0);
}
`;

/**
 * Pases 2 y 3 — reescalado separable en un solo eje (S.axis: 0 = horizontal, 1 = vertical).
 * Núcleos: 0 bilineal, 1 bicúbico (Catmull-Rom), 2 Lanczos-2, 3 Lanczos-3.
 * Si el destino es menor que el origen el núcleo se ensancha para no producir aliasing.
 */
export const SCALE_WGSL = /* wgsl */ `${VERTEX}
struct ScaleParams { axis: u32, kernel: u32, antiring: f32, dstN: f32 };
@group(0) @binding(0) var src: texture_2d<f32>;
@group(0) @binding(1) var<uniform> S: ScaleParams;

const PI = 3.14159265358979;

fn sinc(x: f32) -> f32 {
  let a = PI * x;
  return select(sin(a) / a, 1.0, abs(a) < 0.0001);
}

fn kernelRadius(k: u32) -> f32 {
  if (k == 0u) { return 1.0; }
  if (k == 3u) { return 3.0; }
  return 2.0;
}

fn kernelWeight(x: f32, k: u32) -> f32 {
  let ax = abs(x);
  if (k == 0u) { return max(0.0, 1.0 - ax); }
  if (k == 1u) {
    if (ax < 1.0) { return (1.5 * ax - 2.5) * ax * ax + 1.0; }
    if (ax < 2.0) { return ((-0.5 * ax + 2.5) * ax - 4.0) * ax + 2.0; }
    return 0.0;
  }
  let r = kernelRadius(k);
  if (ax >= r) { return 0.0; }
  return sinc(ax) * sinc(ax / r);
}

@fragment
fn fs(in: VsOut) -> @location(0) vec4f {
  let dims = vec2i(textureDimensions(src));
  let horizontal = S.axis == 0u;
  let srcN = select(dims.y, dims.x, horizontal);
  let pos = in.pos.xy;
  // coordenada del píxel de salida en el eje activo (centro del píxel = n + 0.5) y píxel fijo del otro eje
  let o = select(pos.y, pos.x, horizontal);
  let other = i32(select(pos.x, pos.y, horizontal));

  let ratio = f32(srcN) / S.dstN;
  let stretch = max(ratio, 1.0);
  let radius = kernelRadius(S.kernel) * stretch;
  let center = o * ratio;
  let first = i32(ceil(center - 0.5 - radius));
  let last = i32(floor(center - 0.5 + radius));

  var acc = vec3f(0.0);
  var wsum = 0.0;
  var lo = vec3f(1e9);
  var hi = vec3f(-1e9);
  for (var i = first; i <= last; i++) {
    let d = f32(i) + 0.5 - center;
    let w = kernelWeight(d / stretch, S.kernel);
    let idx = clamp(i, 0, srcN - 1);
    let coord = select(vec2i(other, idx), vec2i(idx, other), horizontal);
    let s = textureLoad(src, coord, 0).rgb;
    acc += s * w;
    wsum += w;
    if (abs(d) <= stretch) {
      lo = min(lo, s);
      hi = max(hi, s);
    }
  }
  var result = acc / wsum;
  // anti-ringing: el resultado no puede salirse del rango de los píxeles más cercanos
  result = mix(result, clamp(result, lo, hi), S.antiring);
  return vec4f(result, 1.0);
}
`;

/**
 * Pase 4 — nitidez adaptativa + dithering + comparador, escrito en el lienzo.
 * La nitidez usa una cruz de 3×3 con mínimos/máximos "suaves": afila menos donde ya hay contraste alto
 * (idea de AMD FidelityFX CAS, licencia MIT). Implementación propia.
 */
export const FINAL_WGSL = /* wgsl */ `${VERTEX}
struct FinalParams {
  originX: f32, originY: f32, split: f32, sharpen: f32,
  dither: f32, frame: f32, p0: f32, p1: f32,
};
@group(0) @binding(0) var up: texture_2d<f32>;
@group(0) @binding(1) var orig: texture_2d<f32>;
@group(0) @binding(2) var linearSamp: sampler;
@group(0) @binding(3) var<uniform> F: FinalParams;

fn fetch(p: vec2i, dims: vec2i) -> vec3f {
  return textureLoad(up, clamp(p, vec2i(0), dims - vec2i(1)), 0).rgb;
}

fn sharpenAdaptive(p: vec2i, dims: vec2i, amount: f32) -> vec3f {
  let e = fetch(p, dims);
  if (amount <= 0.001) { return e; }
  let b = fetch(p + vec2i(0, -1), dims);
  let d = fetch(p + vec2i(-1, 0), dims);
  let f = fetch(p + vec2i(1, 0), dims);
  let h = fetch(p + vec2i(0, 1), dims);
  let a = fetch(p + vec2i(-1, -1), dims);
  let c = fetch(p + vec2i(1, -1), dims);
  let g = fetch(p + vec2i(-1, 1), dims);
  let i = fetch(p + vec2i(1, 1), dims);

  // mínimo y máximo "suaves" (cruz + esquinas), escalados x2
  let mnCross = min(min(min(d, e), min(f, b)), h);
  let mxCross = max(max(max(d, e), max(f, b)), h);
  let mn = mnCross + min(min(min(a, c), min(g, i)), mnCross);
  let mx = mxCross + max(max(max(a, c), max(g, i)), mxCross);

  // cuánto margen hay hasta los límites de la señal; cerca de 0 o 1 se afila menos
  let amp = sqrt(clamp(min(mn, vec3f(2.0) - mx) / max(mx, vec3f(0.0001)), vec3f(0.0), vec3f(1.0)));
  let w = amp * (-0.2 * amount);
  let rcp = vec3f(1.0) / (vec3f(1.0) + 4.0 * w);
  return clamp((b * w + d * w + f * w + h * w + e) * rcp, vec3f(0.0), vec3f(1.0));
}

// ruido de gradiente entrelazado (Jorge Jimenez): barato y sin patrón visible
fn ign(p: vec2f) -> f32 {
  return fract(52.9829189 * fract(dot(p, vec2f(0.06711056, 0.00583715))));
}

@fragment
fn fs(in: VsOut) -> @location(0) vec4f {
  let dims = vec2i(textureDimensions(up));
  let local = in.pos.xy - vec2f(F.originX, F.originY);
  var col: vec3f;
  if (local.x < F.split * f32(dims.x)) {
    col = textureSampleLevel(orig, linearSamp, in.uv, 0.0).rgb;
  } else {
    col = sharpenAdaptive(vec2i(local), dims, F.sharpen);
  }
  if (F.dither > 0.5) {
    col = col + (ign(in.pos.xy + vec2f(5.588238 * F.frame)) - 0.5) / 255.0;
  }
  return vec4f(clamp(col, vec3f(0.0), vec3f(1.0)), 1.0);
}
`;
