// Banco de provas das maos do Odin: roda tools\evals\hand-cases.json como testes de
// tool-call da ElevenLabs (agent-testing) contra UMA agente e devolve o placar x/N.
// Uso: node tools\odin-evals.mjs [--teste (padrao) | --lab | --prod] [--so nome1,nome2]
// Grava: <FACTORY_DATA>\odin\evals\<data-hora>-<perfil>.json (placar + motivo de cada falha) e o mapa de testes
// criados em <FACTORY_DATA>\odin\evals\testes.json (recria o teste se o caso mudar). So le a agente; nao muda nada nela.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { repoRoot, factoryData } from './profile.mjs';
const API = 'https://api.elevenlabs.io';
const H = { 'xi-api-key': process.env.ELEVENLABS_API_KEY, 'Content-Type': 'application/json' };
const ORB = join(repoRoot, 'src', 'Factory', 'Assets', 'orb');
const DIR = join(factoryData, 'odin', 'evals');
const perfil = process.argv.includes('--prod') ? 'prod' : process.argv.includes('--lab') ? 'lab' : 'teste';
const arqAgente = { prod: 'agent.json', teste: 'agent-test.json', lab: 'agent-lab.json' }[perfil];
const so = (process.argv[process.argv.indexOf('--so') + 1] || '').split(',').filter(s => process.argv.includes('--so') && s);

const api = async (metodo, caminho, corpo) => {
  const r = await fetch(API + caminho, { method: metodo, headers: H, body: corpo ? JSON.stringify(corpo) : undefined });
  const t = await r.text();
  if (!r.ok) throw new Error(`${metodo} ${caminho} -> ${r.status}: ${t.slice(0, 400)}`);
  return t ? JSON.parse(t) : {};
};

await mkdir(DIR, { recursive: true });
const { casos } = JSON.parse(await readFile(join(repoRoot, 'tools', 'evals', 'hand-cases.json'), 'utf-8'));
const agentId = JSON.parse(await readFile(join(ORB, arqAgente), 'utf-8')).agent_id;

// nome da mao -> tool_id DESTA agente (o mesmo nome existe em varias agentes)
const agente = await api('GET', `/v1/convai/agents/${agentId}`);
const idPorNome = {};
for (const id of agente.conversation_config.agent.prompt.tool_ids || []) {
  const t = await api('GET', `/v1/convai/tools/${id}`);
  idPorNome[t.tool_config?.name] = id;
}

let mapa = {};
try { mapa = JSON.parse(await readFile(join(DIR, 'testes.json'), 'utf-8')); } catch {}
const evalDe = (e) => e.exact !== undefined ? { type: 'exact', expected_value: e.exact }
  : e.regex !== undefined ? { type: 'regex', pattern: e.regex } : e.llm !== undefined ? { type: 'llm', description: e.llm } : { type: 'anything' };

const rodar = casos.filter(c => !so.length || so.includes(c.nome));
const testIds = [];
for (const c of rodar) {
  const toolId = idPorNome[c.mao];
  if (!toolId) throw new Error(`a agente ${perfil} nao tem a mao '${c.mao}'`);
  const chave = `${perfil}:${c.nome}`;
  const hash = createHash('sha1').update(JSON.stringify({ c, toolId })).digest('hex').slice(0, 10);
  if (mapa[chave]?.hash !== hash) {
    const r = await api('POST', '/v1/convai/agent-testing/create', {
      name: `odin-eval ${perfil} ${c.nome}`, type: 'tool',
      // meio de conversa: sem isso o 1o turno vira a chamada de `estado` da abertura e nao mede a mao
      // igual a abertura real: o Factory pede `estado`, o Odin chama, o painel recebe
      chat_history: [
        { role: 'user', time_in_call_secs: 0, message: '[Factory] o painel ainda esta sem teu estado: chame a mao `estado` agora (caos, humor, recado), sem anunciar.' },
        { role: 'agent', time_in_call_secs: 1, message: '', tool_calls: [{ type: 'client', request_id: 'abertura', tool_name: 'estado', params_as_json: '{"caos":18,"humor":"sereno, atento","recado":"Painel atualizado."}', tool_has_been_called: true }],
          tool_results: [{ request_id: 'abertura', tool_name: 'estado', result_value: 'Tool called successfully.', is_error: false, tool_has_been_called: true }] },
        { role: 'user', time_in_call_secs: 6, message: 'E aí, Odin?' },
        { role: 'agent', time_in_call_secs: 8, message: 'E aí. O que tu quer atacar agora?' },
        { role: 'user', time_in_call_secs: 15, message: c.fala },
      ],
      tool_call_parameters: {
        referenced_tool: { id: toolId, type: 'client' },
        parameters: Object.entries(c.params || {}).map(([path, e]) => ({ path, eval: evalDe(e) })),
        verify_absence: !!c.ausente,   // caso 'ausente': a mao NAO pode ser chamada (ex.: ele cantando)
      },
      check_any_tool_matches: true,   // o Odin as vezes chama `estado`/`gesto` junto: vale se a mao certa estiver entre elas
    });
    mapa[chave] = { id: r.id, hash };
  }
  testIds.push({ nome: c.nome, id: mapa[chave].id });
}
await writeFile(join(DIR, 'testes.json'), JSON.stringify(mapa, null, 1), 'utf-8');

const inv = await api('POST', `/v1/convai/agents/${agentId}/run-tests`, { tests: testIds.map(t => ({ test_id: t.id })) });
let runs = inv.test_runs || [];
for (let i = 0; i < 90 && runs.some(r => r.status === 'pending'); i++) {
  await new Promise(r => setTimeout(r, 4000));
  runs = (await api('GET', `/v1/convai/test-invocations/${inv.id}`)).test_runs || runs;
}
const res = testIds.map(t => {
  const r = runs.find(x => x.test_id === t.id) || {};
  return { caso: t.nome, status: r.status || '?', motivo: r.condition_result?.rationale ? JSON.stringify(r.condition_result.rationale) : null,
    chamou: (r.agent_responses || []).flatMap(a => a.tool_calls || []).map(tc => `${tc.tool_name} ${tc.params_as_json ?? ''}`.slice(0, 160)) };
});
const ok = res.filter(r => r.status === 'passed').length;
const quando = new Date().toLocaleString('sv-SE').replace(' ', '-').replace(/:/g, '').slice(0, 15);
await writeFile(join(DIR, `${quando}-${perfil}.json`), JSON.stringify({ schema: 'odin-evals-placar/v1', perfil, agent_id: agentId, invocacao: inv.id, placar: `${ok}/${res.length}`, resultados: res }, null, 1), 'utf-8');
console.log(`placar ${perfil}: ${ok}/${res.length}`);
for (const r of res) console.log(`${r.status === 'passed' ? 'OK ' : 'XX '} ${r.caso.padEnd(22)} ${r.status}${r.status !== 'passed' && r.motivo ? ' · ' + String(r.motivo).slice(0, 140) : ''}${r.status !== 'passed' && r.chamou.length ? ' · chamou: ' + r.chamou.join(' | ') : ''}`);

// fecha a volta: o placar novo vai pro card RUMO do PLANO
await import('./direction-score.mjs');
await import('./pack-memory.mjs');
