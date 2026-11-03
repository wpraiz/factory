// MATILHA do Odin — GERI e FREKI (lobos de guerra) e HUGINN e MUNINN (corvos de agouro). Fichas em pack\*.md.
// v2 (v1 looked like a child's drawing): arte animal nórdica entalhada, não ilustração naturalista.
// Referências (pesquisadas antes do traço):
//  · Urnes / Ringerike / Mammen — corpos em fita, olho amendoado estreito, apêndices que viram gavinhas e volutas,
//    juba e cauda em tufos de gavinhas (Ringerike), corpo com "pelleting" e linhas secundárias (Mammen):
//    https://hurstwic.com/history/articles/manufacturing/text/norse_art.htm ·
//    https://www.sciencenordic.com/photo-gallery-six-styles-viking-art · https://gelmir.com/compendium_item/urnes-style
//  · Borre — corrente de anéis e "gripping beast" preso ao entrelaço: https://gelmir.com/compendium_item/borre-style
//  · Placas de elmo de Vendel (séc. VII, Uppland): cavaleiro, provavelmente Odin, com dois corvos em voo:
//    https://historiska.se/en/explore-history/history-hub/3d-vendel-helmets/
//  · Pedras pintadas de Gotland (Stora Hammars, Tängelgårda): figuras entalhadas em faixas, gastas pelo tempo
//    (https://gotlandicpicturestones.se.gotlandsmuseum.se/s/index/page/iconography). O valknut dessas pedras NÃO entra.
// Linguagem: contorno DUPLO como sulco de cinzel; fita que passa por cima e por baixo (entrelaço com corte de verdade);
// espiral nas juntas; boca aberta com presas; nariz e queixo terminando em voluta; base = faixa-serpente rúnica de
// pedra rúnica com o nome do membro em Futhark antigo. Paleta: ferro e prata velha; a cor do membro é só o fio rúnico
// (sulco central, olho, runas). Partículas = desgaste e fagulha de forja. Nada de símbolo de ódio.
// Mesmo motor do rosto (avatar.js): geometria em coordenadas, "ossos" com parâmetro de estado, linhas finas + pontos,
// um único canvas WebGL sem clique. TUDO se mexe sempre, devagar.
// API: criarMatilha(host, {layout:'odin'|'frota', paineis?:{geri:el,...}, odin?:el}) -> { set(id,{estado,nivel}), parar() }
// estados: descansa | alerta | trabalhando | entregou | falhou (entregou/falhou = gesto de ~2,5 s e volta à base).
export function criarMatilha(HOST, OPTS = {}) {
const LAYOUT = OPTS.layout === 'frota' ? 'frota' : 'odin';
const TAU = Math.PI * 2;
const sm = (a, b, v) => { const t = Math.min(1, Math.max(0, (v - a) / (b - a))); return t * t * (3 - 2 * t); };
const lerp = (a, b, t) => a + (b - a) * t;
const cl = (v, a, b) => Math.max(a, Math.min(b, v));
const sm1 = (c, t, r, dt) => c + (t - c) * Math.min(1, r * dt);
function rng(seed) { let x = seed >>> 0 || 1; return () => ((x ^= x << 13, x ^= x >>> 17, x ^= x << 5) >>> 0) / 4294967296; }
const hex = c => [1, 3, 5].map(i => parseInt(c.slice(i, i + 2), 16) / 255);
function cr(P, n = 6) {
  const o = [];
  for (let i = 0; i < P.length - 1; i++) {
    const a = P[Math.max(0, i - 1)], b = P[i], c = P[i + 1], d = P[Math.min(P.length - 1, i + 2)];
    for (let k = 0; k < n; k++) { const t = k / n, t2 = t * t, t3 = t2 * t; o.push([0, 1].map(j => 0.5 * (2 * b[j] + (-a[j] + c[j]) * t + (2 * a[j] - 5 * b[j] + 4 * c[j] - d[j]) * t2 + (-a[j] + 3 * b[j] - 3 * c[j] + d[j]) * t3))); }
  }
  o.push(P[P.length - 1].slice()); return o;
}
function dentro(poly, x, y) { let c = false; for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) { const [xi, yi] = poly[i], [xj, yj] = poly[j]; if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c; } return c; }
function bbox(poly) { const b = [1e9, 1e9, -1e9, -1e9]; for (const [x, y] of poly) { b[0] = Math.min(b[0], x); b[1] = Math.min(b[1], y); b[2] = Math.max(b[2], x); b[3] = Math.max(b[3], y); } return b; }
const fechar = P => P.concat([P[0]]);
const escalar = (P, k, c) => P.map(p => [c[0] + (p[0] - c[0]) * k, c[1] + (p[1] - c[1]) * k]);
const centro = P => { let x = 0, y = 0; for (const p of P) { x += p[0]; y += p[1]; } return [x / P.length, y / P.length]; };

// Futhark antigo, traço reto (feito para ser entalhado) — o mesmo desenho das runas do rosto
const FUTHARK = {
  'ᚠ': [[[.3, 0], [.3, 1]], [[.3, .12], [.8, -.05]], [[.3, .42], [.8, .22]]], 'ᚢ': [[[.25, 1], [.25, 0], [.75, .3], [.75, 1]]],
  'ᚨ': [[[.3, 0], [.3, 1]], [[.3, .05], [.75, .32]], [[.3, .35], [.75, .62]]], 'ᚱ': [[[.3, 1], [.3, 0], [.72, .25], [.3, .5], [.75, 1]]],
  'ᚲ': [[[.72, 0], [.3, .5], [.72, 1]]], 'ᚷ': [[[.2, 0], [.8, 1]], [[.8, 0], [.2, 1]]], 'ᚺ': [[[.25, 0], [.25, 1]], [[.75, 0], [.75, 1]], [[.25, .35], [.75, .6]]],
  'ᚾ': [[[.5, 0], [.5, 1]], [[.3, .35], [.7, .6]]], 'ᛁ': [[[.5, 0], [.5, 1]]], 'ᛏ': [[[.5, 0], [.5, 1]], [[.2, .3], [.5, 0], [.8, .3]]],
  'ᛖ': [[[.25, 1], [.25, 0], [.5, .3], [.75, 0], [.75, 1]]], 'ᛗ': [[[.25, 1], [.25, 0], [.75, .45]], [[.75, 1], [.75, 0], [.25, .45]]],
  'ᛚ': [[[.3, 1], [.3, 0], [.72, .3]]],
};

