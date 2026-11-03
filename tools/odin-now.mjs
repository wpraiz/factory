// Config VIVA das agentes do Odin, lida da API da ElevenLabs (fonte da verdade; os agente-*.json sao copia).
// Motivo: um .json local desatualizado ja fez o fiscal certificar um modelo que nao era o que estava rodando.
//   node tools/odin-now.mjs          -> prod, teste, lab: cerebro, reserva, cascade, maos que nao esperam resposta
// Sai com codigo 2 se algum .json local divergir da API.
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ORB = join(fileURLToPath(new URL('.', import.meta.url)), '..', 'src', 'Factory', 'Assets', 'orb');
const H = { 'xi-api-key': process.env.ELEVENLABS_API_KEY };
if (!H['xi-api-key']) throw new Error('ELEVENLABS_API_KEY nao definida');
let diverge = false;

for (const [perfil, file] of Object.entries({ prod: 'agent.json', teste: 'agent-test.json', lab: 'agent-lab.json' })) {
  let local; try { local = JSON.parse(await readFile(join(ORB, file), 'utf-8')); } catch { continue; }
  if (!local.agent_id || local.agent_id.endsWith('REPLACE_ME')) { console.log(`${perfil.padEnd(5)} (sem agente: rode node tools/create-orb-agent.mjs)`); continue; }
  const a = await (await fetch(`https://api.elevenlabs.io/v1/convai/agents/${local.agent_id}`, { headers: H })).json();
  const p = a.conversation_config?.agent?.prompt || {};
  const reserva = p.backup_llm_config?.preference === 'override' ? p.backup_llm_config.order.join(' > ') : p.backup_llm_config?.preference ?? '?';
  const mudas = (p.tools || []).filter((t) => t.expects_response === false).map((t) => t.name);
  console.log(`${perfil.padEnd(5)} ${a.name}`);
  console.log(`      cerebro ${p.llm} | reserva ${reserva} | cascade ${p.cascade_timeout_seconds ?? '?'} s | maos ${(p.tools || []).length} | sem resposta: ${mudas.join(', ') || '(nenhuma)'}`);
  if (local.llm !== p.llm) { diverge = true; console.log(`      ⚠ ${file} diz llm=${local.llm}, API diz ${p.llm}`); }
}
process.exit(diverge ? 2 : 0);
