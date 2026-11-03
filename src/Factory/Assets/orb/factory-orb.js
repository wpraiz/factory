// Orb do Factory (ODIN): renderizador generico (orb-gl.js / orb.js, inalterados) +
// sessao com a agente de voz (ElevenLabs Agents) + constelacao de falas + ponte com o C#.
//
// Contrato com o C# (ChatPage.xaml.cs):
//   JS -> C#  chrome.webview.postMessage(JSON)  { type: 'ready' | 'want-start' | 'status' | 'message' | 'tool' }
//   C# -> JS  factory.start({ signedUrl, catalogo, memoria, hoje })   abre a sessao (URL assinada vem do C#)
//             factory.stop() · factory.toggle() · factory.disable(motivo)
//             factory.linha(quem, texto, atualiza)      uma fala no ceu (VOCE/ODIN/CLAUDE/FACTORY)
//             factory.toolResult(id, texto)              resposta de uma ferramenta executada no C#
//
// Ferramentas (client tools): a plataforma chama, a gente manda pro C# e espera o texto de volta.
// Nada aqui gasta credito sozinho: a sessao so abre depois de um clique.

import { OrbGL } from './orb-gl.js';
import { Orb } from './orb.js';
import { criarAvatar } from './avatar.js';
import { criarMatilha } from './pack.js';
import { Conversation } from 'https://esm.sh/@elevenlabs/client@1.25.0';

const canvas = document.getElementById('orb');
const ceu = document.getElementById('ceu');
const bridge = window.chrome?.webview;

function post(msg) { try { bridge?.postMessage(JSON.stringify(msg)); } catch { /* fora do WebView2 */ } }

function criarRender(el) {
  const gl = OrbGL.create(el);
  if (gl) return gl;
  const novo = el.cloneNode(false);
  el.replaceWith(novo);
  return new Orb(novo);
}
const orb = criarRender(canvas);
window.addEventListener('resize', () => orb.resize());

// Odin's face: avatar.js desenha orb + rosto numa camada por cima; a orb antiga fica invisivel e
// continua recebendo o clique. Sem WebGL o avatar nao nasce e a orb antiga volta sozinha.
// Sintonia: enquanto nao ha calculo externo (resumo.json diz "ainda nao calculada"), o rosto aparece inteiro (1).
const MODO_AVATAR = { idle: 'idle', off: 'idle', connecting: 'pensando', listening: 'ouvindo', thinking: 'pensando', speaking: 'falando', error: 'fiscal_falha' };
let avatar = null;
try { avatar = criarAvatar(document.getElementById('avatar')); avatar.set({ sintonia: 1 }); canvas.style.opacity = '0'; }
catch (e) { avatar = null; canvas.style.opacity = ''; post({ type: 'metric', kind: 'avatar-erro', ms: 0, name: String(e?.message || e).slice(0, 300) }); }

