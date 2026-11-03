// ODIN web: a orb do Factory fora de casa. Conecta direto na agente publica ODIN-web (sem chave,
// sem servidor). Sem maos: aqui ele so fala. Persona, memoria e historia vao como valores padrao
// da agente (tools/create-web-agent.mjs). Mesmo renderizador do Factory (orb-gl.js / orb.js).
import { OrbGL } from './orb-gl.js';
import { Orb } from './orb.js';
import { Conversation } from 'https://esm.sh/@elevenlabs/client@1.25.0';

const AGENT_ID = 'agent_REPLACE_ME';   // ODIN-web (publico): o id que node tools/create-web-agent.mjs imprime. Fechar com: --fechar

const canvas = document.getElementById('orb');
const ceu = document.getElementById('ceu');
const botao = document.getElementById('botao');
const status = document.getElementById('status');

function criarRender(el) {
  const gl = OrbGL.create(el);
  if (gl) return gl;
  const novo = el.cloneNode(false);
  el.replaceWith(novo);
  return new Orb(novo);
}
const orb = criarRender(canvas);
window.addEventListener('resize', () => orb.resize());

let conv = null;
let modo = 'idle';
let peakOut = 0.05, peakIn = 0.02;
let ultimoQuem = null;
let pensandoTimer = null;
let desde = 0;

const TXT = { idle: 'Odin em repouso · clique no orb ou em VOZ', connecting: 'chamando Odin…', listening: 'Odin ouve', thinking: 'Odin pensa', speaking: 'Odin fala', error: 'erro' };
function setModo(m, texto) {
  modo = m;
  document.body.dataset.modo = m;
  status.textContent = texto || TXT[m] || m;
  botao.textContent = (m === 'idle' || m === 'error') ? 'VOZ' : 'CALAR';
}

// ---------- constelacao ----------
// colunas que empilham, max 3 por lado: nunca sobrepoe (mesma correcao do Factory)
const colVoce = document.getElementById('colVoce');
const colOutros = document.getElementById('colOutros');
function linha(quem, texto, atualiza = false) {
  const col = quem === 'VOCE' ? colVoce : colOutros;
  const ultima = col.lastElementChild;
  if (atualiza && ultima && ultima.dataset.quem === quem) { ultima.querySelector('.txt').textContent = texto; return; }
  for (const f of ceu.querySelectorAll('.fala:not(.antiga)')) f.classList.add('antiga');
  while (col.children.length >= 3) col.firstElementChild.remove();
  const p = document.createElement('div');
  p.className = `fala ${quem === 'VOCE' ? 'voce' : 'odin'}`;
  p.dataset.quem = quem;
  const q = document.createElement('span'); q.className = 'quem'; q.textContent = quem;
  const t = document.createElement('span'); t.className = 'txt'; t.textContent = texto;
  p.append(q, t);
  col.appendChild(p);
  requestAnimationFrame(() => p.classList.add('on'));
}

// ---------- sessao ----------
// Modo CASA: se a pagina esta atras do server.mjs (tunel pro Factory), usa a agente de teste com as
// maos de verdade rodando no PC do usuario. Senao (hospedagem estatica), cai na agente publica sem maos.
const NOMES = ['abrir_projeto', 'abrir_terminal', 'abrir_pasta', 'ler_status', 'perguntar_claude', 'criar_nota', 'gravar_status', 'lembrar',
  'criar_arquivo', 'abrir_arquivo', 'fazer', 'ver_tela', 'terminais', 'focar_terminal', 'digitar_terminal', 'fechar_terminal', 'reportar_falha', 'musica', 'abrir_app', 'pesquisar', 'estado', 'alarme', 'cancelar_alarme', 'acordado', 'desenhar'];
async function sessaoCasa() {
  try { const r = await fetch('/api/sessao', { cache: 'no-store' }); if (!r.ok) return null; return await r.json(); } catch { return null; }
}
function maoRemota(nome) {
  return async (params) => {
    try { const r = await fetch('/api/hands/' + nome, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(params || {}) }); const j = await r.json(); return String(j.result ?? j.error ?? 'sem resposta'); }
    catch (e) { return 'A mao falhou: ' + (e.message || e); }
  };
}

