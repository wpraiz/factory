// ODIN-web: copia do Odin pra abrir por LINK em outro computador (fora de casa).
// Sem maos de Windows (nao existem la), com o conteudo do perfil (familia.md, memoria) embutido como valores
// padrao das variaveis, e link publico. Cria/reaplica a partir da agente de TESTE.
//
//   node tools/create-web-agent.mjs            -> cria/atualiza e imprime o link
//   node tools/create-web-agent.mjs --fechar   -> desliga o acesso publico (deixe assim depois de usar)
//
// Privacidade: o perfil nao deve ter nomes de gente; mesmo assim, feche o link quando nao estiver usando.
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const ORB = join(ROOT, 'src', 'Factory', 'Assets', 'orb');
const KEY = process.env.ELEVENLABS_API_KEY;
if (!KEY) throw new Error('ELEVENLABS_API_KEY nao definida');
const FECHAR = process.argv.includes('--fechar');
const H = { 'xi-api-key': KEY, 'Content-Type': 'application/json' };
import { chatDir } from './profile.mjs';
const CHAT = chatDir();   // vida do usuario: perfil (fora do git)
const ler = async (p, fallback) => { try { return (await readFile(p, 'utf-8')).trim() || fallback; } catch { return fallback; } };

const teste = JSON.parse(await readFile(join(ORB, 'agent-test.json'), 'utf-8'));
let cfg = {};
try { cfg = JSON.parse(await readFile(join(ORB, 'agent-web.json'), 'utf-8')); } catch { /* primeira vez */ }

if (FECHAR) {
  if (!cfg.agent_id) throw new Error('agent-web.json sem agent_id');
  const r = await fetch(`https://api.elevenlabs.io/v1/convai/agents/${cfg.agent_id}`, { method: 'PATCH', headers: H,
    body: JSON.stringify({ platform_settings: { auth: { enable_auth: true } } }) });
  console.log(r.ok ? 'link publico FECHADO' : 'falhou: ' + (await r.text()).slice(0, 300));
  process.exit(r.ok ? 0 : 1);
}

// Config viva da agente de teste = base (persona, voz, turn, etc.). Tira as maos.
const base = await (await fetch(`https://api.elevenlabs.io/v1/convai/agents/${teste.agent_id}`, { headers: H })).json();
const cc = base.conversation_config;
const prompt = cc.agent.prompt.prompt
  + '\n\n# MODO PASSEIO (voce esta num link, fora do computador do usuario)\nAqui voce NAO tem mao nenhuma: nada de abrir, digitar, criar, pesquisar, lembrar, estado, terminal, fazer. NUNCA diga "comecei a procurar", "vou garantir", "registrei", "a equipe vai ver": nao existe equipe e nada acontece aqui fora. Se pedirem algo, uma frase: "aqui fora eu so falo; anota pra me pedir em casa". O que voce tem: o que ele escreveu sobre a familia ({{familia}}), a memoria ({{memoria}}) e a conversa. Se for uma crianca falando, vira o avo que conta saga.';

const body = {
  name: 'ODIN-web (link)',
  conversation_config: {
    ...cc,
    agent: {
      ...cc.agent,
      first_message: 'Tô aqui. Quem tá falando comigo?',
      dynamic_variables: { dynamic_variable_placeholders: {
        catalogo: '(fora de casa: sem catalogo)',
        memoria: await ler(join(CHAT, 'odin-memoria.md'), '(sem memoria)'),
        hoje: new Date().toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' }),
        ultima_conversa: '(nenhuma)',
        familia: (await ler(join(CHAT, 'familia.md'), '(nada ainda)')).slice(0, 8000),
      } },
      prompt: { ...cc.agent.prompt, prompt, tools: [], tool_ids: [] },
    },
  },
  platform_settings: { auth: { enable_auth: false } },   // link publico: qualquer um com o link fala (gasta minutos). Feche depois.
};

const url = cfg.agent_id ? `https://api.elevenlabs.io/v1/convai/agents/${cfg.agent_id}` : 'https://api.elevenlabs.io/v1/convai/agents/create';
const r = await fetch(url, { method: cfg.agent_id ? 'PATCH' : 'POST', headers: H, body: JSON.stringify(body) });
const txt = await r.text();
if (!r.ok) { console.error('falhou', r.status, txt.slice(0, 800)); process.exit(1); }
const id = JSON.parse(txt).agent_id || cfg.agent_id;
const link = `https://elevenlabs.io/app/talk-to?agent_id=${id}`;
await writeFile(join(ORB, 'agent-web.json'), JSON.stringify({ agent_id: id, nome: body.name, link, atualizado_em: new Date().toISOString().slice(0, 10) }, null, 2) + '\n');
console.log('agente web:', id);
console.log('LINK:', link);