// Pack: Geri/Freki aos pes, Huginn/Muninn nos ombros (fichas em pack\*.md). Cada mao tem um dono;
// enquanto a mao roda o dono fica `trabalhando`, no resultado `entregou`/`falhou` e volta a base. Base: Odin falando
// = todos `alerta`; parado = `descansa`. Sem WebGL a matilha nao nasce e nada mais muda.
const DONO = {
  desenhar: 'geri', janela: 'geri', fazer: 'geri', sessoes: 'geri', terminais: 'geri', digitar_terminal: 'geri', focar_terminal: 'geri', fechar_terminal: 'geri',
  musica: 'freki', abrir_app: 'freki', obs: 'freki', abrir_arquivo: 'freki', abrir_pasta: 'freki', abrir_projeto: 'freki',
  pesquisar: 'huginn', perguntar_claude: 'huginn', ver_tela: 'huginn',
  lembrar: 'muninn', seguranca: 'muninn', reportar_falha: 'muninn', criar_nota: 'muninn', gravar_status: 'muninn', alarme: 'muninn',
};
const MEMBROS = ['geri', 'freki', 'huginn', 'muninn'];
const emTrabalho = { geri: 0, freki: 0, huginn: 0, muninn: 0 };
let baseMatilha = 'descansa';
let matilha = null;
try { matilha = criarMatilha(document.getElementById('matilha'), { layout: 'odin' }); }
catch (e) { matilha = null; post({ type: 'metric', kind: 'matilha-erro', ms: 0, name: String(e?.message || e).slice(0, 300) }); }
function matilhaBase(m) {
  baseMatilha = m === 'speaking' ? 'alerta' : 'descansa';
  for (const id of MEMBROS) if (!emTrabalho[id]) matilha?.set(id, { estado: baseMatilha });
}
const FALHOU = /^\s*(n[aã]o consegui|n[aã]o entendi|erro|error|falh|o factory n[aã]o respondeu)/i;
function matilhaInicio(nome) {
  const id = DONO[nome]; if (!id) return null;
  emTrabalho[id]++; matilha?.set(id, { estado: 'trabalhando' });
  return id;
}
function matilhaFim(id, texto) {
  if (!id) return;
  emTrabalho[id] = Math.max(0, emTrabalho[id] - 1);
  if (emTrabalho[id]) return;   // one of its hands is still running: keep working
  matilha?.set(id, { estado: baseMatilha });
  matilha?.set(id, { estado: FALHOU.test(String(texto ?? '')) ? 'falhou' : 'entregou' });
}

let conv = null;
// Double click / double trigger disparava DOIS want-start antes do 1o Conversation.startSession
// resolver (a checagem 'if (conv) return' nao pega porque conv so e setado depois do await). Isso abria
// DUAS sessoes reais (duas vozes, duplo credito); a 1a virava orfa (JS so guarda a ultima em `conv`) e
// ninguem mais desligava ela pela UI. `starting` fecha essa janela.
let starting = false;
let pensandoTimer = null;
let modo = 'idle';          // off | idle | connecting | listening | speaking | error
let peakOut = 0.05, peakIn = 0.02;
let ultimoQuem = null;
// Controles do painel (ChatPage): volume da voz do Odin (0-1), intensidade do orb (0.2-2) e microfone mudo.
// Guardados aqui e reaplicados a cada sessao nova.
let volume = 1, intensidade = 1, micMudo = false;
// Latencia medida aqui (logs locais de latencia):
// tFala = when the user transcript arrived; a primeira resposta do Odin (texto e voz) vira {type:'metric'}.
let tFala = 0, aguardaTexto = false, aguardaVoz = false;
// Contexto ([Factory] tarefa terminou) que chegou sem sessao: entra na proxima conversa, na abertura.
const filaCtx = [];

function setModo(m, texto) {
  modo = m;
  document.body.dataset.modo = m;
  avatar?.set({ modo: MODO_AVATAR[m] ?? 'idle' });
  matilhaBase(m);
  post({ type: 'status', status: m, text: texto ?? '' });
}

function disable(motivo) {
  setModo('off', motivo || 'voz desligada');
  orb.update(0.016, null); orb.draw();
}