/* ------------------------------ cinzel: construtor de geometria ------------------------------ */
// vértice estático (9 floats): x, y, osso, peso, tipo, sombra, s (ao longo da fita 0..1), rnd, flex
// linhas: 0 sulco externo · 1 sulco interno (contorno duplo) · 2 fio rúnico (cor do membro) · 3 olho/brilho ·
//         4 detalhe entalhado · 5 moldura · 6 racha (só aparece ao falhar) · 7 runa gravada
// pontos: 8 desgaste · 9 halo do olho · 10 fagulha de forja · 11 "pelleting" (Mammen)
function cinzel(seed, osso) {
  const R = rng(seed), riscos = [], occ = [], P = [];
  let gid = 0;
  const bOf = (p, o, t) => p.b || (o.bone && o.bone(p, t)) || osso(p[0], p[1]);
  function risco(pts, o = {}) { riscos.push({ pts, kind: o.kind ?? 4, z: o.z ?? 5, g: o.g ?? -1, bone: o.bone, sh: o.sh ?? 0.75, s0: o.s0 ?? R(), s1: o.s1 ?? null, flex: o.flex || null, gasto: o.gasto ?? 1, dust: o.dust ?? 0.12 }); }
  function ocluir(poly, z, g) { occ.push({ poly, z, g, bb: bbox(poly) }); }
  // fita: contorno duplo (sulco externo + interno), fio central opcional, tampas e polígono que esconde o que está por baixo
  function fita(c, wf, o = {}) {
    const g = o.g ?? ++gid, n = c.length - 1, L = [], Rr = [], Li = [], Ri = [], z = o.z ?? 2;
    for (let i = 0; i <= n; i++) {
      const a = c[Math.max(0, i - 1)], b = c[Math.min(n, i + 1)], dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1, nx = -dy / l, ny = dx / l, w = wf(i / n), p = c[i];
      L.push([p[0] + nx * w, p[1] + ny * w]); Rr.push([p[0] - nx * w, p[1] - ny * w]);
      Li.push([p[0] + nx * w * 0.6, p[1] + ny * w * 0.6]); Ri.push([p[0] - nx * w * 0.6, p[1] - ny * w * 0.6]);
      if (p.b) for (const q of [L, Rr, Li, Ri]) q[i].b = p.b;   // o osso do centro vale pras bordas
    }
    const base = { z, g, bone: o.bone, s0: 0, s1: 1, gasto: o.gasto ?? 1 };
    risco(L, { ...base, kind: o.k0 ?? 0, sh: o.shL ?? 0.95 }); risco(Rr, { ...base, kind: o.k0 ?? 0, sh: o.shR ?? 0.7 });
    if (o.duplo !== false) { risco(Li, { ...base, kind: 1, sh: 0.6 }); risco(Ri, { ...base, kind: 1, sh: 0.5 }); }
    if (o.fio) risco(c, { ...base, kind: 2, sh: 0.8, dust: 0 });
    if (o.ini !== false) risco(cr([L[0], [lerp(L[0][0], Rr[0][0], 0.5) - (c[1][0] - c[0][0]) * 0.6, lerp(L[0][1], Rr[0][1], 0.5) - (c[1][1] - c[0][1]) * 0.6], Rr[0]], 4).map(q => (q.b = c[0].b, q)), { ...base, kind: o.k0 ?? 0, sh: 0.8 });
    if (o.fim) risco(cr([L[n], [lerp(L[n][0], Rr[n][0], 0.5) + (c[n][0] - c[n - 1][0]) * 0.6, lerp(L[n][1], Rr[n][1], 0.5) + (c[n][1] - c[n - 1][1]) * 0.6], Rr[n]], 4).map(q => (q.b = c[n].b, q)), { ...base, kind: o.k0 ?? 0, sh: 0.8 });
    if (o.talhas) for (let i = 2; i < n - 1; i += o.talhas) risco([Li[i], Ri[i]].map(q => (q.b = c[i].b, q)), { ...base, kind: 4, sh: 0.42, dust: 0 });
    if (o.pellet) for (let i = 2; i < n - 1; i += 3) { const p = c[i]; P.push(p[0], p[1], ...bOf(p, o, i / n), 11, 0.7, i / n, R(), 0.5 + 0.5 * R()); }
    if (o.ocluir !== false) ocluir([...L, ...Rr.slice().reverse()], z, g);
    return { L, Rr, g };
  }
  // gavinha: linha que se enrola em voluta e afina até sumir (juba, cauda, lábios — Ringerike/Urnes)
  function gavinha(p0, ang, len, curl, w0, o = {}) {
    const N = Math.max(10, Math.round(len / 0.01)), ds = len / N, c = [];
    let x = p0[0], y = p0[1], a = ang;
    for (let i = 0; i <= N; i++) { c.push([x, y]); const t = i / N; a += curl * ds * (0.25 + 2.6 * t * t); x += Math.cos(a) * ds; y += Math.sin(a) * ds; }
    const bone = o.bone || (o.boneT ? (p, t) => o.boneT(t) : undefined);
    if (o.boneT) c.forEach((p, i) => { p.b = o.boneT(i / N); });
    return fita(c, t => w0 * Math.pow(1 - t, 0.85) + 0.0025, { ...o, bone, duplo: w0 > 0.025, fim: false });
  }
  // espiral de junta (ombro/quadril em Mammen/Ringerike): dois sulcos que giram para dentro + anel
  function espiral(c, r, o = {}) {
    const z = o.z ?? 6, g = ++gid, anel = [];
    for (let a = 0; a <= TAU + 0.01; a += TAU / 40) anel.push([c[0] + Math.cos(a) * r, c[1] + Math.sin(a) * r]);
    risco(anel, { kind: 0, z, g, sh: 0.95, bone: o.bone });
    for (const fase of [0, Math.PI]) { const s = []; for (let k = 0; k <= 60; k++) { const t = k / 60, a = fase + t * 2.4 * Math.PI * (o.dir ?? 1), rr = r * (0.85 - 0.75 * t); s.push([c[0] + Math.cos(a) * rr, c[1] + Math.sin(a) * rr]); } risco(s, { kind: 4, z, g, sh: 0.7 - 0.2 * (fase > 0), bone: o.bone }); }
    ocluir(anel, z, g);
  }
  // runa gravada (sulco duplo), girada no ângulo da fita
  function runa(ch, c, h, ang, o = {}) {
    const st = FUTHARK[ch]; if (!st) return;
    const ca = Math.cos(ang), sa = Math.sin(ang);
    const at = (u, v) => { const x = (u - 0.5) * h * 0.6, y = (v - 0.5) * h; return [c[0] + x * ca - y * sa, c[1] + x * sa + y * ca]; };
    for (const s of st) { const pts = []; for (let k = 0; k < s.length - 1; k++) for (let j = 0; j < 4; j++) pts.push(at(lerp(s[k][0], s[k + 1][0], j / 4), lerp(s[k][1], s[k + 1][1], j / 4))); pts.push(at(...s[s.length - 1])); risco(pts, { kind: 7, z: o.z ?? 9, sh: 0.9, bone: o.bone, dust: 0, gasto: 0 }); }
  }
  // racha: linha quebrada que só acende ao falhar
  function racha(p0, ang, len) {
    const pts = []; let x = p0[0], y = p0[1], a = ang;
    for (let i = 0; i <= 9; i++) { pts.push([x, y]); a += (R() - 0.5) * 1.2; x += Math.cos(a) * len / 9; y += Math.sin(a) * len / 9; }
    risco(pts, { kind: 6, z: 20, sh: 1, dust: 0, gasto: 0 });
    if (R() < 0.8) { const k = 3 + Math.floor(R() * 4); const q = pts[k]; risco([q, [q[0] + Math.cos(a + 1.2) * len * 0.3, q[1] + Math.sin(a + 1.2) * len * 0.3]], { kind: 6, z: 20, sh: 0.8, dust: 0, gasto: 0 }); }
  }
  // faixa-serpente rúnica (base de pedra rúnica): fita com o nome em runas, cabeça de serpente na ponta de dentro,
  // a cauda dá um laço que passa por cima de si mesma na ponta de fora
  function faixaRunica(x0, x1, y, nome, o = {}) {
    const B = o.bone || (() => [o.osso ?? 0, 1]);
    const c = []; for (let i = 0; i <= 60; i++) { const u = i / 60; c.push([lerp(x0, x1, u), y + 0.035 * Math.sin(Math.PI * u)]); }
    const hw = o.hw ?? 0.075;
    fita(c, () => hw, { z: 0, bone: B, ini: false, gasto: 1.4, shL: 0.75, shR: 0.55 });
    // runas do nome, espaçadas no meio da faixa
    const ch = [...nome], n = ch.length;
    ch.forEach((r, i) => { const u = 0.28 + (i + 0.5) / n * 0.5, k = Math.round(u * 60), p = c[k], q = c[Math.min(60, k + 1)]; runa(r, p, hw * 1.5, Math.atan2(q[1] - p[1], q[0] - p[0]), { z: 1, bone: B }); });
    // cabeça de serpente (lado de dentro, x1): crânio em gota, olho amendoado, lábio em voluta
    const hx = x1, hy = y, d = Math.sign(x1 - x0);
    const cab = [[hx, hy - hw], [hx + d * 0.07, hy - hw * 1.5], [hx + d * 0.17, hy - hw * 0.9], [hx + d * 0.2, hy], [hx + d * 0.15, hy + hw * 0.8], [hx + d * 0.04, hy + hw * 1.1], [hx, hy + hw]];
    risco(cr(cab, 5), { kind: 0, z: 1, sh: 0.85, bone: B }); risco(cr(escalar(cab, 0.7, [hx + d * 0.09, hy]), 4), { kind: 1, z: 1, sh: 0.5, bone: B });
    risco(cr([[hx + d * 0.06, hy - hw * 0.15], [hx + d * 0.1, hy - hw * 0.45], [hx + d * 0.15, hy - hw * 0.2]], 3), { kind: 3, z: 1, sh: 0.9, bone: B });
    gavinha([hx + d * 0.2, hy + hw * 0.1], d > 0 ? 0.9 : Math.PI - 0.9, 0.12, d * 9, 0.012, { z: 1, bone: B });
    // laço da cauda (ponta de fora): sobe, dá a volta e cruza por cima da própria faixa
    const l0 = [x0, y], e = -d;
    const laco = cr([l0, [x0 + e * 0.12, y - 0.02], [x0 + e * 0.17, y - 0.14], [x0 + e * 0.06, y - 0.22], [x0 - e * 0.05, y - 0.12], [x0 - e * 0.02, y + 0.06], [x0 + e * 0.08, y + 0.16]], 6);
    fita(laco, t => hw * (1 - 0.55 * t) + 0.004, { z: 0.5, bone: B, ini: false, gasto: 1.4 });
    gavinha(laco[laco.length - 1], e > 0 ? 0.4 : Math.PI - 0.4, 0.14, e * 10, hw * 0.45, { z: 0.6, bone: B });
  }
  // emissão: corta o que fica por baixo de uma fita mais alta (entrelaço de verdade) e gasta o entalhe aqui e ali
  function fim() {
    const L = [];
    const escondido = (x, y, z, g) => { for (const o of occ) { if (o.z <= z || o.g === g) continue; const b = o.bb; if (x < b[0] || x > b[2] || y < b[1] || y > b[3]) continue; if (dentro(o.poly, x, y)) return true; } return false; };
    const ph = R() * 10;
    const gasto = (x, y) => Math.sin(x * 21.3 + y * 13.7 + ph) * Math.sin(x * 7.9 - y * 17.1 + ph * 1.3);
    for (const r of riscos) {
      const n = r.pts.length - 1;
      for (let i = 0; i < n; i++) {
        const a = r.pts[i], b = r.pts[i + 1], mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2;
        if (escondido(mx, my, r.z, r.g)) continue;
        if (r.gasto && gasto(mx, my) > 1 - 0.13 * r.gasto && r.kind <= 1) continue;   // falha do desgaste
        for (const [p, k] of [[a, i], [b, i + 1]]) {
          const t = k / Math.max(1, n), bo = bOf(p, r, t);
          L.push(p[0], p[1], bo[0], bo[1], r.kind, r.sh * (0.85 + 0.3 * R()), r.s1 !== null ? lerp(r.s0, r.s1, t) : r.s0, R(), r.flex ? r.flex(t) : 0);
        }
        if (r.dust && R() < r.dust) { const bo = bOf(a, r, i / Math.max(1, n)); P.push(a[0] + (R() - 0.5) * 0.02, a[1] + (R() - 0.5) * 0.02, bo[0], bo[1], 8, 0.4 + 0.5 * R(), R(), R(), R()); }
      }
    }
    return { L, P };
  }
  function fagulhas(caixa, n, bone) { for (let i = 0; i < n; i++) { const x = lerp(caixa[0], caixa[2], R()), y = lerp(caixa[1], caixa[3], R()); P.push(x, y, bone, 1, 10, 0.8, R(), R(), R()); } }
  return { R, risco, fita, gavinha, espiral, runa, racha, faixaRunica, fagulhas, ocluir, fim, P };
}

