// Arquiva as conversas das 3 agentes do Odin (prod/teste/lab) da API da ElevenLabs em
// <FACTORY_DATA>\odin\conversas\<conv_id>.json (dado do usuario: fora do repo, onde o backup pega).
// So baixa conversa "done" que ainda nao esta no arquivo. Rodar antes de encurtar a retencao na plataforma.
// Uso: node tools\archive-conversations.mjs
import fs from 'node:fs';
import path from 'node:path';
import { repoRoot, factoryData } from './profile.mjs';

const KEY = process.env.ELEVENLABS_API_KEY;
if (!KEY) { console.error('falta ELEVENLABS_API_KEY'); process.exit(1); }
const ORB = path.join(repoRoot, 'src', 'Factory', 'Assets', 'orb');
const DEST = path.join(factoryData, 'odin', 'conversas');
fs.mkdirSync(DEST, { recursive: true });
const api = async p => { const r = await fetch('https://api.elevenlabs.io' + p, { headers: { 'xi-api-key': KEY } }); if (!r.ok) throw new Error(`${r.status} ${p}`); return r.json(); };

let novas = 0, ja = 0, vivas = 0;
for (const f of ['agent.json', 'agent-test.json', 'agent-lab.json']) {
  const { agent_id } = JSON.parse(fs.readFileSync(path.join(ORB, f), 'utf8'));
  if (!agent_id || agent_id.endsWith('REPLACE_ME')) continue;
  let cursor = '';
  do {
    const pg = await api(`/v1/convai/conversations?agent_id=${agent_id}&page_size=100${cursor ? '&cursor=' + cursor : ''}`);
    for (const c of pg.conversations) {
      const alvo = path.join(DEST, `${c.conversation_id}.json`);
      if (c.status !== 'done') { vivas++; continue; }
      if (fs.existsSync(alvo)) { ja++; continue; }
      const full = await api(`/v1/convai/conversations/${c.conversation_id}`);
      fs.writeFileSync(alvo, JSON.stringify({ agente: f, ...full }));
      novas++;
    }
    cursor = pg.has_more ? pg.next_cursor : '';
  } while (cursor);
}
console.log(`arquivadas agora: ${novas} · ja estavam: ${ja} · nao encerradas (puladas): ${vivas} · pasta: ${DEST}`);