// ---------- feeling -> rosto (o rosto segue o humor do Odin e o que esta acontecendo) ----------
// A mao `estado` ja traz caos 0-100 e humor em palavras: vira caos/valencia/ativacao do avatar. Eventos das maos
// (tarefa, falha) mexem por alguns segundos e decaem sozinhos no avatar.js.
const HUMOR = [
  [/(feliz|content|animad|alegr|satisfeit|confiant|orgulh|empolgad|divert|bem|otimist|grato|leve)/, 0.6, 0.55],
  [/(seren|calm|tranquil|pront|em paz|focad|atent)/, 0.25, 0.3],
  [/(curios|intrigad|pensativ|refle)/, 0.15, 0.45],
  [/(inquiet|ansios|agitad|impacient|elétric|eletric)/, -0.1, 0.8],
  [/(cansad|exaust|sonolent|lent)/, -0.2, 0.1],
  [/(preocupad|tens|apreensiv|desconfiad)/, -0.35, 0.6],
  [/(irritad|frustrad|brav|furios|raiva|puto|revoltad)/, -0.75, 0.9],
  [/(trist|melancol|desanim|abatid)/, -0.6, 0.15],
];
function sentir(nome, p) {
  if (!avatar) return;
  if (nome === 'estado') {
    const caos = Math.max(0, Math.min(100, +p.caos || 0)) / 100;
    const h = String(p.humor || '').toLowerCase();
    const hit = HUMOR.find(([re]) => re.test(h));
    avatar.set({ caos, valencia: hit ? hit[1] : 0.1, ativacao: hit ? Math.max(hit[2], caos * 0.8) : 0.3 + caos * 0.4 });
  }
  else if (nome === 'fazer' || nome === 'pesquisar' || nome === 'ver_tela') avatar.set({ evento: 'tarefa' });
  else if (nome === 'reportar_falha') avatar.set({ evento: 'falha' });
}

// ---------- ferramentas: pedido vai pro C#, resposta volta por toolResult ----------
let seq = 0;
const pendentes = new Map();
function ferramenta(nome) {
  return (params) => new Promise((resolve) => {
    const id = ++seq;
    pendentes.set(id, { resolve, dono: matilhaInicio(nome) });
    avatar?.set({ modo: 'mao', mao: nome });   // runa da mao em volta do rosto; o proximo status devolve o modo
    sentir(nome, params || {});
    // conv + t: o recibo hand-result/v1 amarra a execucao ao turno da conversa (o SDK nao entrega o
    // tool_call_id pro client tool; o fiscal acha o turno pela API com conv + nome + horario)
    post({ type: 'tool', id, name: nome, params: params || {}, conv: conv?.getId?.() ?? null, t: Date.now() });
    setTimeout(() => { const p = pendentes.get(id); if (p) { pendentes.delete(id); const t = 'O Factory nao respondeu a tempo. Diga que a mao falhou.'; matilhaFim(p.dono, t); resolve(t); } }, 60000);
  });
}
function toolResult(id, texto) {
  const p = pendentes.get(id);
  if (p) { pendentes.delete(id); matilhaFim(p.dono, texto); p.resolve(String(texto ?? '')); }
}
// C# (ou teste) aciona um membro direto: factory.matilha('geri'|'freki'|'huginn'|'muninn'|'*', estado)
function matilhaManual(id, estado) {
  if (!matilha) return 'sem matilha';
  if (estado === 'descansa' || estado === 'alerta') for (const k of (id === '*' ? MEMBROS : [id])) if (k in emTrabalho) emTrabalho[k] = 0;
  return matilha.set(id, { estado }) ? 'ok' : 'nao';
}
// TODA mao declarada em tools/create-orb-agent.mjs precisa estar aqui, senao a plataforma devolve
// "Client tool with name X is not defined on client" (it happened with `fazer`).
const NOMES = ['abrir_projeto', 'abrir_terminal', 'abrir_pasta', 'ler_status', 'perguntar_claude', 'criar_nota', 'gravar_status', 'lembrar',
  'criar_arquivo', 'abrir_arquivo', 'fazer', 'ver_tela',
  'terminais', 'focar_terminal', 'digitar_terminal', 'fechar_terminal', 'reportar_falha', 'musica', 'abrir_app', 'obs', 'janela', 'sessoes', 'pesquisar', 'estado', 'alarme', 'cancelar_alarme', 'acordado', 'desenhar', 'volume_odin', 'seguranca', 'roteiro'];
