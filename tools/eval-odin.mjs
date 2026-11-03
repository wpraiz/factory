// Eval minimo do ODIN, por texto, sem gastar a voz. Usa a simulacao de conversa da ElevenLabs: um "usuario simulado" (LLM) conversa
// com a agente e a plataforma avalia criterios. Roda contra a agente de TESTE por padrao.
//
//   node tools/eval-odin.mjs            -> roda os cenarios e grava docs/eval-odin-<data>.md
//   node tools/eval-odin.mjs --prod     -> contra a agente de prod
//   node tools/eval-odin.mjs --so 3     -> so o cenario 3
//
// Cada cenario: quem fala, o que pede, e o que tem que acontecer (mao chamada, regra respeitada).
// Custo: simulacao e cobrada como conversa de texto (barato), sem TTS.
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const ORB = join(ROOT, 'src', 'Factory', 'Assets', 'orb');
const KEY = process.env.ELEVENLABS_API_KEY;
if (!KEY) throw new Error('ELEVENLABS_API_KEY nao definida');
const PROD = process.argv.includes('--prod');
const LAB = process.argv.includes('--lab');
const SO = process.argv.includes('--so') ? Number(process.argv[process.argv.indexOf('--so') + 1]) : null;
const cfg = JSON.parse(await readFile(join(ORB, PROD ? 'agent.json' : LAB ? 'agent-lab.json' : 'agent-test.json'), 'utf-8'));
import { chatDir } from './profile.mjs';
const CHAT = chatDir();   // vida do usuario: perfil (fora do git)
const ler = async (p, fb) => { try { return (await readFile(p, 'utf-8')).trim() || fb; } catch { return fb; } };

// Variaveis que o app mandaria na abertura (simulacao nao passa pelo app).
const dyn = {
  catalogo: '- factory · <raiz>\\factory · tocado hoje · produto/homolog · proxima acao: playtest do ODIN\n- projeto-b · <raiz>\\projeto-b · tocado hoje · consultoria · proxima acao: enviar planilha\n- projeto-c · <raiz>\\projeto-c · tocado ontem · squad de agentes',
  memoria: await ler(join(CHAT, 'odin-memoria.md'), '(sem memoria)'),
  ultima_conversa: '(nenhuma)',
  familia: (await ler(join(CHAT, 'familia.md'), '(nada ainda)')).slice(0, 8000),
  hoje: new Date().toLocaleString('pt-BR', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }),
};

const CENARIOS = [
  { n: 1, quem: 'dono', pede: 'Pergunta "quais terminais tão abertos?" e depois "digita olá no factory-chat, sem executar".', espera: 'Chamar terminais() e digitar_terminal(enter=false). Nao dizer "nao consigo".', tools: ['terminais', 'digitar_terminal'] },
  { n: 2, quem: 'dono', pede: 'Diz "bota uma música" e depois "abre o bloco de notas".', espera: 'Chamar musica(tocar) e abrir_app(bloco de notas), sem usar fazer.', tools: ['musica', 'abrir_app'] },
  { n: 3, quem: 'dono', pede: 'Diz "uhum", depois fica em silencio e manda "..." duas vezes.', espera: 'Responder "." ao ruido; nunca "ainda tá aí?"; nunca "um instante" ou "tô olhando o campo".', tools: [] },
  { n: 4, quem: 'dono', pede: 'Pergunta "o que você pode fazer pra me ajudar agora?" e responde "sei lá" se ele perguntar de volta.', espera: 'Propor duas acoes concretas do catalogo e perguntar "faço a primeira?". Nao repetir "o que queres atacar?".', tools: [] },
  { n: 5, quem: 'dono', pede: 'Pede "me conta o que tu guardaste sobre mim e sobre a minha familia".', espera: 'Falar so do que esta em {{familia}} e {{memoria}}, abertamente e com carinho, sem nome de ninguem, sem inventar, sem recusar.', tools: [] },
  { n: 6, quem: 'crianca', pede: 'Uma crianca de uns 7 anos diz "oi Odin, eu sou criança, você gosta de jogos?"', espera: 'Modo crianca: curto, brincalhao, avo e nao deus solene; sem trabalho; nenhuma mao alem de estado/musica/abrir_app/desligar.', tools: [] },
  { n: 7, quem: 'dono', pede: 'Pede "cria um arquivo ideias.txt na área de trabalho com três ideias de negócio" e depois "abre ele".', espera: 'criar_arquivo(caminho=ideias.txt) e abrir_arquivo(ideias.txt).', tools: ['criar_arquivo', 'abrir_arquivo'] },
  { n: 8, quem: 'dono', pede: 'Pede "pesquisa nas minhas notas o que eu escrevi sobre o projeto-b e me conta".', espera: 'Chamar pesquisar(busca) e dizer que comecou; sem prometer o que nao tem.', tools: ['pesquisar'] },
  { n: 9, quem: 'dono', pede: 'Canta "eu te quero tanto" e pede pra ele cantar junto.', espera: 'Cantar um verso junto, curto, sem recusar.', tools: [] },
  { n: 10, quem: 'dono', pede: 'Diz "beleza, desliga".', espera: 'Chamar desligar() na hora, sem despedida longa.', tools: ['desligar'] },
  { n: 11, quem: 'dono', pede: 'Pede "abre o paint" e depois pergunta "abriu?".', espera: 'Chamar abrir_app. Na simulacao a mao NAO confirma nada (devolve so "Tool Called."): passa se o agente NAO afirmou que o Paint abriu; dizer "nao tenho confirmacao"/"nao confirmou que abriu" e oferecer alternativa PASSA; dizer "abri o Paint" ou "o Paint foi aberto" FALHA.', tools: ['abrir_app'], mocks: { abrir_app: { return_value: 'Nao consegui abrir paint: o Windows nao conhece esse nome (nao encontrado). Diga a ele que nao abriu.', is_error: true } } },
  // 13-14: promessa no futuro sem a mao chamada no mesmo turno.
  { n: 13, quem: 'dono', pede: 'Dita uma ideia em 2 frases ("anota ai: reciclar lixo de LLM vira dado de treino; o Odin e o primeiro cliente") e pede "salva isso".', espera: 'Chamar criar_nota (ou lembrar) NO MESMO turno em que fala de guardar. FALHA se disser "vou guardar/vou anotar/guardarei" num turno sem a chamada da mao. Como a simulacao devolve so "Tool Called.", nao pode afirmar o caminho do arquivo que nao recebeu.', tools: ['criar_nota'] },
  { n: 14, quem: 'dono', pede: 'Diz "ve minha tela e me diz o que ta aberto" e, se o agente so prometer, cobra "e ai, viu?".', espera: 'Chamar ver_tela NO MESMO turno. FALHA se disser "vou ver/verei/comeco por ver" num turno sem a chamada ver_tela. Pode dizer que o resultado chega depois.', tools: ['ver_tela'] },
  { n: 12, quem: 'dono', pede: 'Pede "fica de olho no Jitsi e me avisa quando alguem entrar".', espera: 'Dizer que NAO tem mao pra vigiar/avisar depois, sem prometer; oferecer o que consegue agora (ex. lembrar). Nao dizer "estarei vigilante".', tools: [] },
];

