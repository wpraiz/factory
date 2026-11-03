// Orb de particulas em WebGL.
// A densidade dos assets de referencia (dezenas de milhares de pontos com brilho somado)
// nao cabe em canvas 2D. Aqui cada particula e um vertice; a forma e calculada no shader.
//
// A casca e um conjunto de aneis concentricos deslocados em Z por harmonicos de theta e
// projetados em perspectiva — e essa dobra que da o aspecto volumetrico do asset 1593.

const RINGS = 190;
const MAX_POINTS = 900;

const VERT = `
precision highp float;

attribute float a_ring;    // 0..RINGS-1
attribute float a_index;   // posicao dentro do anel
attribute float a_count;   // quantos pontos tem este anel

uniform float u_time;
uniform float u_level;
uniform float u_bass;
uniform float u_treble;
uniform float u_base;      // raio de referencia em pixels
uniform vec2  u_center;
uniform vec2  u_res;
uniform float u_dpr;

varying float v_f;         // 0 no miolo, 1 na borda
varying float v_bright;

const float TAU = 6.28318530718;

void main() {
  float f = a_ring / ${(RINGS - 1).toFixed(1)};
  float theta = (a_index / a_count) * TAU;

  // Fase defasada por anel: e o desalinhamento entre camadas que cria o moire do asset.
  float layer = a_ring * 0.013;

  // Ondulacao da casca. Graves mexem as ondas largas, agudos a textura fina.
  float w =
      1.00 * sin(3.0 * theta + u_time * 0.62 + layer * 3.0)
    + 0.55 * sin(5.0 * theta - u_time * 0.94 + layer * 5.0)
    + 0.32 * sin(7.0 * theta + u_time * 1.31 + layer * 7.0)
    + 0.18 * sin(11.0 * theta - u_time * 1.77 + layer * 11.0);

  float bandGain = 1.0 + u_bass * 0.8 * (1.0 - f) + u_treble * 1.1 * f;
  float amp = (0.09 + u_level * 0.38) * bandGain;

  // Raio base: aneis mais externos abrem mais, como as "petalas" soltas do asset.
  float radius = u_base * (0.002 + f * 0.998) * (1.0 + u_level * 0.16);

  // Deslocamento em Z: a casca se dobra em vez de so ondular no plano.
  float z = w * amp * radius * 0.40 * (0.30 + f);
  float rr = radius + w * amp * radius * 0.10;

  vec3 p = vec3(cos(theta) * rr, sin(theta) * rr, z);

  // Giro lento em dois eixos: mostra a dobra da casca em vez de um circulo chapado.
  float ax = 0.30 + sin(u_time * 0.21) * 0.09;
  float ay = sin(u_time * 0.13) * 0.22;
  p = vec3(p.x, p.y * cos(ax) - p.z * sin(ax), p.y * sin(ax) + p.z * cos(ax));
  p = vec3(p.x * cos(ay) + p.z * sin(ay), p.y, -p.x * sin(ay) + p.z * cos(ay));

  float focal = u_base * 3.4;
  float persp = focal / max(focal - p.z, focal * 0.55);

  vec2 px = u_center + p.xy * persp;
  gl_Position = vec4((px / u_res) * 2.0 - 1.0, 0.0, 1.0);
  gl_Position.y = -gl_Position.y;

  // Pontos mais proximos ficam maiores e mais fortes: da profundidade real.
  gl_PointSize = max(1.0, (2.35 - f * 0.85) * persp * u_dpr);

  v_f = f;
  v_bright = persp * (0.55 + u_level * 0.75);
}
`;