async function start() {
  if (conv) return;
  setModo('connecting');
  try {
    const casa = await sessaoCasa();
    const clientTools = { desligar: async () => { setTimeout(stop, 400); return 'Desligando.'; } };
    if (casa) for (const n of NOMES) clientTools[n] = maoRemota(n);
    const hoje = new Date().toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' });
    const dynamicVariables = casa
      ? { catalogo: casa.vars.catalogo || '(sem catalogo)', memoria: casa.vars.memoria || '(sem memoria)', ultima_conversa: casa.vars.ultima || '(nenhuma)', familia: casa.vars.familia || '(nada ainda)', hoje: casa.vars.hoje || hoje }
      : { hoje };
    document.getElementById('topo').innerHTML = casa ? 'FACTORY // <b>ODIN</b> · casa (maos ligadas)' : 'FACTORY // <b>ODIN</b> · fora de casa';
    conv = await Conversation.startSession({
      ...(casa ? { signedUrl: casa.signedUrl } : { agentId: AGENT_ID }),
      connectionType: 'websocket',
      dynamicVariables,
      clientTools,
      onConnect: () => { desde = performance.now(); setModo('listening'); },
      onDisconnect: () => { conv = null; ultimoQuem = null; setModo('idle', 'encerrada · clique pra chamar de novo'); },
      onModeChange: ({ mode }) => { if (!conv) return; if (mode === 'speaking') setModo('speaking'); else if (modo !== 'thinking') setModo('listening'); },
      onMessage: (m) => {
        const quem = (m.source ?? m.role) === 'user' ? 'VOCE' : 'ODIN';
        const atualiza = quem === 'ODIN' && ultimoQuem === 'ODIN';
        ultimoQuem = quem;
        linha(quem, m.message, atualiza);
        if (quem === 'VOCE') { setModo('thinking'); clearTimeout(pensandoTimer); pensandoTimer = setTimeout(() => { if (modo === 'thinking') setModo('listening'); }, 20000); }
        else if (modo === 'thinking') setModo('speaking');
      },
      onError: (msg) => setModo('error', `erro: ${String(msg).slice(0, 80)}`),
    });
  } catch (err) {
    conv = null;
    const n = err?.name || '';
    setModo('error', n === 'NotAllowedError' ? 'microfone bloqueado: libere no cadeado da barra e recarregue'
      : n === 'NotFoundError' ? 'nenhum microfone' : `nao conectou: ${err?.message || err}`);
  }
}
async function stop() {
  if (!conv) return;
  const c = conv; conv = null;
  try { await c.endSession(); } catch { /* ja caiu */ }
  ultimoQuem = null;
  setModo('idle', 'encerrada · clique pra chamar de novo');
}
function toggle() { if (conv || modo === 'connecting') stop(); else start(); }
canvas.addEventListener('click', toggle);
botao.addEventListener('click', toggle);
window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && conv) stop(); });

// ---------- metricas pro orb ----------
function metrics() {
  if (!conv) return null;
  if (modo === 'thinking') { const t = performance.now() / 1000; const level = 0.35 + 0.25 * Math.sin(t * 2.2); return { level, bass: level * 0.6, mid: level * 0.4, treble: 0.1, rms: level }; }
  const falando = modo === 'speaking';
  let data;
  try { data = falando ? conv.getOutputByteFrequencyData() : conv.getInputByteFrequencyData(); } catch { return null; }
  if (!data || !data.length) return null;
  const bins = data.length;
  const band = (a0, a1) => { let s = 0; const a = Math.floor(bins * a0); const b = Math.max(a + 1, Math.floor(bins * a1)); for (let i = a; i < b; i++) s += data[i]; return s / ((b - a) * 255); };
  const raw = band(0, 0.6);
  let level;
  if (falando) { peakOut = Math.max(raw, peakOut * 0.995); level = Math.min(1, Math.pow(raw / Math.max(0.05, peakOut), 0.8)); }
  else { peakIn = Math.max(raw, peakIn * 0.995); level = Math.min(1, Math.pow(raw / Math.max(0.03, peakIn), 0.8)) * 0.55; }
  return { level, bass: band(0, 0.08), mid: band(0.08, 0.3), treble: band(0.3, 0.7), rms: raw };
}
let last = performance.now();
function frame(now) {
  const dt = Math.min(0.1, (now - last) / 1000); last = now;
  orb.update(dt, metrics()); orb.draw();
  if (conv && modo !== 'idle') {
    const s = Math.floor((now - desde) / 1000);
    if (modo === 'listening' || modo === 'speaking' || modo === 'thinking') status.textContent = `${TXT[modo]} · ${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
    requestAnimationFrame(frame);
  } else setTimeout(() => requestAnimationFrame(frame), 80);
}
requestAnimationFrame(frame);
setModo('idle');