/* ------------------------------------ LOBO DE GUERRA ------------------------------------ */
// De perfil, em passo de guarda, olhando para +x (o Odin). y para baixo; patas em y=0; base rúnica em y≈0.1.
const LB = { ROOT: 0, BODY: 1, HEAD: 2, JAW: 3, MANE: 4, TAIL: 5, EYE: 6, FRAME: 7, EAR: 8 };
const PAR_LOBO = [0, 0, 1, 2, 1, 0, 2, 0, 2];
const PIV_LOBO = { chest: [0.05, -0.92], neck: [0.5, -1.26], jaw: [0.66, -1.31], mane: [0.42, -1.18], tail: [-0.58, -0.8], eye: [0.8, -1.47], ear: [0.56, -1.5], frame: [0, 0.1], runa: [1.0, -1.75] };
function loboOsso(x, y) {
  const wh = sm(-1.16, -1.32, y) * sm(0.36, 0.5, x);
  if (wh > 0.001) return [LB.HEAD, wh];
  return [LB.BODY, sm(0.0, -0.55, y)];
}
function lobo(seed, nome, ch) {
  const C = cinzel(seed, loboOsso), R = C.R;
  const H = () => [LB.HEAD, 1], J = () => [LB.JAW, 1];
  // cabeça: crânio longo, testa baixa, focinho que termina em voluta pra cima; boca aberta
  const cranio = [[0.48, -1.38], [0.52, -1.52], [0.64, -1.585], [0.78, -1.555], [0.9, -1.505], [1.02, -1.475], [1.12, -1.455], [1.14, -1.415], [1.09, -1.375], [0.94, -1.36], [0.78, -1.352], [0.66, -1.32], [0.6, -1.24], [0.5, -1.22]];
  C.risco(cr(fechar(cranio), 4), { kind: 0, z: 7, g: 900, bone: H, sh: 1.0, s0: 0, s1: 1 });
  C.risco(cr(fechar(escalar(cranio, 0.86, [0.74, -1.42])), 4), { kind: 1, z: 7, g: 900, bone: H, sh: 0.55 });
  C.ocluir(cranio, 7, 900);
  const mand = [[0.66, -1.32], [0.82, -1.312], [0.98, -1.3], [1.06, -1.285], [1.05, -1.235], [0.9, -1.205], [0.72, -1.195], [0.6, -1.225]];
  C.risco(cr(fechar(mand), 4), { kind: 0, z: 7.5, g: 901, bone: J, sh: 0.85 });
  C.risco(cr(escalar(mand, 0.8, [0.85, -1.26]).slice(0, 5), 4), { kind: 1, z: 7.5, g: 901, bone: J, sh: 0.45 });
  C.ocluir(mand, 7.5, 901);
  // presas: de cima pra baixo e de baixo pra cima (entrelaçam com a boca fechada; à mostra ao abrir)
  for (const [x, l] of [[1.06, 0.075], [0.9, 0.055], [0.78, 0.04]]) C.risco([[x - 0.018, -1.362], [x + 0.002, -1.362 + l], [x + 0.016, -1.362]], { kind: 4, z: 8, bone: H, sh: 1.05, dust: 0 });
  for (const [x, l] of [[0.99, 0.065], [0.84, 0.045]]) C.risco([[x - 0.016, -1.302], [x, -1.302 - l], [x + 0.016, -1.302]], { kind: 4, z: 8, bone: J, sh: 1.0, dust: 0 });
  // volutas do focinho e do queixo (Urnes)
  C.gavinha([1.13, -1.45], -1.9, 0.2, -9, 0.022, { z: 6.5, bone: H });
  C.gavinha([1.04, -1.24], 1.6, 0.2, 10, 0.02, { z: 7.6, bone: J });
  // olho amendoado com cauda pra trás + sobrancelha em crista
  const ol = PIV_LOBO.eye;
  const olho = [[ol[0] - 0.08, ol[1] + 0.018], [ol[0] - 0.01, ol[1] - 0.022], [ol[0] + 0.07, ol[1] - 0.008], [ol[0] - 0.005, ol[1] + 0.02], [ol[0] - 0.08, ol[1] + 0.018]];
  C.risco(cr(olho, 4), { kind: 3, z: 9, bone: () => [LB.EYE, 1], sh: 1, dust: 0 });
  C.risco([[ol[0] - 0.08, ol[1] + 0.018], [ol[0] - 0.17, ol[1] + 0.045]], { kind: 3, z: 9, bone: H, sh: 0.7, dust: 0 });
  C.P.push(ol[0] - 0.005, ol[1] - 0.002, LB.EYE, 1, 9, 1, 0, 0.5, 1);
  C.risco(cr([[0.66, -1.535], [0.76, -1.545], [0.9, -1.515]], 4), { kind: 4, z: 9, bone: H, sh: 0.9 });
  C.risco(cr([[0.86, -1.44], [0.96, -1.43], [1.06, -1.425]], 3), { kind: 4, z: 9, bone: H, sh: 0.45 });
  // orelha: folha longa varrida pra trás, com voluta
  const orelha = cr([[0.6, -1.53], [0.52, -1.64], [0.42, -1.75], [0.34, -1.8]], 6); orelha.forEach((p, i, a) => { p.b = [LB.EAR, sm(0, 0.4, i / (a.length - 1))]; });
  C.fita(orelha, t => 0.05 * Math.pow(1 - t, 0.7) + 0.004, { z: 6.8, bone: (p) => p.b, fim: false });
  // pescoço e corpo em fita (contorno duplo + fio rúnico + "pelleting"); peito largo, lombo arqueado
  C.fita(cr([[0.34, -0.92], [0.44, -1.08], [0.52, -1.25]], 6), t => 0.2 - 0.05 * t, { z: 3, pellet: true, ini: false });
  const corpo = cr([[0.44, -0.88], [0.2, -1.02], [-0.1, -1.05], [-0.4, -0.96], [-0.62, -0.82]], 8);
  C.fita(corpo, t => 0.22 - 0.06 * t, { z: 2, fio: true, ini: false, fim: false, talhas: 3 });
  C.runa(ch, [0.04, -0.99], 0.15, 0, { z: 9 });
  // juba de gavinhas eriçadas (Ringerike): da nuca ao lombo, por trás do pescoço
  const crista = cr([[0.54, -1.5], [0.48, -1.38], [0.4, -1.24], [0.3, -1.14], [0.16, -1.12], [0.0, -1.15], [-0.14, -1.13]], 3);
  crista.forEach((b, i, a) => {
    const u = i / (a.length - 1), len = (0.5 - 0.22 * u) * (0.8 + 0.4 * R()), ang = -2.0 - 0.75 * u + 0.25 * (R() - 0.5);
    C.gavinha(b, ang, len * 1.15, (i % 2 ? 1 : -1) * (4.2 + 1.6 * R()), 0.036, { z: 1.5 + i * 0.01, boneT: t => [LB.MANE, Math.pow(t, 0.8)] });
    if (i % 2 === 0 && i < a.length - 2) C.gavinha([b[0] + 0.03, b[1] + 0.04], ang + 0.4, len * 0.75, (i % 4 ? -1 : 1) * 6, 0.024, { z: 1.4, boneT: t => [LB.MANE, Math.pow(t, 0.8) * 0.7] });
  });
  for (let i = 0; i < 3; i++) C.gavinha([0.56 - i * 0.03, -1.24 + i * 0.04], 2.3 + 0.2 * i, 0.24 + 0.06 * i, (i % 2 ? 1 : -1) * 6, 0.022, { z: 2.9, bone: () => [LB.HEAD, 0.6] });   // barba do queixo
  // patas em fita com espiral na junta e garras em gancho; as de trás passam por baixo do corpo
  const garras = (x, y, d, z, sh) => { for (let k = 0; k < 3; k++) { const bx = x + d * (k * 0.035 - 0.02); C.risco(cr([[bx, y - 0.02], [bx + d * 0.04, y - 0.01], [bx + d * 0.055, y + 0.02]], 3), { kind: 4, z, sh, dust: 0 }); } };
  C.fita(cr([[0.38, -0.86], [0.52, -0.58], [0.44, -0.32], [0.53, -0.1], [0.6, -0.04]], 6), t => 0.13 - 0.085 * Math.pow(t, 0.8), { z: 4, fio: true, fim: true, talhas: 4 });
  C.espiral([0.38, -0.86], 0.12, { z: 5 }); garras(0.63, -0.02, 1, 5, 1);
  C.fita(cr([[0.22, -0.84], [0.32, -0.56], [0.25, -0.3], [0.33, -0.09], [0.4, -0.04]], 6), t => 0.1 - 0.065 * t, { z: 1, fim: true, shL: 0.55, shR: 0.4 });
  garras(0.43, -0.02, 1, 1.2, 0.5);
  C.fita(cr([[-0.5, -0.88], [-0.28, -0.62], [-0.38, -0.4], [-0.58, -0.22], [-0.47, -0.04]], 6), t => 0.16 - 0.11 * Math.pow(t, 0.7), { z: 4, fio: true, fim: true, talhas: 4 });
  C.espiral([-0.5, -0.88], 0.14, { z: 5, dir: -1 }); garras(-0.44, -0.02, 1, 5, 1);
  C.fita(cr([[-0.66, -0.82], [-0.5, -0.58], [-0.6, -0.36], [-0.78, -0.2], [-0.68, -0.04]], 6), t => 0.1 - 0.065 * t, { z: 1, fim: true, shL: 0.55, shR: 0.4 });
  garras(-0.66, -0.02, 1, 1.2, 0.5);
  // cauda: sobe, faz o arco, passa POR CIMA do lombo e POR BAIXO da coxa, termina em voluta
  const cauda = cr([[-0.6, -0.8], [-0.84, -0.92], [-0.99, -1.18], [-0.87, -1.45], [-0.6, -1.46], [-0.47, -1.25], [-0.42, -1.0], [-0.36, -0.78], [-0.42, -0.6], [-0.6, -0.5], [-0.78, -0.43]], 6);
  cauda.forEach((p, i, a) => { p.b = [LB.TAIL, Math.min(1, i / (a.length - 1) * 1.6)]; });
  const fc = C.fita(cauda, t => 0.075 - 0.045 * t, { z: 3, bone: p => p.b, ini: false, fim: false, fio: true });
  for (const [da, l, cu] of [[0.3, 0.24, -11], [-0.2, 0.3, 9], [0.9, 0.2, -13]]) C.gavinha(cauda[cauda.length - 1], Math.PI + da, l, cu, 0.03, { z: 3, boneT: () => [LB.TAIL, 1] });
  // rachas (escondidas; acendem ao falhar)
  C.racha([0.62, -1.56], 2.2, 0.32); C.racha([-0.1, -1.08], 1.3, 0.3); C.racha([0.42, -0.6], 1.7, 0.26);
  // base: faixa-serpente rúnica com o nome
  C.faixaRunica(-1.05, 0.95, 0.1, nome, { bone: () => [LB.FRAME, 1] });
  C.fagulhas([-0.8, -1.7, 1.1, -0.1], 70, LB.BODY);
  return C;
}