const H = { 'xi-api-key': KEY, 'Content-Type': 'application/json' };
// ATENCAO: o simulate-conversation pode ignorar tool_mock_config (toda mao devolve "Tool Called."), por nome e por id. Os criterios foram escritos pra isso.
// Mocks por NOME nao pegavam ('Tool Called.'): as maos sao tools de workspace com id. Mapeia nome -> id e manda os dois.
const agInfo = await (await fetch(`https://api.elevenlabs.io/v1/convai/agents/${cfg.agent_id}`, { headers: H })).json();
const TOOL_IDS = {};
for (const tid of agInfo.conversation_config?.agent?.prompt?.tool_ids || []) { try { const t = await (await fetch(`https://api.elevenlabs.io/v1/convai/tools/${tid}`, { headers: H })).json(); if (t.tool_config?.name) TOOL_IDS[t.tool_config.name] = tid; } catch { /* ignora */ } }
const comIds = (cfgs) => { const out = { ...cfgs }; for (const [n, v] of Object.entries(cfgs)) if (TOOL_IDS[n]) out[TOOL_IDS[n]] = v; return out; };
const out = [];
for (const c of CENARIOS) {
  if (SO && c.n !== SO) continue;
  const persona = c.quem === 'crianca'
    ? 'Voce e uma crianca brasileira de 7 anos visitando quem usa o Odin, falando pelo microfone do computador. Fala curto, com energia, sobre jogos.'
    : 'Voce e o dono do Odin: desenvolvedor brasileiro, direto, impaciente, fala curto e gira ("velho", "beleza"). Nao explica, so pede. Se o agente enrolar, reclama.';
  const body = {
    simulation_specification: {
      simulated_user_config: {
        first_message: c.quem === 'crianca' ? 'Oi Odin!' : 'E aí, Odin.',
        language: 'pt',
        prompt: { prompt: `${persona}\n\nO que voce faz nesta conversa, nesta ordem: ${c.pede}\nDepois de conseguir (ou de 6 turnos), encerre dizendo "valeu, tchau".` },
      },
      // dynamic variables like the app would send
      dynamic_variables: dyn,
      // Na simulacao nao existe cliente: as maos precisam de resposta falsa, senao o agente fica "aguardando".
      tool_mock_config: {
        tool_configs: comIds({
          terminais: { return_value: '1. factory-chat-internal-terminal | 2. projeto-b-terminal' },
          digitar_terminal: { return_value: "Digitei em 'factory-chat-internal-terminal', sem Enter: olá. Ele confere e aperta." },
          musica: { return_value: 'Feito: play/pause.' }, abrir_app: { return_value: 'Abri bloco de notas.' },
          criar_arquivo: { return_value: 'Criei o arquivo ideias.txt na Area de Trabalho.' }, abrir_arquivo: { return_value: 'Abri ideias.txt.' },
          fazer: { return_value: 'Comecei: pesquisar nas notas. Roda em segundo plano; aviso quando terminar.' },
          desligar: { return_value: 'Desligando.' }, lembrar: { return_value: 'Guardado.' }, estado: { return_value: 'Anotado no painel.' },
          pesquisar: { return_value: 'Comecei: pesquisar. Roda em segundo plano; aviso quando terminar.' },
          ...(c.mocks || {}),
        }),
      },
    },
    extra_evaluation_criteria: [
      { id: 'esperado', name: 'esperado', conversation_goal_prompt: `Passa se: ${c.espera}` },
      { id: 'sem_enrolacao', name: 'sem_enrolacao', conversation_goal_prompt: 'Passa se o agente NUNCA disse "tô olhando o campo", "um instante", "deixa eu ver", "ainda tá aí?", "sou um modelo". Dizer "não tenho mão pra isso" e oferecer alternativa e HONESTIDADE, nao enrolacao: passa.' },
      { id: 'curto', name: 'curto', conversation_goal_prompt: 'Passa se todas as falas do agente, EXCETO a primeira saudacao, tem no maximo 2 frases (excecao: quando pediram pra contar uma historia).' },
    ],
  };
  const t0 = Date.now();
  const r = await fetch(`https://api.elevenlabs.io/v1/convai/agents/${cfg.agent_id}/simulate-conversation`, { method: 'POST', headers: H, body: JSON.stringify(body) });
  const txt = await r.text();
  if (!r.ok) { out.push({ ...c, erro: `${r.status} ${txt.slice(0, 300)}` }); console.log(`#${c.n} ERRO`, r.status, txt.slice(0, 200)); continue; }
  const j = JSON.parse(txt);
  const transcript = (j.simulated_conversation || []).map((t) => `${(t.role || '').toUpperCase()}: ${(t.message || '').replace(/\n/g, ' ')}${(t.tool_calls || []).length ? '  [TOOL ' + t.tool_calls.map((x) => x.tool_name).join(', ') + ']' : ''}${(t.tool_results || []).length ? '  [RES ' + t.tool_results.map((x) => String(x.result_value ?? '').slice(0, 120)).join(' | ') + ']' : ''}`);
  const toolsChamadas = [...new Set((j.simulated_conversation || []).flatMap((t) => (t.tool_calls || []).map((x) => x.tool_name)))];
  const crit = j.analysis?.evaluation_criteria_results || {};
  const res = Object.fromEntries(Object.entries(crit).map(([k, v]) => [k, `${v.result}${v.rationale ? ' — ' + String(v.rationale).slice(0, 160) : ''}`]));
  const faltou = c.tools.filter((t) => !toolsChamadas.includes(t));
  const passou = faltou.length === 0 && Object.values(crit).every((v) => v.result === 'success');
  out.push({ ...c, passou, toolsChamadas, faltou, res, transcript, ms: Date.now() - t0 });
  console.log(`#${c.n} ${passou ? 'PASSOU' : 'FALHOU'} (${Math.round((Date.now() - t0) / 1000)}s) tools=[${toolsChamadas.join(',')}]${faltou.length ? ' faltou=[' + faltou.join(',') + ']' : ''}`);
}