const clientTools = Object.fromEntries(NOMES.map((n) => [n, ferramenta(n)]));
// `desligar` e local: corta a sessao aqui mesmo, sem depender do LLM chamar end_call (falhou em 06:55).
clientTools.desligar = async () => { setTimeout(() => stop(), 400); return 'Desligando.'; };
// `gesto` tambem e local: mexe o proprio rosto, sem ida ao C# (a voice "shake your head" used to do nothing).
clientTools.gesto = async (p) => {
  if (!avatar) return 'Sem rosto agora (avatar nao carregou). Diga isso a ele.';
  const g = String(p?.gesto || '').toLowerCase(), cor = p?.cor_cabelo;
  if (g && !['sim', 'nao', 'balancar', 'inclinar'].includes(g)) return `Gesto "${g}" nao existe: so sim, nao, balancar, inclinar.`;
  if (cor !== undefined && avatar.set({ cor_cabelo: cor }) === false) return `Cor "${cor}" nao reconhecida: diga uma cor simples (vermelho, azul, roxo, dourado...) ou "padrao".`;
  if (g) avatar.set({ gesto: g });
  post({ type: 'metric', kind: 'gesto', ms: 0, name: [g, cor].filter(Boolean).join(' ') });
  return [g && `fiz o gesto ${g}`, cor !== undefined && `cabelo agora ${cor}`].filter(Boolean).join('; ') + '. Vale so ate fechar o app.';
};

// ---------- sessao ----------
async function start({ signedUrl, catalogo, memoria, hoje, ultima, saudacao, familia }) {
  starting = false;   // chegou aqui: a partir de agora modo='connecting' e quem bloqueia novo clique
  if (conv) return;
  setModo('connecting');
  try {
    conv = await Conversation.startSession({
      signedUrl,
      connectionType: 'websocket',
      dynamicVariables: { catalogo: catalogo || '(catalogo vazio)', memoria: memoria || '(sem memoria ainda)', hoje: hoje || '', ultima_conversa: ultima || '(nenhuma)', familia: familia || '(nada ainda)' },
      // Retomada: se a conversa anterior caiu, a primeira frase cita o que o usuario disse por ultimo.
      ...(saudacao ? { overrides: { agent: { firstMessage: saudacao } } } : {}),
      clientTools,
      onConnect: () => setModo('listening'),
      onDisconnect: () => { conv = null; ultimoQuem = null; setModo('idle', 'encerrada'); },
      onModeChange: ({ mode }) => {
        if (!conv) return;
        // Voltar a 'listening' logo depois da fala do usuario apagaria o 'thinking'; so troca se nao estiver pensando.
        if (mode === 'speaking') {
          if (aguardaVoz && tFala) { aguardaVoz = false; post({ type: 'metric', kind: 'resposta-voz', ms: Math.round(performance.now() - tFala) }); }
          setModo('speaking');
        }
        else if (modo !== 'thinking') setModo('listening');
      },
      onMessage: (m) => {
        const quem = (m.source ?? m.role) === 'user' ? 'pessoa' : 'agente';
        const atualiza = quem === 'agente' && ultimoQuem === 'agente';
        ultimoQuem = quem;
        post({ type: 'message', who: quem, text: m.message, update: atualiza });
        // Ele falou e o LLM ainda nao respondeu: 'thinking' (o feedback visual que substitui o filler falado).
        if (quem === 'pessoa') { const ruido = /^[\s.\u2026]*$/.test(String(m.message || '')); if (!ruido) { tFala = performance.now(); aguardaTexto = true; aguardaVoz = true; } else { tFala = 0; aguardaTexto = false; aguardaVoz = false; } setModo('thinking'); clearTimeout(pensandoTimer); pensandoTimer = setTimeout(() => { if (modo === 'thinking') setModo('listening'); }, 20000); }
        else {
          if (aguardaTexto && tFala) { aguardaTexto = false; post({ type: 'metric', kind: 'resposta-texto', ms: Math.round(performance.now() - tFala) }); }
          if (modo === 'thinking') setModo('speaking');
        }
      },
      onError: (msg) => setModo('error', `erro: ${String(msg).slice(0, 80)}`),
    });
    try { conv.setVolume({ volume }); if (micMudo) conv.setMicMuted?.(true); } catch { /* versao do SDK sem o metodo */ }
    while (filaCtx.length) contexto(filaCtx.shift());
  } catch (err) {
    conv = null;
    const n = err?.name || '';
    const txt = n === 'NotAllowedError' ? 'microfone bloqueado'
      : n === 'NotFoundError' ? 'nenhum microfone'
      : n === 'NotReadableError' ? 'microfone ocupado por outro programa'
      : `nao conectou: ${err?.message || err}`;
    setModo('error', txt);
    starting = false;
  }
}