/* ------------------------------------ CORVO DE AGOURO ------------------------------------ */
// Corvo de broche/placa de Vendel: bico pesado, olho amendoado, asa em leque de lâminas, espiral no ombro, garras.
const CV_ = { ROOT: 0, BODY: 1, HEAD: 2, BEAK: 3, WING: 4, F0: 5, F1: 6, F2: 7, F3: 8, F4: 9, F5: 10, TAIL: 11, EYE: 12, FRAME: 13 };
const PAR_CORVO = [0, 0, 1, 2, 1, 4, 4, 4, 4, 4, 4, 1, 2, 0];
const PIV_CORVO = { body: [0.0, -0.55], neck: [0.16, -0.92], beak: [0.4, -1.0], wing: [0.04, -0.7], tail: [-0.46, -0.38], eye: [0.25, -1.06], frame: [0, 0.04], runa: [0.85, -1.0] };
function corvoOsso(x, y) {
  const wh = sm(-0.86, -0.98, y) * sm(0.04, 0.16, x);
  if (wh > 0.001) return [CV_.HEAD, wh];
  return [CV_.BODY, 1];
}
function corvo(seed, nome, ch) {
  const C = cinzel(seed, corvoOsso), R = C.R;
  const H = () => [CV_.HEAD, 1], Bk = () => [CV_.BEAK, 1];
  // corpo em gota, contorno duplo
  const corpo = [[0.14, -0.96], [-0.1, -0.86], [-0.4, -0.62], [-0.62, -0.4], [-0.6, -0.3], [-0.3, -0.24], [0.02, -0.26], [0.22, -0.46], [0.3, -0.7], [0.28, -0.88]];
  C.risco(cr(fechar(corpo), 5), { kind: 0, z: 2, g: 800, sh: 0.95, s0: 0, s1: 1 });
  C.risco(cr(fechar(escalar(corpo, 0.88, [-0.1, -0.55])), 5), { kind: 1, z: 2, g: 800, sh: 0.5 });
  C.ocluir(corpo, 2, 800);
  // peito em escamas (penas como lâminas curtas)
  for (let row = 0; row < 4; row++) for (let k = 0; k < 4; k++) { const cx = 0.22 - row * 0.02 - k * 0.07, cy = -0.74 + row * 0.1 + k * 0.03; if (!dentro(corpo, cx, cy + 0.02)) continue; const P = []; for (let a = 0; a <= Math.PI + 0.01; a += Math.PI / 6) P.push([cx + Math.cos(a) * 0.035, cy + Math.sin(a) * 0.03]); C.risco(P, { kind: 4, z: 2.5, sh: 0.55 }); }
  C.runa(ch, [0.06, -0.38], 0.14, -0.5, { z: 9 });
  // cabeça e bico pesado em gancho
  // perfil contínuo nuca -> crânio -> bico em gancho -> boca (um só sulco, como no broche), e o segundo sulco por dentro
  const perfil = [[0.02, -0.96], [0.1, -1.1], [0.24, -1.16], [0.36, -1.13], [0.48, -1.105], [0.6, -1.05], [0.655, -0.965], [0.585, -0.98], [0.38, -0.995], [0.36, -0.94], [0.32, -0.87]];
  const cabPoly = [...perfil, [0.2, -0.86], [0.06, -0.9]];
  C.risco(cr(perfil, 5), { kind: 0, z: 4, g: 801, bone: H, sh: 1.05 });
  C.risco(cr(escalar([[0.08, -0.98], [0.14, -1.08], [0.25, -1.125], [0.35, -1.1], [0.46, -1.08], [0.56, -1.04]], 1, [0, 0]), 4), { kind: 1, z: 4, g: 801, bone: H, sh: 0.55 });
  C.risco(cr([[0.36, -1.12], [0.37, -1.05], [0.38, -0.995]], 3), { kind: 4, z: 4, bone: H, sh: 0.7 });   // base do bico
  C.ocluir(cabPoly, 4, 801);
  const bicoI = [[0.38, -0.99], [0.56, -0.972], [0.5, -0.935], [0.36, -0.928]];
  C.risco(cr(fechar(bicoI), 3), { kind: 0, z: 4.8, bone: Bk, sh: 0.8 });
  // olho amendoado com cauda
  const oc = PIV_CORVO.eye;
  C.risco(cr([[0.18, -1.108], [0.26, -1.1], [0.35, -1.072]], 3), { kind: 4, z: 9, bone: H, sh: 1.0, dust: 0 });   // sobrancelha baixa: agouro
  C.risco(cr([[oc[0] - 0.06, oc[1] + 0.008], [oc[0], oc[1] - 0.012], [oc[0] + 0.055, oc[1] - 0.008], [oc[0], oc[1] + 0.01], [oc[0] - 0.06, oc[1] + 0.008]], 4), { kind: 3, z: 9, bone: () => [CV_.EYE, 1], sh: 1, dust: 0 });
  C.risco([[oc[0] - 0.055, oc[1] + 0.01], [oc[0] - 0.13, oc[1] + 0.035]], { kind: 3, z: 9, bone: H, sh: 0.6, dust: 0 });
  C.P.push(oc[0], oc[1] - 0.002, CV_.EYE, 1, 9, 1, 0, 0.5, 1);
  // crista de gavinhas na nuca
  for (let i = 0; i < 3; i++) C.gavinha([0.07 - i * 0.03, -1.02 + i * 0.05], 2.75 + 0.12 * i, 0.17 + 0.05 * i, (i % 2 ? 1 : -1) * 4, 0.022, { z: 3.5, bone: () => [CV_.HEAD, 0.5] });   // penas da nuca
  for (let i = 0; i < 4; i++) C.gavinha([0.34 - i * 0.025, -0.93 + i * 0.05], 1.7 + 0.1 * i, 0.15 + 0.03 * i, (i % 2 ? 1 : -1) * 3, 0.022, { z: 4.6, bone: () => [CV_.HEAD, 0.4] });   // barbela eriçada
  // asa: leque de seis lâminas (cada uma é um osso girando no ombro), escamas na raiz e espiral no ombro
  const piv = PIV_CORVO.wing;
  for (let k = 0; k < 6; k++) {
    const ang = 2.42 + 0.07 * k, len = 0.66 + 0.1 * Math.sin(Math.PI * k / 5), b0 = [piv[0] + Math.cos(ang) * 0.12, piv[1] + Math.sin(ang) * 0.12];
    const c = []; for (let i = 0; i <= 14; i++) { const t = i / 14; c.push([b0[0] + Math.cos(ang) * len * t, b0[1] + Math.sin(ang) * len * t + 0.03 * Math.sin(Math.PI * t)]); }
    const bone = [CV_.F0 + k, 1]; c.forEach(p => { p.b = bone; });
    C.fita(c, t => 0.048 * Math.pow(Math.sin(Math.PI * Math.min(1, 0.15 + t * 0.9)), 0.6) + 0.004, { z: 3 + k * 0.1, bone: p => p.b, fim: true, fio: k === 2 });
  }
  for (let row = 0; row < 2; row++) for (let k = 0; k < 4; k++) { const cx = 0.0 - row * 0.09 - k * 0.07, cy = -0.66 + row * 0.08 + k * 0.05, P = []; for (let a = 0; a <= Math.PI + 0.01; a += Math.PI / 6) { const p = [cx + Math.cos(a) * 0.045, cy + Math.sin(a) * 0.035]; p.b = [CV_.WING, 1]; P.push(p); } C.risco(P, { kind: 4, z: 3.9, sh: 0.6 }); }
  C.espiral(piv, 0.085, { z: 4, bone: () => [CV_.WING, 1] });
  // cauda em cunha de lâminas
  for (let k = 0; k < 4; k++) { const c = cr([[-0.54, -0.36], [-0.72, -0.2 + 0.03 * k], [-0.9 + 0.03 * k, -0.02 + 0.04 * k]], 5); c.forEach((p, i, a) => { p.b = [CV_.TAIL, i / (a.length - 1)]; }); C.fita(c, t => 0.04 * (1 - 0.3 * t) + 0.004, { z: 1 + k * 0.05, bone: p => p.b, fim: true }); }
  // pernas e garras agarrando a faixa
  for (const [dx, z] of [[0, 2.2], [-0.07, 1.5]]) {
    C.fita(cr([[0.04 + dx, -0.28], [0.07 + dx, -0.16], [0.065 + dx, -0.1], [0.08 + dx, -0.01]], 3), t => 0.026 - 0.01 * t, { z, fim: false });
    for (const [tx, ty] of [[0.17, 0.035], [0.13, 0.05], [-0.0, 0.04]]) C.risco(cr([[0.08 + dx, -0.01], [lerp(0.08 + dx, tx + dx, 0.6), ty - 0.03], [tx + dx, ty]], 3), { kind: 4, z: z + 0.1, sh: 0.9, dust: 0 });
  }
  C.racha([0.24, -1.12], 1.2, 0.2); C.racha([-0.15, -0.8], 1.0, 0.32); C.racha([0.2, -0.55], 2.0, 0.25);
  C.faixaRunica(-0.85, 0.75, 0.05, nome, { bone: () => [CV_.FRAME, 1], hw: 0.05 });
  C.fagulhas([-0.7, -1.2, 0.7, -0.1], 50, CV_.BODY);
  return C;
}