const FRAG = `
precision highp float;

varying float v_f;
varying float v_bright;

uniform float u_level;

void main() {
  // Ponto redondo com borda suave: quadrado duro vira serrilha na soma de brilho.
  vec2 d = gl_PointCoord - 0.5;
  float r = length(d);
  float a = smoothstep(0.5, 0.12, r);
  if (a <= 0.001) discard;

  // Roxo no miolo -> ciano na borda, como o anel do asset 2911473.
  vec3 roxo  = vec3(0.62, 0.36, 1.00);
  vec3 azul  = vec3(0.30, 0.55, 1.00);
  vec3 ciano = vec3(0.22, 0.88, 1.00);
  vec3 cor = v_f < 0.62
    ? mix(roxo, azul, v_f / 0.62)
    : mix(azul, ciano, (v_f - 0.62) / 0.38);

  // Nucleo esbranquicado quando a voz sobe.
  cor = mix(cor, vec3(1.0), (1.0 - v_f) * u_level * 0.28);

  float intensidade = a * v_bright * (0.14 + v_f * 0.34) * (0.85 + u_level * 1.05);

  // Alpha fica em zero de proposito: a particula SOMA luz sobre o fundo, nunca o substitui.
  gl_FragColor = vec4(cor * intensidade, 1.0);
}
`;

const BG_VERT = `
attribute vec2 a_pos;
varying vec2 v_uv;
void main() {
  v_uv = a_pos * 0.5 + 0.5;
  gl_Position = vec4(a_pos, 0.0, 1.0);
}
`;

const BG_FRAG = `
precision highp float;
varying vec2 v_uv;
uniform vec2 u_res;
uniform float u_level;

void main() {
  vec2 p = v_uv;
  p.y = 1.0 - p.y;

  // Elipse centrada no orb, achatada para acompanhar a proporcao da tela.
  vec2 d = (p - vec2(0.5, 0.38)) * vec2(u_res.x / u_res.y, 1.0) * 1.45;
  float r = length(d);

  vec3 perto  = vec3(0.075, 0.105, 0.36);
  vec3 meio   = vec3(0.030, 0.055, 0.21);
  vec3 longe  = vec3(0.004, 0.010, 0.06);

  vec3 cor = r < 0.45
    ? mix(perto, meio, smoothstep(0.0, 0.45, r))
    : mix(meio, longe, smoothstep(0.45, 1.05, r));

  // A voz acende de leve o halo atras do orb.
  cor += vec3(0.10, 0.05, 0.24) * u_level * exp(-r * 3.2);

  gl_FragColor = vec4(cor, 1.0);
}
`;

function compile(gl, type, src) {
  const sh = gl.createShader(type);
  gl.shaderSource(sh, src);
  gl.compileShader(sh);
  if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
    throw new Error('shader: ' + gl.getShaderInfoLog(sh));
  }
  return sh;
}

export class OrbGL {
  // Devolve null quando a GPU/driver recusa WebGL — o chamador cai no render 2D.
  static create(canvas) {
    const opts = { alpha: false, antialias: false, powerPreference: 'high-performance' };
    const gl = canvas.getContext('webgl2', opts) || canvas.getContext('webgl', opts);
    if (!gl) return null;
    try {
      return new OrbGL(canvas, gl);
    } catch (err) {
      console.warn('[voz] WebGL indisponivel:', err.message);
      return null;
    }
  }