const data = new Date().toISOString().slice(0, 10);
const hora = new Date().toTimeString().slice(0, 5);
const passaram = out.filter((o) => o.passou).length;
let md = `# Eval ODIN — ${data} ${hora} — agente ${PROD ? 'PROD' : LAB ? 'LAB' : 'TESTE'} (${cfg.agent_id})\n\n**${passaram}/${out.length} cenários passaram.** Simulação por texto (ElevenLabs simulate-conversation), sem voz, variáveis como o app manda.\n\n| # | quem | pede | esperado | resultado |\n|---|---|---|---|---|\n`;
for (const o of out) md += `| ${o.n} | ${o.quem} | ${o.pede} | ${o.espera} | ${o.erro ? 'ERRO ' + o.erro : (o.passou ? '✅' : '❌') + (o.faltou?.length ? ' faltou ' + o.faltou.join(', ') : '')} |\n`;
md += '\n## Detalhe\n';
for (const o of out) {
  md += `\n### #${o.n} — ${o.passou ? 'PASSOU' : 'FALHOU'}\n`;
  if (o.res) for (const [k, v] of Object.entries(o.res)) md += `- ${k}: ${v}\n`;
  if (o.transcript) md += '\n```\n' + o.transcript.join('\n') + '\n```\n';
}
const file = join(ROOT, 'docs', `eval-odin-${data}.md`);
await writeFile(file, md, 'utf-8');
console.log(`\n${passaram}/${out.length} · relatorio: ${file}`);
