// Orb de particulas reativo a voz.
// Referencia visual: circle-made-wavy-lines (1593) + abstract-glowing-particles (2911473).
// Nada e importado dos assets: a forma e gerada por soma de senoides e desenhada em canvas 2D.

const TAU = Math.PI * 2;

// Harmonicos que dao a forma de "petala irregular" do asset 1593.
// freq = quantas ondulacoes na volta; speed = velocidade de rotacao da fase.
const HARMONICS = [
  { freq: 3, speed: 0.11, weight: 1.0 },
  { freq: 5, speed: -0.17, weight: 0.55 },
  { freq: 7, speed: 0.23, weight: 0.3 },
  { freq: 11, speed: -0.31, weight: 0.15 },
];

export class Orb {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d', { alpha: false });
    this.t = 0;

    this.rings = 84;        // aneis concentricos
    this.ringGap = 0.011;   // espacamento relativo ao raio base
    this.innerRadius = 0.06;

    // Estado suavizado (o audio cru treme demais para virar imagem).
    this.level = 0;
    this.bass = 0;
    this.mid = 0;
    this.treble = 0;

    this.resize();
  }

  // Simetrico ao OrbGL.dispose(): canvas 2D nao segura GPU, mas o chamador nao precisa saber
  // qual dos dois recebeu. Depois disso, draw() nao faz nada.
  dispose() {
    this.ctx = null;
  }

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = this.canvas.clientWidth;
    const h = this.canvas.clientHeight;
    this.canvas.width = Math.round(w * dpr);
    this.canvas.height = Math.round(h * dpr);
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.w = w;
    this.h = h;
    this.cx = w / 2;
    this.cy = h * 0.40;
    this.base = Math.min(w, h * 1.25) * 0.33;
    this.bg = null; // invalida o gradiente de fundo em cache
  }

  // metrics: { level, bass, mid, treble } em 0..1 — ou null quando em repouso.
  update(dt, metrics) {
    this.t += dt;

    const target = metrics || { level: 0, bass: 0, mid: 0, treble: 0 };
    // Ataque rapido, decaimento lento: a onda salta na silaba e desce macio.
    const k = (cur, next) => {
      const rate = next > cur ? 14 : 3.5;
      return cur + (next - cur) * Math.min(1, rate * dt);
    };
    this.level = k(this.level, target.level);
    this.bass = k(this.bass, target.bass);
    this.mid = k(this.mid, target.mid);
    this.treble = k(this.treble, target.treble);
  }

  drawBackground() {
    const ctx = this.ctx;
    if (!this.bg) {
      const g = ctx.createRadialGradient(this.cx, this.cy, 0, this.cx, this.cy, Math.max(this.w, this.h) * 0.75);
      g.addColorStop(0, '#0d1547');
      g.addColorStop(0.55, '#070c2e');
      g.addColorStop(1, '#03051a');
      this.bg = g;
    }
    ctx.fillStyle = this.bg;
    ctx.fillRect(0, 0, this.w, this.h);
  }

  draw() {
    const ctx = this.ctx;
    if (!ctx) return; // depois de dispose()
    this.drawBackground();

    // A voz empurra o orb para fora e aumenta o brilho.
    const swell = 1 + this.level * 0.22;
    const amp = 0.05 + this.level * 0.26;

    ctx.globalCompositeOperation = 'lighter';

    for (let i = 0; i < this.rings; i++) {
      const f = i / (this.rings - 1);                       // 0 no centro, 1 na borda
      const radius = this.base * (this.innerRadius + f * (1 - this.innerRadius)) * swell;

      // Densidade proporcional ao perimetro, com teto para nao matar o frame rate.
      const points = Math.min(260, Math.max(28, Math.round(radius * 1.15)));

      // Cada anel atrasa a fase: e isso que cria o desalinhamento em camadas do asset.
      const layer = i * 0.052;

      // Graves mexem as ondulacoes largas, agudos a textura fina.
      const bandGain = 1 + this.bass * 0.9 * (1 - f) + this.treble * 1.2 * f;

      // Cor: roxo no miolo -> ciano na borda, como o anel do 2911473.
      const hue = 282 - f * 96;
      const light = 68 - f * 8 + this.level * 14;
      const alpha = (0.85 - f * 0.45) * (0.45 + this.level * 0.55);
      ctx.fillStyle = `hsla(${hue}, 100%, ${light}%, ${alpha.toFixed(3)})`;

      const size = Math.max(0.7, 1.9 - f * 1.1);

      for (let p = 0; p < points; p++) {
        const theta = (p / points) * TAU;

        let d = 0;
        for (const h of HARMONICS) {
          d += h.weight * Math.sin(h.freq * theta + this.t * h.speed * TAU + layer * h.freq);
        }
        d *= amp * bandGain * radius * 0.32;

        // Respiro lento para o orb nunca ficar morto no silencio.
        const breath = Math.sin(this.t * 0.6 + layer * 2) * radius * 0.012;

        const r = radius + d + breath;
        ctx.fillRect(this.cx + Math.cos(theta) * r - size / 2, this.cy + Math.sin(theta) * r - size / 2, size, size);
      }
    }

    this.drawCore();
    ctx.globalCompositeOperation = 'source-over';
  }

  // Nucleo brilhante do centro (o "olho" do asset 1593).
  drawCore() {
    const ctx = this.ctx;
    const r = this.base * (0.08 + this.level * 0.1);
    const g = ctx.createRadialGradient(this.cx, this.cy, 0, this.cx, this.cy, r);
    const a = 0.35 + this.level * 0.5;
    g.addColorStop(0, `rgba(190, 235, 255, ${a})`);
    g.addColorStop(0.4, `rgba(110, 160, 255, ${a * 0.4})`);
    g.addColorStop(1, 'rgba(80, 60, 255, 0)');
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(this.cx, this.cy, r, 0, TAU);
    ctx.fill();
  }
}