// The C# 4-min cutoff only saw FINAL user sentences; a long monologue never closed a turn and the voice dropped mid-speech.
// Voz sustentada no mic (>= 6 de 8 amostras de 250 ms acima do limiar) avisa o C#, no maximo a cada 10 s.
// Ruido de fundo baixo nao passa do limiar, entao a conversa fantasma continua sendo cortada.
const vozJanela = [];
let vozAvisou = 0;
setInterval(() => {
  if (!conv || micMudo) { vozJanela.length = 0; return; }
  let v = 0; try { v = conv.getInputVolume?.() ?? 0; } catch { /* sessao caindo */ }
  vozJanela.push(v > 0.12); if (vozJanela.length > 8) vozJanela.shift();
  const agora = performance.now();
  if (vozJanela.filter(Boolean).length >= 6 && agora - vozAvisou > 10000) { vozAvisou = agora; post({ type: 'metric', kind: 'fala-bruta', ms: Math.round(v * 1000) }); }
}, 250);

async function stop() {
  if (!conv) return;
  const c = conv;
  conv = null;
  try { await c.endSession(); } catch { /* ja caiu */ }
  ultimoQuem = null;
  setModo('idle', 'encerrada');
}

function toggle() {
  if (modo === 'off') return;
  if (conv || modo === 'connecting') { stop(); return; }
  if (starting) return;   // ja pedimos pro C# iniciar; so falta a resposta (signed URL e' rede, tem atraso)
  starting = true;
  post({ type: 'want-start' });
}
canvas.addEventListener('click', toggle);

// ---------- constelacao de falas ----------
// Duas colunas que empilham (flex), no maximo 3 falas cada: nunca sobrepoe. "atualiza" (fala em
// streaming) olha a ultima fala DA COLUNA, nao do ceu inteiro — antes uma runa no meio fazia a mesma
// frase virar dois cartoes.
const colVoce = document.getElementById('colVoce');
const colOutros = document.getElementById('colOutros');
const runas = document.getElementById('runas');
const POR_COLUNA = 3;
function linha(quem, texto, atualiza = false) {
  const cls = { VOCE: 'voce', ODIN: 'odin', CLAUDE: 'claude', FACTORY: 'factory' }[quem] || 'factory';
  const col = cls === 'voce' ? colVoce : colOutros;
  const ultima = col.lastElementChild;
  if (atualiza && ultima && ultima.dataset.quem === quem) {
    ultima.querySelector('.txt').textContent = texto;
    return;
  }
  for (const f of ceu.querySelectorAll('.fala:not(.antiga)')) f.classList.add('antiga');
  while (col.children.length >= POR_COLUNA) col.firstElementChild.remove();

  const p = document.createElement('div');
  p.className = `fala ${cls}` + (texto.startsWith('⚙') ? ' tool' : '');
  p.dataset.quem = quem;
  const q = document.createElement('span'); q.className = 'quem'; q.textContent = quem;
  const t = document.createElement('span'); t.className = 'txt'; t.textContent = texto;
  p.append(q, t);
  col.appendChild(p);
  requestAnimationFrame(() => p.classList.add('on'));
}
function limpar() { colVoce.innerHTML = ''; colOutros.innerHTML = ''; }