/* ------------------------------------ membros ------------------------------------ */
const FICHA = {
  geri: { tipo: 'lobo', cor: '#7FB8FF', runa: 'ᛏ', nome: 'ᚷᛖᚱᛁ', seed: 11 },
  freki: { tipo: 'lobo', cor: '#B58CFF', runa: 'ᚢ', nome: 'ᚠᚱᛖᚲᛁ', seed: 23 },
  huginn: { tipo: 'corvo', cor: '#5FFF9F', runa: 'ᚨ', nome: 'ᚺᚢᚷᛁᚾ', seed: 37 },
  muninn: { tipo: 'corvo', cor: '#00E5FF', runa: 'ᛚ', nome: 'ᛗᚢᚾᛁᚾ', seed: 51 },
};
const IDS = Object.keys(FICHA);
const ALVO = {
  descansa: { lum: 0.8, alert: 0, work: 0 },
  alerta: { lum: 1.0, alert: 1, work: 0 },
  trabalhando: { lum: 1.12, alert: 0.7, work: 1 },
  entregou: { lum: 1.2, alert: 0.6, work: 0 },
  falhou: { lum: 0.75, alert: 0, work: 0 },
};
const membros = {};
let NL = 0, NP = 0;
IDS.forEach((id, mi) => {
  const f = FICHA[id], C = f.tipo === 'lobo' ? lobo(f.seed, f.nome, f.runa) : corvo(f.seed, f.nome, f.runa);
  const { L, P } = C.fim();
  const SL = new Float32Array(L), SP = new Float32Array(P);
  const bb = [1e9, 1e9, -1e9, -1e9]; for (let i = 0; i < SL.length; i += 9) { bb[0] = Math.min(bb[0], SL[i]); bb[2] = Math.max(bb[2], SL[i]); bb[1] = Math.min(bb[1], SL[i + 1]); bb[3] = Math.max(bb[3], SL[i + 1]); }
  const m = {
    id, mi, tipo: f.tipo, runaCh: f.runa, col: hex(f.cor), SL, SP, nL: SL.length / 9, nP: SP.length / 9, oL: NL, oP: NP, bb,
    PAR: f.tipo === 'lobo' ? PAR_LOBO : PAR_CORVO, M: [], estado: 'descansa', base: 'descansa', shot: null, nivel: 0,
    lum: 0.6, alert: 0, work: 0, fly: 0, fx: 1, fase: mi * 1.7, phB: Math.random(),
    blinkT: -9, nextBlink: 1 + Math.random() * 3, headT: 0, headSm: 0, nextHead: 1, crack: 0, cold: 0, shrink: 0, joy: 0, ember: 0,
    runa: 0, runaRed: 0, wig: 0, br: 0, ax: 0, ay: 0, U: 0, face: 1, k: 1, Uk: 0, ox: 0, oy: 0,
  };
  NL += m.nL; NP += m.nP; membros[id] = m;
});

/* ------------------------------------ WebGL ------------------------------------ */
const cv = document.createElement('canvas');
cv.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;display:block;pointer-events:none;mix-blend-mode:screen';
HOST.appendChild(cv);
const gl = cv.getContext('webgl', { alpha: true, premultipliedAlpha: true, antialias: true });
if (!gl) { cv.remove(); throw new Error('matilha: sem webgl'); }

const VS = `
precision highp float;
attribute vec2 a_pos; attribute vec4 a_meta; attribute float a_size;   // meta: membro*16+tipo, sombra, s, rnd
uniform vec2 u_res; uniform float u_time; uniform float u_dpr; uniform float u_pt;
uniform vec3 u_col[4]; uniform vec4 u_st[4]; uniform vec4 u_st2[4]; uniform float u_u[4]; uniform float u_wig[4];
// st: brilho, trabalho, racha, uivo | st2: brasa (runa ardendo), olho, frio, alerta
varying vec3 v_col; varying float v_soft;
const vec3 IRON=vec3(0.30,0.33,0.39); const vec3 SILVER=vec3(0.80,0.84,0.90); const vec3 ICE=vec3(0.86,0.93,1.0);
const vec3 EMBER=vec3(1.0,0.45,0.10); const vec3 COLD=vec3(0.42,0.52,0.68);
void main(){
  float mf=floor(a_meta.x/16.0+0.001); float kind=a_meta.x-mf*16.0; int m=int(mf+0.5);
  vec3 C=u_col[m]; vec4 S=u_st[m]; vec4 S2=u_st2[m]; float U=u_u[m];
  float sh=a_meta.y, s=a_meta.z, rnd=a_meta.w;
  vec2 p=a_pos;
  if(u_pt<0.5 && a_size>0.0){ float ph=rnd*6.2831853+mf*1.7; p+=a_size*u_wig[m]*U*vec2(sin(u_time*1.3+ph+p.y*0.03), 0.6*cos(u_time*1.1+ph)); }
  float sparkT=fract(u_time*(0.12+0.1*rnd)+rnd*7.0);
  if(u_pt>0.5 && kind>9.5 && kind<10.5){ p.y-=sparkT*U*(0.35+0.5*S.y+0.6*S2.x); p.x+=sin(u_time*2.0+rnd*40.0)*U*0.03; }
  if(u_pt>0.5 && kind>7.5 && kind<8.5){ float ph=rnd*6.2831853; p+=vec2(sin(u_time*0.21+ph),cos(u_time*0.17+ph*1.3))*U*0.012; }
  gl_Position=vec4(p/u_res*2.0-1.0,0.0,1.0); gl_Position.y=-gl_Position.y;
  vec3 metal=mix(IRON,SILVER,smoothstep(0.15,1.05,sh)); metal=mix(metal,C,0.10);
  // trabalho: a luz corre pelas fitas (o entrelaço "anda")
  float pulse=exp(-pow((fract(s*1.5-u_time*0.55+mf*0.37)-0.5)*7.0,2.0));
  float I=0.0; vec3 col=metal; v_soft=0.0;
  float eye=S2.y;
  if(u_pt<0.5){
    if(kind<0.5) I=0.82; else if(kind<1.5) I=0.40; else if(kind<2.5){ I=0.30*(0.7+0.9*eye); col=mix(metal,C,0.75); }
    else if(kind<3.5){ I=1.25*eye; col=mix(C,ICE,0.35); }
    else if(kind<4.5) I=0.48; else if(kind<5.5) I=0.30;
    else if(kind<6.5){ I=S.z*2.6; col=mix(ICE,vec3(1.0,0.35,0.45),0.35); }
    else { I=0.85; col=mix(SILVER*0.85,C,0.45); }
    I*=(0.35+0.75*sh);
    if(kind<6.5||kind>6.5) I*=S.x;
    if(kind<2.5||(kind>4.5&&kind<5.5)){ I+=S.y*pulse*0.55*(0.4+sh); col=mix(col,mix(C,ICE,0.4),S.y*pulse*0.7); }
    if(kind>6.5){ col=mix(col,EMBER*1.15,S2.x); I+=S2.x*1.4; }
    if(kind>2.5&&kind<3.5){ col=mix(col,EMBER,S2.x*0.6); }
    gl_PointSize=1.0;
  } else {
    float sz=1.0;
    if(kind<8.5){ float tw=0.55+0.45*sin(u_time*(0.6+rnd*1.6)+rnd*25.0); I=0.22*tw*sh*S.x; col=SILVER; sz=0.8+1.0*a_size; }
    else if(kind<9.5){ I=0.55*eye; sz=U*0.12; v_soft=1.0; col=mix(mix(C,ICE,0.3),EMBER,S2.x*0.6); }
    else if(kind<10.5){ float vis=0.06*step(0.8,rnd)+S.y*step(0.35,rnd)+S2.x; I=vis*sin(3.14159*sparkT)*0.9; col=mix(EMBER,vec3(1.0,0.8,0.45),rnd); sz=1.2+1.4*a_size; }
    else { I=0.45*sh*S.x*(1.0+S.y*pulse); col=mix(SILVER,C,0.3); sz=1.6+0.8*a_size; }
    gl_PointSize=max(1.0,sz*u_dpr);
  }
  col=mix(col,COLD,S2.z*0.75); I*=1.0-0.35*S2.z;
  v_col=col*I;
}`;
const FS = `precision highp float; varying vec3 v_col; varying float v_soft; uniform float u_pt;
void main(){ float a=1.0;
  if(u_pt>0.5){ vec2 d=gl_PointCoord-0.5; float r=length(d)*2.0; a=v_soft>0.5?exp(-r*r*4.5)*(1.0-smoothstep(0.8,1.0,r)):1.0-smoothstep(0.3,1.0,r); if(a<0.003) discard; }
  vec3 c=v_col*a; gl_FragColor=vec4(c,clamp(max(c.r,max(c.g,c.b)),0.0,1.0)); }`;