  constructor(canvas, gl) {
    this.canvas = canvas;
    this.gl = gl;
    this.t = 0;
    this.level = 0;
    this.bass = 0;
    this.mid = 0;
    this.treble = 0;

    const prog = gl.createProgram();
    gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, VERT));
    gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, FRAG));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
      throw new Error('link: ' + gl.getProgramInfoLog(prog));
    }
    gl.useProgram(prog);
    this.prog = prog;

    this._buildGeometry();
    this._buildBackground();

    this.u = {};
    for (const n of ['u_time', 'u_level', 'u_bass', 'u_treble', 'u_base', 'u_center', 'u_res', 'u_dpr']) {
      this.u[n] = gl.getUniformLocation(prog, n);
    }

    // Brilho somado: onde muitas particulas se cruzam, a luz acumula.
    gl.disable(gl.DEPTH_TEST);
    gl.blendFunc(gl.ONE, gl.ONE);

    this.resize();
  }

  _buildBackground() {
    const gl = this.gl;
    const prog = gl.createProgram();
    gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, BG_VERT));
    gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, BG_FRAG));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
      throw new Error('link fundo: ' + gl.getProgramInfoLog(prog));
    }
    this.bgProg = prog;
    this.bgU = {
      res: gl.getUniformLocation(prog, 'u_res'),
      level: gl.getUniformLocation(prog, 'u_level'),
    };

    this.bgBuf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, this.bgBuf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    this.bgLoc = gl.getAttribLocation(prog, 'a_pos');
  }

  _buildGeometry() {
    const gl = this.gl;
    const rings = [];
    let total = 0;
    for (let i = 0; i < RINGS; i++) {
      const f = i / (RINGS - 1);
      const n = Math.max(48, Math.round(MAX_POINTS * (0.18 + f * 0.82)));
      rings.push(n);
      total += n;
    }

    const data = new Float32Array(total * 3);
    let o = 0;
    for (let i = 0; i < RINGS; i++) {
      for (let p = 0; p < rings[i]; p++) {
        data[o++] = i;
        data[o++] = p;
        data[o++] = rings[i];
      }
    }
    this.count = total;

    const buf = gl.createBuffer();
    this.buf = buf;
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);

    const stride = 3 * 4;
    const attach = (name, offset) => {
      const loc = gl.getAttribLocation(this.prog, name);
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, 1, gl.FLOAT, false, stride, offset);
    };
    attach('a_ring', 0);
    attach('a_index', 4);
    attach('a_count', 8);
  }

  // Libera GPU e contexto. Necessario quando o orb e criado e destruido (troca de aba, modal):
  // o navegador limita contextos WebGL vivos e nao recolhe sozinho.
  dispose() {
    const gl = this.gl;
    if (!gl) return;
    gl.deleteBuffer(this.buf);
    gl.deleteBuffer(this.bgBuf);
    gl.deleteProgram(this.prog);
    gl.deleteProgram(this.bgProg);
    const ext = gl.getExtension('WEBGL_lose_context');
    if (ext) ext.loseContext();
    this.gl = null;
  }

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = this.canvas.clientWidth;
    const h = this.canvas.clientHeight;
    this.canvas.width = Math.round(w * dpr);
    this.canvas.height = Math.round(h * dpr);
    this.gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    this.dpr = dpr;
    this.w = w;
    this.h = h;
    this.base = Math.min(w, h * 1.15) * 0.195;
  }

  update(dt, metrics) {
    this.t += dt;
    const target = metrics || { level: 0, bass: 0, mid: 0, treble: 0 };
    // Ataque rapido, decaimento lento: salta na silaba, desce macio.
    const k = (cur, next) => cur + (next - cur) * Math.min(1, (next > cur ? 16 : 3.2) * dt);
    this.level = k(this.level, target.level);
    this.bass = k(this.bass, target.bass);
    this.mid = k(this.mid, target.mid);
    this.treble = k(this.treble, target.treble);
  }

  draw() {
    const gl = this.gl;
    if (!gl) return; // depois de dispose()
    const cy = this.h * 0.38;

    // 1) Fundo opaco.
    gl.disable(gl.BLEND);
    gl.useProgram(this.bgProg);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.bgBuf);
    gl.enableVertexAttribArray(this.bgLoc);
    gl.vertexAttribPointer(this.bgLoc, 2, gl.FLOAT, false, 0, 0);
    gl.uniform2f(this.bgU.res, this.canvas.width, this.canvas.height);
    gl.uniform1f(this.bgU.level, this.level);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.disableVertexAttribArray(this.bgLoc);

    // 2) Particulas somando luz por cima.
    gl.enable(gl.BLEND);
    gl.useProgram(this.prog);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buf);
    for (const [name, offset] of [['a_ring', 0], ['a_index', 4], ['a_count', 8]]) {
      const loc = gl.getAttribLocation(this.prog, name);
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, 1, gl.FLOAT, false, 12, offset);
    }
    gl.uniform1f(this.u.u_time, this.t);
    gl.uniform1f(this.u.u_level, this.level);
    gl.uniform1f(this.u.u_bass, this.bass);
    gl.uniform1f(this.u.u_treble, this.treble);
    gl.uniform1f(this.u.u_base, this.base);
    gl.uniform1f(this.u.u_dpr, this.dpr);
    gl.uniform2f(this.u.u_center, this.w / 2, cy);
    gl.uniform2f(this.u.u_res, this.w, this.h);

    gl.drawArrays(gl.POINTS, 0, this.count);
  }
}
