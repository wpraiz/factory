// Log local de latencia do ODIN.
// Puxa as ultimas conversas de cada agente (prod/teste/lab) pela API da ElevenLabs, guarda o JSON bruto em
// %LOCALAPPDATA%\Factory\odin-log\plataforma\<conversa>.json (uma vez; conversa done nao muda) e imprime,
// por conversa: modelo, turnos, TTFB do LLM (p50/p95), primeira frase (p50), maos chamadas, creditos.
// O log do app (o que o Factory mediu na hora: fala->voz, fala->texto, cada mao) fica em
// %LOCALAPPDATA%\Factory\odin-log\<data>.jsonl e e resumido no fim.
//   node tools/odin-log.mjs            -> ultimas 3 conversas de cada agente
//   node tools/odin-log.mjs --n 8      -> ultimas 8
import { readFile, writeFile, mkdir, readdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { repoRoot, localData } from './profile.mjs';

const ORB = join(repoRoot, 'src', 'Factory', 'Assets', 'orb');
const KEY = process.env.ELEVENLABS_API_KEY; if (!KEY) throw new Error('ELEVENLABS_API_KEY nao definida');
const H = { 'xi-api-key': KEY };
const N = process.argv.includes('--n') ? Number(process.argv[process.argv.indexOf('--n') + 1]) : 3;
const LOG = join(localData, 'odin-log');
const PLAT = join(LOG, 'plataforma');
await mkdir(PLAT, { recursive: true });

const perfis = { prod: 'agent.json', teste: 'agent-test.json', lab: 'agent-lab.json' };
const p = (arr, q) => { if (!arr.length) return null; const s = [...arr].sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.floor(q * s.length))]; };
const f = (x) => (x == null ? '-' : x.toFixed(2) + 's');

for (const [perfil, file] of Object.entries(perfis)) {
  let id; try { id = JSON.parse(await readFile(join(ORB, file), 'utf-8')).agent_id; } catch { continue; }
  if (!id || id.endsWith('REPLACE_ME')) continue;
  const primario = (await (await fetch(`https://api.elevenlabs.io/v1/convai/agents/${id}`, { headers: H })).json()).conversation_config?.agent?.prompt?.llm;
  const lst = await (await fetch(`https://api.elevenlabs.io/v1/convai/conversations?agent_id=${id}&page_size=${N}`, { headers: H })).json();
  for (const c of lst.conversations || []) {
    const cache = join(PLAT, `${c.conversation_id}.json`);
    let d;
    if (existsSync(cache) && c.status === 'done') d = JSON.parse(await readFile(cache, 'utf-8'));
    else { d = await (await fetch(`https://api.elevenlabs.io/v1/convai/conversations/${c.conversation_id}`, { headers: H })).json(); if (c.status === 'done') await writeFile(cache, JSON.stringify(d, null, 1), 'utf-8'); }
    const t = d.transcript || [];
    const ttfb = [], ttfs = []; const maos = {}; const falou = {};
    for (const m of t) {
      if (m.role !== 'agent') continue;
      const mt = m.conversation_turn_metrics?.metrics || {};
      if (mt.convai_llm_service_ttfb?.elapsed_time != null) ttfb.push(mt.convai_llm_service_ttfb.elapsed_time);
      if (mt.convai_llm_service_ttf_sentence?.elapsed_time != null) ttfs.push(mt.convai_llm_service_ttf_sentence.elapsed_time);
      for (const x of m.tool_calls || []) maos[x.tool_name] = (maos[x.tool_name] || 0) + 1;
      if (m.producing_llm) falou[m.producing_llm] = (falou[m.producing_llm] || 0) + 1;
    }
    // Quem FALOU cada turno (producing_llm), nao o que foi cobrado: o reserva calado pode responder uma fatia.
    const nFalou = Object.values(falou).reduce((a, b) => a + b, 0);
    const reserva = nFalou ? 1 - (falou[primario] || 0) / nFalou : 0;
    const quem = Object.entries(falou).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k}×${v}`).join(' ') || '?';
    const quando = new Date(c.start_time_unix_secs * 1000).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
    console.log(`${perfil.padEnd(5)} ${quando} ${String(c.status).padEnd(11)} ${String(c.call_duration_secs + 's').padStart(6)} | falou ${quem}${reserva > 0 ? ` ⚠ reserva ${Math.round(reserva * 100)}% (primario hoje: ${primario})` : ''} | turnos ${ttfb.length} | ttfb p50 ${f(p(ttfb, 0.5))} p95 ${f(p(ttfb, 0.95))} | 1a frase p50 ${f(p(ttfs, 0.5))} | maos ${Object.entries(maos).map(([k, v]) => `${k}×${v}`).join(' ') || '-'} | ${d.metadata?.cost ?? '?'} cred`);
  }
}

// ---- log do app (medido no Factory) ----
try {
  const files = (await readdir(LOG)).filter((x) => x.endsWith('.jsonl')).sort().slice(-3);
  for (const fl of files) {
    const linhas = (await readFile(join(LOG, fl), 'utf-8')).split('\n').filter(Boolean).map((l) => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);
    const voz = linhas.filter((l) => l.kind === 'resposta-voz').map((l) => l.ms / 1000);
    const txt = linhas.filter((l) => l.kind === 'resposta-texto').map((l) => l.ms / 1000);
    const mao = linhas.filter((l) => l.kind === 'mao');
    console.log(`app ${fl}: fala->voz p50 ${f(p(voz, 0.5))} p95 ${f(p(voz, 0.95))} (${voz.length}) | fala->texto p50 ${f(p(txt, 0.5))} | maos ${mao.length}${mao.length ? ' (mais lenta: ' + mao.reduce((a, b) => (a.ms > b.ms ? a : b)).name + ' ' + (mao.reduce((a, b) => (a.ms > b.ms ? a : b)).ms / 1000).toFixed(1) + 's)' : ''}`);
  }
  if (!files.length) console.log('app: sem log ainda (aparece na primeira conversa)');
} catch { /* pasta nova */ }
console.log(`\nbrutos em ${PLAT}`);