const FX_VS = `precision highp float; attribute vec2 a_pos; attribute vec4 a_c; uniform vec2 u_res; uniform float u_dpr; varying vec3 v_col;
void main(){ gl_Position=vec4(a_pos/u_res*2.0-1.0,0.0,1.0); gl_Position.y=-gl_Position.y; gl_PointSize=max(1.0,a_c.w*u_dpr); v_col=a_c.rgb; }`;
const FX_FS = `precision highp float; varying vec3 v_col; uniform float u_pt;
void main(){ float a=1.0; if(u_pt>0.5){ vec2 d=gl_PointCoord-0.5; float r=length(d)*2.0; a=exp(-r*r*4.0)*(1.0-smoothstep(0.85,1.0,r)); if(a<0.003) discard; }
  vec3 c=v_col*a; gl_FragColor=vec4(c,clamp(max(c.r,max(c.g,c.b)),0.0,1.0)); }`;
function mk(vs, fs) {
  const c = (t, s) => { const sh = gl.createShader(t); gl.shaderSource(sh, s); gl.compileShader(sh); if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(sh)); return sh; };
  const p = gl.createProgram(); gl.attachShader(p, c(gl.VERTEX_SHADER, vs)); gl.attachShader(p, c(gl.FRAGMENT_SHADER, fs)); gl.linkProgram(p);
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
  p.u = {}; const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
  for (let i = 0; i < n; i++) { const nm = gl.getActiveUniform(p, i).name.replace(/\[0\]$/, ''); p.u[nm] = gl.getUniformLocation(p, nm); }
  const at = {}; p.a = n => (at[n] ??= gl.getAttribLocation(p, n));
  return p;
}
const P_M = mk(VS, FS), P_FX = mk(FX_VS, FX_FS);
function meta(nome) {
  const N = nome === 'L' ? NL : NP, out = new Float32Array(N * 5);
  for (const m of Object.values(membros)) {
    const S = nome === 'L' ? m.SL : m.SP, n = nome === 'L' ? m.nL : m.nP, off = nome === 'L' ? m.oL : m.oP;
    for (let i = 0; i < n; i++) { const o = i * 9, d = (off + i) * 5; out[d] = m.mi * 16 + S[o + 4]; out[d + 1] = S[o + 5]; out[d + 2] = S[o + 6]; out[d + 3] = S[o + 7]; out[d + 4] = S[o + 8]; }
  }
  const b = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.bufferData(gl.ARRAY_BUFFER, out, gl.STATIC_DRAW); return b;
}
const B_ML = meta('L'), B_MP = meta('P');
const POS_L = new Float32Array(NL * 2), POS_P = new Float32Array(NP * 2);
const B_PL = gl.createBuffer(), B_PP = gl.createBuffer(), B_FXL = gl.createBuffer(), B_FXP = gl.createBuffer();
gl.bindBuffer(gl.ARRAY_BUFFER, B_PL); gl.bufferData(gl.ARRAY_BUFFER, POS_L.byteLength, gl.DYNAMIC_DRAW);
gl.bindBuffer(gl.ARRAY_BUFFER, B_PP); gl.bufferData(gl.ARRAY_BUFFER, POS_P.byteLength, gl.DYNAMIC_DRAW);

/* ------------------------------------ matrizes 2D ------------------------------------ */
const I6 = [1, 0, 0, 1, 0, 0];
const mul = (A, B) => [A[0] * B[0] + A[2] * B[1], A[1] * B[0] + A[3] * B[1], A[0] * B[2] + A[2] * B[3], A[1] * B[2] + A[3] * B[3], A[0] * B[4] + A[2] * B[5] + A[4], A[1] * B[4] + A[3] * B[5] + A[5]];
const rot = (a, p) => { const c = Math.cos(a), s = Math.sin(a); return [c, s, -s, c, p[0] - c * p[0] + s * p[1], p[1] - s * p[0] - c * p[1]]; };
const esc = (sx, sy, p) => [sx, 0, 0, sy, p[0] - sx * p[0], p[1] - sy * p[1]];
const tra = (x, y) => [1, 0, 0, 1, x, y];

/* ------------------------------------ layout ------------------------------------ */
// A matilha é a MOLDURA da cena (never crowding Odin): lobos nos cantos de baixo, corvos nos
// cantos de cima, sempre fora da orb com folga de 15% do diâmetro dela. O Odin fica sozinho no centro.
let W = 0, Hh = 0, DPR = 1;
function medir() {
  const w = HOST.clientWidth, h = HOST.clientHeight, d = Math.min(window.devicePixelRatio || 1, 2);
  if (w !== W || h !== Hh || d !== DPR) { W = w; Hh = h; DPR = d; cv.width = Math.round(W * DPR); cv.height = Math.round(Hh * DPR); gl.viewport(0, 0, cv.width, cv.height); }
  if (LAYOUT === 'odin') posOdin(); else posFrota();
}
function encaixa(m, x0, y0, x1, y1, face, alinhaY) {   // maior U que cabe na caixa; ancora pelo bbox local
  const bw = m.bb[2] - m.bb[0], bh = m.bb[3] - m.bb[1];
  const U = Math.min((x1 - x0) / bw, (y1 - y0) / bh);
  const w = bw * U, h = bh * U, bx0 = face > 0 ? x0 : x1 - w, by0 = alinhaY > 0 ? y1 - h : y0;
  Object.assign(m, { U, face, ax: face > 0 ? bx0 - m.bb[0] * U : bx0 + w + m.bb[0] * U, ay: by0 - m.bb[1] * U });
}
function posOdin() {
  const ORBR = Math.min(W, Hh) * 0.43, CX = W / 2, CY = Hh * 0.5;
  const rOrb = ORBR * 0.95, Rmin = rOrb * 1.33;                // borda da orb + 15% do diâmetro
  const mg = Math.max(10, Math.min(W, Hh) * 0.025);
  for (const [id, lado, baixo] of [['geri', -1, 1], ['freki', 1, 1], ['huginn', -1, -1], ['muninn', 1, -1]]) {
    const m = membros[id], umax = (m.tipo === 'lobo' ? 0.2 : 0.15) * Hh;
    let lo = 4, hi = umax;
    for (let it = 0; it < 22; it++) {
      const U = (lo + hi) / 2, w = (m.bb[2] - m.bb[0]) * U, h = (m.bb[3] - m.bb[1]) * U;
      const x0 = lado < 0 ? mg : W - mg - w, y0 = baixo > 0 ? Hh - mg - h : mg;
      const nx = cl(CX, x0, x0 + w), ny = cl(CY, y0, y0 + h);
      if (Math.hypot(nx - CX, ny - CY) >= Rmin && w < W * 0.45) lo = U; else hi = U;
    }
    const w = (m.bb[2] - m.bb[0]) * lo, h = (m.bb[3] - m.bb[1]) * lo, x0 = lado < 0 ? mg : W - mg - w, y0 = baixo > 0 ? Hh - mg - h : mg;
    encaixa(m, x0, y0, x0 + w, y0 + h, -lado, 1);
    m.olhar = [CX, CY - 0.2 * ORBR];
  }
}
function posFrota() {
  const hr = HOST.getBoundingClientRect(), od = OPTS.odin?.getBoundingClientRect();
  const ox = od ? od.left + od.width / 2 - hr.left : W / 2, oy = od ? od.top + od.height * 0.4 - hr.top : Hh * 0.4;
  for (const id of IDS) {
    const el = OPTS.paineis?.[id], m = membros[id]; if (!el) { m.U = 0; continue; }
    const r = el.getBoundingClientRect(), x = r.left - hr.left, y = r.top - hr.top, w = r.width, h = r.height, cx = x + w / 2;
    const face = ox >= cx ? 1 : -1, bw = m.bb[2] - m.bb[0], bh = m.bb[3] - m.bb[1];
    const U = Math.min((w - 20) / bw, (h - 66) / bh), ww = bw * U, hh = bh * U, x0 = cx - ww / 2, y1 = y + h - 50;
    encaixa(m, x0, y1 - hh, x0 + ww, y1, face, 1);
    m.olhar = [ox, oy];
  }
}