// Resultado de uma tarefa em segundo plano. sendContextualUpdate e MUDO (o agente so usa no proximo
// turno do usuario); sendUserMessage faz o Odin responder na hora. O prefixo "[Factory]" diz que nao e o usuario.
function contexto(texto) {
  const t = String(texto);
  // resultado de tarefa mexe na cara do Odin: comprovado = sorri; falhou/nao confirmado = fecha a cara;
  // revisado por IA (sem prova) nao sorri: o rosto nao pode prometer mais que o recibo
  if (/✔ COMPROVADO/.test(t)) avatar?.set({ evento: 'ok' });
  else if (/✖ (FALHOU|NAO CONFIRMADO)/.test(t)) avatar?.set({ evento: 'falha' });
  if (!conv) { filaCtx.push(t); return; }   // sem sessao: fica na fila e entra na proxima conversa
  try {
    if (conv?.sendUserMessage) conv.sendUserMessage('[Factory] ' + t);
    else conv?.sendContextualUpdate?.(t);
  } catch { /* sessao ja caiu: fica so no ceu */ }
}

function setVolume(v) { volume = Math.max(0, Math.min(1, Number(v) || 0)); try { conv?.setVolume({ volume }); } catch { /* sem sessao */ } }
function setIntensidade(k) { intensidade = Math.max(0.2, Math.min(2, Number(k) || 1)); }
// Devolve o que aconteceu (o C# mostra no status): 'ok' | 'sem-sessao' (vale na proxima) | 'sem-metodo' (SDK sem setMicMuted).
function setMic(mudo) {
  micMudo = !!mudo;
  if (!conv) return 'sem-sessao';
  if (typeof conv.setMicMuted !== 'function') return 'sem-metodo';
  try { conv.setMicMuted(micMudo); return 'ok'; } catch (e) { return 'erro: ' + (e?.message || e); }
}
// ---------- efeitos randomicos: runa que surge e some no ceu, pulso do orb ----------
const RUNAS = ['ᚠ', 'ᚢ', 'ᚦ', 'ᚨ', 'ᚱ', 'ᚲ', 'ᚷ', 'ᚹ', 'ᚺ', 'ᚾ', 'ᛁ', 'ᛃ', 'ᛇ', 'ᛈ', 'ᛉ', 'ᛊ', 'ᛏ', 'ᛒ', 'ᛖ', 'ᛗ', 'ᛚ', 'ᛜ', 'ᛞ', 'ᛟ'];
function efeitoRandomico() {
  if (modo === 'off') return;
  const sorte = Math.random();
  if (sorte < 0.6) {
    const p = document.createElement('div');
    p.className = 'runa';
    p.style.top = `${10 + Math.random() * 75}%`; p.style.left = `${5 + Math.random() * 80}%`; p.style.fontSize = `${18 + Math.random() * 26}px`;
    p.textContent = RUNAS[Math.floor(Math.random() * RUNAS.length)];
    runas.appendChild(p);
    requestAnimationFrame(() => { p.style.transition = 'opacity 1.6s'; p.style.opacity = '0.35'; });
    setTimeout(() => { p.style.opacity = '0'; setTimeout(() => p.remove(), 1800); }, 3500 + Math.random() * 4000);
  } else {
    const antes = intensidade; intensidade = Math.min(2, antes * (1.3 + Math.random() * 0.5));
    setTimeout(() => { intensidade = antes; }, 1200 + Math.random() * 1500);
  }
  setTimeout(efeitoRandomico, 20000 + Math.random() * 50000);
}
setTimeout(efeitoRandomico, 15000);