/* ------------------------------------ estado ------------------------------------ */
let T = 0;
function set(id, o = {}) {
  const alvo = id === '*' || id === 'todos' ? IDS : [id];
  const e = o.estado;
  for (const k of alvo) {
    const m = membros[k]; if (!m) continue;
    if (o.nivel !== undefined) m.nivel = cl(+o.nivel || 0, 0, 1);
    if (!e || !ALVO[e]) continue;
    if (e === 'entregou' || e === 'falhou') { m.shot = { tipo: e, t0: T }; m.estado = e; }
    else if (e === 'trabalhando') { m.shot = null; m.estado = e; }
    else { m.base = e; if (!m.shot) m.estado = e; }
  }
  acordar();
  return true;
}
const DUR = { entregou: 2.8, falhou: 2.8 };

function piscar(m) {
  if (T > m.nextBlink) { m.blinkT = T; m.nextBlink = T + 2.4 + 4 * Math.random(); }
  const bt = (T - m.blinkT) / 0.16; return bt >= 0 && bt < 1 ? Math.sin(Math.PI * bt) : 0;
}
function poseLobo(m, dt) {
  const P = PIV_LOBO, t = T + m.fase;
  m.phB += dt * lerp(0.18, 0.3, m.alert + m.work * 0.5);
  const breath = Math.sin(TAU * m.phB), blink = piscar(m);
  const snarl = m.work * (0.5 + 0.5 * Math.sin(t * 3.1)) ;
  const head = -0.07 * m.alert + 0.05 * m.work - 0.5 * m.joy + 0.2 * m.shrink + 0.02 * Math.sin(t * 0.37) + 0.012 * Math.sin(t * 0.91 + 1) + 0.008 * breath;
  const jaw = 0.015 + 0.012 * (1 + breath) + 0.05 * m.alert + m.work * (0.17 + 0.05 * snarl) + 0.33 * m.joy - 0.02 * m.shrink;
  const mane = -0.04 + 0.03 * Math.sin(t * 0.6) - 0.12 * m.alert - 0.16 * m.work - 0.1 * m.joy + 0.18 * m.shrink;
  const M = m.M;
  M[0] = I6;
  M[1] = mul(tra(0, 0.03 * m.shrink), esc(1, 1 + 0.012 * breath, P.chest));
  M[2] = mul(M[1], rot(head, P.neck));
  M[3] = mul(M[2], rot(jaw, P.jaw));
  M[4] = mul(M[1], rot(mane, P.mane));
  M[5] = rot(0.03 * Math.sin(TAU * 0.13 * t) + 0.06 * m.work * Math.sin(TAU * 0.9 * t) - 0.05 * m.alert + 0.1 * m.shrink, P.tail);
  M[6] = mul(M[2], esc(1, 1 - 0.9 * blink, P.eye));
  M[7] = esc(1 + 0.006 * breath - 0.02 * m.work, 1 + 0.01 * breath - 0.03 * m.work, P.frame);
  M[8] = mul(M[2], rot(0.1 * m.alert + 0.05 * m.work - 0.35 * m.shrink + 0.02 * Math.sin(t * 0.8), P.ear));
  m.wig = 0.004 + 0.006 * m.alert + 0.01 * m.work + 0.008 * m.joy;
  m.k = 1 - 0.05 * m.shrink;
}
function poseCorvo(m, dt) {
  const P = PIV_CORVO, t = T + m.fase;
  m.phB += dt * lerp(0.25, 0.4, m.alert);
  const breath = Math.sin(TAU * m.phB), blink = piscar(m);
  if (T > m.nextHead) { m.headT = (Math.random() - 0.5) * 0.3 + 0.12 * m.alert; m.nextHead = T + 1.0 + Math.random() * (3 - 1.5 * m.alert); }
  m.headSm = sm1(m.headSm, m.headT, 10, dt);
  const caw = m.joy > 0 ? Math.max(0, Math.sin(TAU * 2.2 * (T - (m.shot?.t0 ?? T)))) * m.joy : 0;
  // asa: trabalhando = meio leque batendo devagar (paira); entregou = leque inteiro aberto
  m.fly = sm1(m.fly, Math.max(m.work * 0.7, m.joy), 3, dt);
  const bate = m.work * 0.25 * Math.sin(TAU * 1.1 * t);
  const asa = m.fly * 1.45 + bate + 0.012 * Math.sin(t * 0.7) - 0.05 * m.shrink;
  const head = 0.12 + m.headSm + 0.015 * Math.sin(t * 1.1) - 0.35 * caw + 0.25 * m.shrink;
  const hover = m.work * (0.1 + 0.03 * Math.sin(TAU * 1.1 * t + 1.3));
  const M = m.M;
  M[0] = I6;
  M[1] = mul(tra(0, -hover + 0.03 * m.shrink), esc(1 + 0.01 * breath, 1 + 0.015 * breath - 0.05 * m.shrink, [0, 0]));
  M[2] = mul(M[1], rot(head, P.neck));
  M[3] = mul(M[2], rot(0.02 + 0.3 * caw + 0.12 * m.work, P.beak));
  M[4] = mul(M[1], rot(asa, P.wing));
  for (let k = 0; k < 6; k++) M[5 + k] = mul(M[4], rot(m.fly * (k - 2.5) * 0.24 + 0.006 * Math.sin(t * 0.9 + k), P.wing));
  M[11] = mul(M[1], rot(0.025 * Math.sin(t * 0.9) - 0.12 * m.fly + 0.08 * m.shrink, P.tail));
  M[12] = mul(M[2], esc(1, 1 - 0.9 * blink, P.eye));
  M[13] = esc(1 + 0.006 * breath - 0.02 * m.work, 1 + 0.01 * breath - 0.03 * m.work, P.frame);
  m.wig = 0.003 + 0.005 * m.alert + 0.008 * m.work + 0.008 * caw;
  m.k = 1 - 0.05 * m.shrink;
}
function atualiza(m, dt) {
  let joy = 0, ruim = 0, brasa = 0;
  m.runa = 0; m.runaRed = 0;
  if (m.shot) {
    const t = T - m.shot.t0;
    if (m.shot.tipo === 'entregou') { joy = t < 2.0 ? Math.sin(Math.PI * Math.min(1, t / 2.0)) : 0; brasa = sm(0, 0.35, t) * (1 - sm(1.9, 2.8, t)); m.runa = brasa; }
    else { ruim = sm(0, 0.25, t) * (1 - sm(2.0, 2.8, t)); m.runa = (1 - sm(0.2, 1.2, t)) * (t > 0.5 ? (Math.sin(T * 41) > 0 ? 1 : 0.4) : 1); m.runaRed = 1; }
    if (t > DUR[m.shot.tipo]) { m.shot = null; m.estado = m.base; }
  }
  m.joy = sm1(m.joy, joy, 8, dt); m.ember = brasa; m.crack = ruim; m.cold = sm1(m.cold, ruim, 4, dt); m.shrink = sm1(m.shrink, ruim, 5, dt);
  const A = ALVO[m.estado] || ALVO.descansa;
  m.lum = sm1(m.lum, A.lum * (1 + 0.3 * m.nivel) * (1 + 0.03 * Math.sin((T + m.fase) * 1.3)), 3, dt);
  m.alert = sm1(m.alert, A.alert, 4, dt);
  m.work = sm1(m.work, A.work, 2.5, dt);
  m.eye = (0.35 + 0.35 * m.alert + 0.9 * m.work + 0.8 * m.joy) * (1 - 0.7 * m.cold) * (0.85 + 0.15 * Math.sin((T + m.fase) * 0.8));
  if (m.tipo === 'lobo') poseLobo(m, dt); else poseCorvo(m, dt);
  m.Uk = m.U * m.k;
}
function pele(m, S, n, out, off) {
  const M = m.M, PAR = m.PAR, f = m.face;
  for (let i = 0; i < n; i++) {
    const o = i * 9, x = S[o], y = S[o + 1], b = S[o + 2] | 0, w = S[o + 3];
    const A = M[b], Pm = M[PAR[b]];
    const ax = A[0] * x + A[2] * y + A[4], ay = A[1] * x + A[3] * y + A[5];
    const px = Pm[0] * x + Pm[2] * y + Pm[4], py = Pm[1] * x + Pm[3] * y + Pm[5];
    const d = (off + i) * 2;
    out[d] = m.ax + f * (px + (ax - px) * w) * m.Uk;
    out[d + 1] = m.ay + (py + (ay - py) * w) * m.Uk;
  }
}

/* ------------------------------------ FX: a runa grande que arde / racha ------------------------------------ */
let FL = [], FP = [];
const EMBER = [1.0, 0.45, 0.1], GELO = [0.86, 0.93, 1.0], MAG = [1.0, 0.25, 0.4];
function fxLinha(a, b, c, I) { FL.push(a[0], a[1], c[0] * I, c[1] * I, c[2] * I, 1, b[0], b[1], c[0] * I, c[1] * I, c[2] * I, 1); }
function fxPonto(p, c, I, sz) { FP.push(p[0], p[1], c[0] * I, c[1] * I, c[2] * I, sz); }
function fx(m) {
  if (!m.U || m.runa < 0.01) return;
  const U = m.Uk, f = m.face, P = m.tipo === 'lobo' ? PIV_LOBO.runa : PIV_CORVO.runa;
  const ctr = [m.ax + f * P[0] * U, m.ay + P[1] * U], tam = 0.5 * U;
  const cor = m.runaRed ? MAG : EMBER.map((v, i) => lerp(v, lerp(m.col[i], GELO[i], 0.3), 0.25));
  const sobe = m.runaRed ? 0 : (1 - m.runa) * 0.12 * U;
  for (const st of FUTHARK[m.runaCh] || []) for (let s = 0; s < st.length - 1; s++) {
    const A = [ctr[0] + (st[s][0] - 0.5) * tam * 0.6, ctr[1] - sobe + (st[s][1] - 0.5) * tam], B = [ctr[0] + (st[s + 1][0] - 0.5) * tam * 0.6, ctr[1] - sobe + (st[s + 1][1] - 0.5) * tam];
    // contorno duplo (sulco) + brasa no meio
    const dx = B[0] - A[0], dy = B[1] - A[1], l = Math.hypot(dx, dy) || 1, nx = -dy / l * 2.2, ny = dx / l * 2.2;
    fxLinha([A[0] + nx, A[1] + ny], [B[0] + nx, B[1] + ny], cor, 0.75 * m.runa); fxLinha([A[0] - nx, A[1] - ny], [B[0] - nx, B[1] - ny], cor, 0.75 * m.runa);
    fxLinha(A, B, m.runaRed ? MAG : [1, 0.85, 0.6], 1.1 * m.runa);
    const n = Math.max(2, Math.ceil(l / 3));
    for (let j = 0; j <= n; j++) { const u = j / n; fxPonto([lerp(A[0], B[0], u), lerp(A[1], B[1], u)], cor, 0.06 * m.runa, U * 0.14); }
  }
  if (!m.runaRed) for (let k = 0; k < 14; k++) { const ph = (T * 0.4 + k * 0.137) % 1, a = k * 2.4; fxPonto([ctr[0] + Math.cos(a) * tam * (0.3 + 0.25 * ph), ctr[1] - sobe - ph * tam * 1.1 + Math.sin(a) * tam * 0.15], EMBER, 0.8 * m.runa * (1 - ph), 2.2); }
}

/* ------------------------------------ laço ------------------------------------ */
let raf = 0, vivo = true, ultimo = performance.now(), tmr = 0, frames = 0, ftAcc = 0;
function desenha() {
  gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
  gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE);
  const u = P_M.u;
  gl.useProgram(P_M);
  gl.uniform2f(u.u_res, W, Hh); gl.uniform1f(u.u_time, T); gl.uniform1f(u.u_dpr, DPR);
  const cols = [], sts = [], st2 = [], us = [], ws = [];
  for (const id of IDS) { const m = membros[id]; cols.push(...m.col); sts.push(m.lum, m.work, m.crack, m.joy); st2.push(m.ember, m.eye || 0, m.cold, m.alert); us.push(m.Uk || 0); ws.push(m.wig); }
  gl.uniform3fv(u.u_col, cols); gl.uniform4fv(u.u_st, sts); gl.uniform4fv(u.u_st2, st2); gl.uniform1fv(u.u_u, us); gl.uniform1fv(u.u_wig, ws);
  const liga = (b, loc, size, stride, off) => { if (loc < 0) return; gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc, size, gl.FLOAT, false, stride, off); };
  for (let i = 0; i < 4; i++) gl.disableVertexAttribArray(i);
  gl.bindBuffer(gl.ARRAY_BUFFER, B_PL); gl.bufferSubData(gl.ARRAY_BUFFER, 0, POS_L);
  liga(B_PL, P_M.a('a_pos'), 2, 8, 0); liga(B_ML, P_M.a('a_meta'), 4, 20, 0); liga(B_ML, P_M.a('a_size'), 1, 20, 16);
  gl.uniform1f(u.u_pt, 0); gl.drawArrays(gl.LINES, 0, NL);
  gl.bindBuffer(gl.ARRAY_BUFFER, B_PP); gl.bufferSubData(gl.ARRAY_BUFFER, 0, POS_P);
  liga(B_PP, P_M.a('a_pos'), 2, 8, 0); liga(B_MP, P_M.a('a_meta'), 4, 20, 0); liga(B_MP, P_M.a('a_size'), 1, 20, 16);
  gl.uniform1f(u.u_pt, 1); gl.drawArrays(gl.POINTS, 0, NP);
  gl.useProgram(P_FX);
  for (let i = 0; i < 4; i++) gl.disableVertexAttribArray(i);
  gl.uniform2f(P_FX.u.u_res, W, Hh); gl.uniform1f(P_FX.u.u_dpr, DPR);
  if (FL.length) { gl.bindBuffer(gl.ARRAY_BUFFER, B_FXL); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(FL), gl.STREAM_DRAW); liga(B_FXL, P_FX.a('a_pos'), 2, 24, 0); liga(B_FXL, P_FX.a('a_c'), 4, 24, 8); gl.uniform1f(P_FX.u.u_pt, 0); gl.drawArrays(gl.LINES, 0, FL.length / 6); }
  if (FP.length) { gl.bindBuffer(gl.ARRAY_BUFFER, B_FXP); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(FP), gl.STREAM_DRAW); liga(B_FXP, P_FX.a('a_pos'), 2, 24, 0); liga(B_FXP, P_FX.a('a_c'), 4, 24, 8); gl.uniform1f(P_FX.u.u_pt, 1); gl.drawArrays(gl.POINTS, 0, FP.length / 6); }
}
function quadro(now) {
  raf = 0; tmr = 0; if (!vivo || document.hidden) return;
  const dt = Math.min(0.05, (now - ultimo) / 1000); ultimo = now; T += dt;
  const t0 = performance.now();
  medir();
  FL = []; FP = [];
  let calmo = true;
  for (const id of IDS) {
    const m = membros[id]; if (!m.U) continue;
    atualiza(m, dt);
    if (m.estado !== 'descansa' || m.shot || m.work > 0.02 || m.fly > 0.01 || m.cold > 0.01) calmo = false;
    pele(m, m.SL, m.nL, POS_L, m.oL); pele(m, m.SP, m.nP, POS_P, m.oP);
    fx(m);
  }
  desenha();
  ftAcc += performance.now() - t0; if (++frames % 30 === 0) { window.__matilhaMs = ftAcc / 30; ftAcc = 0; }
  if (calmo) tmr = setTimeout(() => { tmr = 0; raf = requestAnimationFrame(quadro); }, 28);   // descansando: ~30 fps
  else raf = requestAnimationFrame(quadro);
}
function acordar() { if (!vivo || document.hidden) return; if (tmr) { clearTimeout(tmr); tmr = 0; } if (!raf) { ultimo = performance.now(); raf = requestAnimationFrame(quadro); } }
// só leitura, pra teste de layout: caixa de cada membro na tela (px) agora
window.__matilhaCaixas = () => Object.fromEntries(IDS.map(id => { const m = membros[id], b = [1e9, 1e9, -1e9, -1e9]; for (let i = m.oL * 2; i < (m.oL + m.nL) * 2; i += 2) { b[0] = Math.min(b[0], POS_L[i]); b[2] = Math.max(b[2], POS_L[i]); b[1] = Math.min(b[1], POS_L[i + 1]); b[3] = Math.max(b[3], POS_L[i + 1]); } return [id, b.map(Math.round)]; }));
const visivel = () => { if (!document.hidden) acordar(); };
document.addEventListener('visibilitychange', visivel);
acordar();

return {
  set,
  parar() { vivo = false; if (raf) cancelAnimationFrame(raf); if (tmr) clearTimeout(tmr); document.removeEventListener('visibilitychange', visivel); cv.remove(); },
};
}