// ---------- camera: deteccao de movimento LOCAL pro protocolo de acordar (nenhum frame sai daqui) ----------
let camStream = null, camTimer = null, camPrev = null;
async function setCamera(on) {
  if (!on) { clearInterval(camTimer); camTimer = null; camPrev = null; try { camStream?.getTracks().forEach((t) => t.stop()); } catch { /* ja parou */ } camStream = null; return 'camera off'; }
  if (camStream) return 'camera ja ligada';
  try {
    camStream = await navigator.mediaDevices.getUserMedia({ video: { width: 160, height: 120 }, audio: false });
    const v = document.createElement('video'); v.srcObject = camStream; v.muted = true; await v.play();
    const c = document.createElement('canvas'); c.width = 160; c.height = 120; const g = c.getContext('2d', { willReadFrequently: true });
    camTimer = setInterval(() => {
      try {
        g.drawImage(v, 0, 0, 160, 120);
        const d = g.getImageData(0, 0, 160, 120).data;
        const cur = new Uint8Array(160 * 120); for (let i = 0, j = 0; i < d.length; i += 4, j++) cur[j] = (d[i] + d[i + 1] + d[i + 2]) / 3;
        if (camPrev) { let diff = 0; for (let j = 0; j < cur.length; j++) if (Math.abs(cur[j] - camPrev[j]) > 25) diff++; const score = Math.round(1000 * diff / cur.length); if (score >= 15) post({ type: 'metric', kind: 'movimento', ms: score }); }
        camPrev = cur;
      } catch { /* frame ruim */ }
    }, 3000);
    return 'camera on';
  } catch (e) { return 'camera falhou: ' + (e?.name || e); }
}
window.factory = { start, stop, toggle, disable, linha, limpar, toolResult, contexto, camera: setCamera, volume: setVolume, intensidade: setIntensidade, mic: setMic, matilha: matilhaManual, get ativo() { return conv !== null; }, get pendentes() { return filaCtx.length; } };

// ---------- metricas pro orb ----------
function metrics() {
  if (!conv) return null;
  if (modo === 'thinking') {
    // Pulso lento e limpo, sem microfone: o orb "respira fundo" enquanto pensa.
    const t = performance.now() / 1000;
    const level = 0.35 + 0.25 * Math.sin(t * 2.2);
    return { level, bass: level * 0.6, mid: level * 0.4, treble: 0.1, rms: level };
  }
  const falando = modo === 'speaking';
  let data;
  try { data = falando ? conv.getOutputByteFrequencyData() : conv.getInputByteFrequencyData(); } catch { return null; }
  if (!data || !data.length) return null;
  const bins = data.length;
  const band = (from, to) => {
    let s = 0; const a = Math.floor(bins * from); const b = Math.max(a + 1, Math.floor(bins * to));
    for (let i = a; i < b; i++) s += data[i];
    return s / ((b - a) * 255);
  };
  const raw = band(0, 0.6);
  let level;
  if (falando) { peakOut = Math.max(raw, peakOut * 0.995); level = Math.min(1, Math.pow(raw / Math.max(0.05, peakOut), 0.8)); }
  else { peakIn = Math.max(raw, peakIn * 0.995); level = Math.min(1, Math.pow(raw / Math.max(0.03, peakIn), 0.8)) * 0.55; }
  // intensidade como curva (nao multiplicador cru): tudo fica em 0..1, senao o shader do orb estoura (190% seen once)
  const g = 1 / Math.max(0.2, intensidade);
  const c = (v) => Math.min(1, Math.pow(Math.max(0, Math.min(1, v)), g));
  return { level: c(level), bass: c(band(0, 0.08)), mid: c(band(0.08, 0.3)), treble: c(band(0.3, 0.7)), rms: c(raw) };
}

// Em sessao: 60 fps. Em repouso: respiro a ~12 fps. Desligada: parado.
let last = performance.now();
function frame(now) {
  const dt = Math.min(0.1, (now - last) / 1000);
  last = now;
  if (avatar) { const mt = metrics(); avatar.set({ nivel_audio: mt ? mt.level : 0 }); }   // o avatar tem laco proprio
  else if (modo !== 'off') { orb.update(dt, metrics()); orb.draw(); }
  if (conv || modo === 'connecting') requestAnimationFrame(frame);
  else setTimeout(() => requestAnimationFrame(frame), 80);
}
requestAnimationFrame(frame);
setModo('idle', 'respirando');
post({ type: 'ready' });
