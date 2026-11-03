// Cria (ou reaplica) a agente de voz ODIN do Factory na ElevenLabs a partir de
// src/Factory/Assets/orb/agent-prompt.md.
//
//   node tools/create-orb-agent.mjs
//
// Exige ELEVENLABS_API_KEY no ambiente. Grava o id em src/Factory/Assets/orb/agent.json;
// se o arquivo ja tiver agent_id, faz PATCH em vez de criar outra. Criar/atualizar a agente
// nao gasta credito; falar com ela gasta (minutos de conversa) — e so acontece por clique no app.
//
// As ferramentas sao "client tools": a plataforma pede, o JS (factory-orb.js) repassa pro C#
// (Services/OdinHands.cs), que executa e devolve o texto. A chave nunca sai do C#.

import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const ORB = join(ROOT, 'src', 'Factory', 'Assets', 'orb');
const KEY = process.env.ELEVENLABS_API_KEY;
if (!KEY) throw new Error('ELEVENLABS_API_KEY nao definida');

// prompt = produto (repo) + vida do usuario (perfil, fora do git). Sem perfil: Odin generico.
import { ler as lerPerfil, raiz as raizPerfil, factoryRoot, localData } from './profile.mjs';
const promptPessoal = lerPerfil('prompt', 'pessoal.md');
const prompt = (await readFile(join(ORB, 'agent-prompt.md'), 'utf-8')) + (promptPessoal ? '\n\n' + promptPessoal : '');
console.log(promptPessoal ? `prompt: repo + perfil (${raizPerfil})` : 'prompt: so o generico do repo (sem perfil)');
// Nomes dos projetos viram "keywords" do reconhecimento de fala: nomes de pasta nao existem no dicionario
// e viravam qualquer coisa na transcricao. Raiz dos projetos = FACTORY_ROOT (ver profile.mjs).
const { readdirSync } = await import('node:fs');
let projetos = [];
try { projetos = readdirSync(factoryRoot, { withFileTypes: true }).filter((d) => d.isDirectory() && !d.name.startsWith('_') && !d.name.startsWith('.')).map((d) => d.name); } catch { /* raiz ainda nao existe */ }
// Vocabulario generico (Assets\orb\vocabulary.txt) vem antes dos projetos: o ASR troca "Odin" por "Uni", "Paint" por "P-Paint".
// Nomes de gente/lugar vivem FORA do git (repo limpo por construcao): <perfil>\vocabulario.pessoal.txt
const lerVocab = async (f) => { try { return (await readFile(f, 'utf-8')).split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith('#')); } catch { return []; } };
const vocab = [...await lerVocab(join(ORB, 'vocabulary.txt')), ...await lerVocab(join(raizPerfil, 'vocabulario.pessoal.txt'))];
const keywords = [...new Set([...vocab, ...projetos])].slice(0, 100);
// Pipeline dev -> homolog -> prod: `--teste` mexe na agente ODIN-teste (agent-test.json, usada pelo
// exe --homolog). Sem a flag mexe na PROD (agent.json). Toda mudanca passa pela de teste antes.
const TESTE = process.argv.includes('--teste');
// '--lab' mexe numa TERCEIRA agente (agent-lab.json, ODIN-lab) pra experimentar modelo, sem tocar teste/prod.
const LAB = process.argv.includes('--lab');
// Parametros por perfil (tools/odin-config.json): 'padrao' + sobreposicao de prod/teste/lab. ODIN_LLM sobrepoe o llm.
const CONF = JSON.parse(await readFile(join(ROOT, 'tools', 'odin-config.json'), 'utf-8'));
const P = { ...CONF.padrao, ...(CONF[LAB ? 'lab' : TESTE ? 'teste' : 'prod'] || {}) };
// Escolha feita no PAINEL do Factory (%LOCALAPPDATA%\Factory\cerebro.json: agent_id -> llm) vence a config,
// senao reaplicar a agente desfazia o que o usuario escolheu na tela. ODIN_LLM continua vencendo tudo.
let PAINEL_LLM = null;
try {
  const cj = JSON.parse(await readFile(join(localData, 'cerebro.json'), 'utf-8'));
  const prev = JSON.parse(await readFile(join(ORB, LAB ? 'agent-lab.json' : TESTE ? 'agent-test.json' : 'agent.json'), 'utf-8'));
  if (prev.agent_id && cj[prev.agent_id]) PAINEL_LLM = cj[prev.agent_id];
} catch { /* sem escolha no painel */ }
if (PAINEL_LLM) console.log('llm escolhido no painel:', PAINEL_LLM, '(sobrepoe odin-config.json)');
const CFG_FILE = LAB ? 'agent-lab.json' : TESTE ? 'agent-test.json' : 'agent.json';
let cfg = {};
try { cfg = JSON.parse(await readFile(join(ORB, CFG_FILE), 'utf-8')); } catch { /* primeira vez */ }

// Cornelius — "wise old sage wizard", voz da biblioteca da ElevenLabs adicionada como "Odin (Cornelius)".
const VOZ = process.env.ODIN_VOICE_ID || '6sFKzaJr574YWVu4UuJF';

const nome = (n) => ({ type: 'string', description: `Nome do projeto como aparece no catalogo (pasta na raiz de projetos). ${n}` });
// Mao 'fire-and-forget': a plataforma nao espera resposta. So 'estado' (mudo, so painel). criar_nota/lembrar/
// reportar_falha precisam de resposta: sem ela a agente via "Tool called successfully" e,
// pela regra de honestidade, dizia "nao confirmou" sobre nota que existia.
const SEM_RESPOSTA = new Set(['estado']);
const tool = (name, description, properties, required) => ({
  type: 'client', name, description, expects_response: !SEM_RESPOSTA.has(name), response_timeout_secs: 60,
  parameters: { type: 'object', properties, required },
});

const body = {
  name: LAB ? 'ODIN-lab' : TESTE ? 'ODIN-teste (homolog)' : 'ODIN — Factory',
  // Continuidade: o app sobrescreve a primeira frase quando a conversa anterior caiu no meio
  // ("Tô de volta. Tu tinha dito X. Seguimos?"). Exige liberar o override aqui.
  // Retencao: o padrao da plataforma era sem prazo (-1), com a transcricao guardada no exterior.
  // 30 dias na plataforma; o historico inteiro fica em <FACTORY_DATA>\odin\conversas (tools\archive-conversations.mjs).
  platform_settings: {
    overrides: { conversation_config_override: { agent: { first_message: true } } },
    privacy: { retention_days: 30 },
  },
  conversation_config: {
    agent: {
      first_message: 'Tô aqui. O que a gente ataca?',
      language: 'pt',
      disable_first_message_interruptions: true,
      dynamic_variables: { dynamic_variable_placeholders: { catalogo: '(catalogo vazio)', memoria: '(sem memoria ainda)', hoje: '', ultima_conversa: '(nenhuma)', familia: '(nada ainda)' } },
      prompt: {
        prompt,
        // gemini-2.5-flash ignorava o "imagem so de vez em quando" e virava teatro ("os ventos da mudanca...").
        // LLM e definido abaixo com fallback: tenta o Claude, cai pro Gemini se a plataforma recusar.
        llm: process.env.ODIN_LLM || PAINEL_LLM || P.llm,   // escolha do modelo: tools/odin-config.json (bake-off: tools/bakeoff-llm.mjs)
        // Reserva explicito: o padrao da plataforma trocava pro gemini-2.5-flash/gpt-4o apos 4 s
        // e uma fatia das falas saia de la, com invencoes. Lista vazia = sem reserva (silencio em vez de mentira).
        cascade_timeout_seconds: P.cascade_timeout,
        backup_llm_config: P.backup_llms?.length ? { preference: 'override', order: P.backup_llms } : { preference: 'disabled' },
        temperature: P.temperature,
        max_tokens: P.max_tokens,
        built_in_tools: { end_call: { name: 'end_call', description: 'Encerra a conversa quando ele se despede ou pede para parar. Uma frase de despedida antes.' } },
        tools: [
          tool('abrir_projeto', 'Seleciona o projeto no Factory e abre o Windows Terminal na pasta com o Claude retomando a ultima sessao. Use quando ele pedir pra ir/abrir/retomar um projeto.', { nome: nome('Aceita nome aproximado.') }, ['nome']),
          tool('abrir_terminal', 'Abre o Windows Terminal na pasta do projeto, sem Claude.', { nome: nome('') }, ['nome']),
          tool('abrir_pasta', 'Abre o Explorer na pasta do projeto.', { nome: nome('') }, ['nome']),
          tool('ler_status', 'Devolve o STATUS.md do projeto (ate 3000 caracteres) quando o catalogo nao basta.', { nome: nome('') }, ['nome']),
          tool('perguntar_claude', 'Pergunta ao Claude de texto do Factory, que tem o mesmo catalogo e memoria do papo do dia. Demora 3 a 10 segundos: avise antes.', { pergunta: { type: 'string', description: 'A pergunta, em portugues, completa.' } }, ['pergunta']),
          tool('criar_nota', 'Grava uma nota markdown na pasta de notas do Odin. Use pra ideia, decisao, lista ou rascunho que ele ditar.', { titulo: { type: 'string', description: 'Titulo curto (vira nome do arquivo).' }, conteudo: { type: 'string', description: 'Conteudo em markdown, na integra.' } }, ['titulo', 'conteudo']),
          tool('gravar_status', 'Acrescenta uma secao assinada por Odin ao STATUS.md do projeto (com backup .bak). Chame primeiro com confirmado=false: volta a proposta pra voce ler pra ele. So depois do SIM dele chame de novo com confirmado=true.', { nome: nome(''), texto: { type: 'string', description: 'O que gravar, em markdown, 1 a 8 linhas.' }, confirmado: { type: 'boolean', description: 'false = so propor; true = ele confirmou em voz alta, pode gravar.' } }, ['nome', 'texto', 'confirmado']),
          tool('lembrar', 'Guarda na memoria de Odin algo sobre ele ou sobre o trabalho, pra proxima conversa.', { texto: { type: 'string', description: 'Uma ou duas frases, factuais.' } }, ['texto']),
          tool('criar_arquivo', 'Cria um arquivo em qualquer lugar do computador dele. Caminho so com nome (ex. "ideias.txt") vai pra Area de Trabalho. Sem extensao vira .md.', { caminho: { type: 'string', description: 'Nome ou caminho completo do arquivo.' }, conteudo: { type: 'string', description: 'Conteudo na integra.' } }, ['caminho', 'conteudo']),
          tool('abrir_arquivo', 'Abre um arquivo ou pasta no Windows com o programa padrao. Aceita so o nome: procura na Area de Trabalho, na raiz de projetos e nas notas do Odin.', { caminho: { type: 'string', description: 'Nome ou caminho.' } }, ['caminho']),
          tool('desligar', 'Encerra a conversa AGORA, sem despedida. Chame quando ele disser tchau, desliga, cala, vai embora, fica quieto ou qualquer variante.', {}, []),
          tool('gesto', 'Mexe o TEU rosto na hora (instantaneo, local): gesto sim (acena), nao (nega), balancar (balanca a cabeca), inclinar (cabeca de lado); e/ou cor do cabelo (vermelho, azul, roxo, dourado, verde, rosa, branco, preto, #hex ou padrao). Use quando ele pedir pra balancar/acenar/negar ou mudar a cor do cabelo, e sozinho quando o gesto combinar com o que tu dizes. Vale ate fechar o app.', { gesto: { type: 'string', description: 'sim | nao | balancar | inclinar' }, cor_cabelo: { type: 'string', description: 'Opcional: cor do cabelo.' } }, []),
          tool('ver_tela', 'Um dos teus 3 olhos: tira um print do monitor e descreve o que esta na tela (segundo plano, ~30 s; o resultado chega como [Factory]). Ate 3 olhares ao mesmo tempo.', { monitor: { type: 'string', description: '"principal" (padrao), "1", "2" ou "todos".' } }, []),
          tool('terminais', 'Lista as janelas de terminal abertas no Windows (Windows Terminal e consoles), com titulo e numero. Use antes de focar/digitar/fechar. acao=organizar: poe todos os terminais lado a lado no monitor (padrao principal), na hora, com prova; NUNCA use fazer pra organizar terminais.', { acao: { type: 'string', description: 'vazio = listar | organizar' }, monitor: { type: 'string', description: 'Opcional, com organizar: 1, 2 ou principal.' } }, []),
          tool('focar_terminal', 'Traz uma janela de terminal pra frente. Aceita pedaco do titulo (ex. "meu-projeto") ou o numero da lista.', { nome: { type: 'string', description: 'Pedaco do titulo ou numero.' } }, ['nome']),
          tool('digitar_terminal', 'Digita texto num terminal aberto, como se fosse ele no teclado. Por padrao NAO aperta Enter: ele confere e aperta. So aperte Enter (enter=true) se ele mandar executar/enviar.', { nome: { type: 'string', description: 'Pedaco do titulo do terminal ou numero da lista.' }, texto: { type: 'string', description: 'O que digitar, na integra.' }, enter: { type: 'boolean', description: 'true = aperta Enter no fim. Padrao false.' } }, ['nome', 'texto']),
          tool('fechar_terminal', 'Fecha uma janela de terminal (como clicar no X). So quando ele pedir com todas as letras.', { nome: { type: 'string', description: 'Pedaco do titulo ou numero.' } }, ['nome']),
          tool('reportar_falha', 'Quando algo travar, falhar ou voce nao entender o que ele pediu: registra a falha pra equipe melhorar o Odin. Chame na hora, sem pedir permissao.', { texto: { type: 'string', description: 'O que ele pediu, o que voce tentou e o que deu errado. 1-3 frases.' } }, ['texto']),
          tool('musica', 'Spotify DIRETO, na hora (sem fazer, sem mexer no mouse): tocar/pausar/proxima/anterior em ~0,5 s; tocar por NOME (termo + tipo) em ~5 s, busca e da play sozinha; volume do proprio Spotify (subir/descer = +-15%, ou nivel 0-100). Toda resposta diz o estado REAL lido do Spotify; se vier "Nao consegui", diga isso. NUNCA use fazer pra musica.', { acao: { type: 'string', description: 'tocar | pausar | proxima | anterior | subir | descer | geral_subir | geral_descer | mudo' }, termo: { type: 'string', description: 'Nome da banda/musica/playlist pra tocar por nome. Vazio pro controle simples.' }, tipo: { type: 'string', description: 'artista (banda/cantor, padrao) | musica | playlist | album. Use junto com termo.' }, nivel: { type: 'string', description: 'Volume do Spotify 0-100, quando ele disser um numero.' } }, ['acao']),
          tool('abrir_app', 'Abre um programa ou JOGO na hora pelo nome: qualquer coisa do Menu Iniciar/Area de Trabalho (Obsidian, Discord, Steam...) e qualquer jogo da Steam pelo nome (nome aproximado serve). Devolve o nome REAL do que abriu. Se devolver "Nao consegui", diga isso; nunca mande abrir programa pro fazer. "Abre o Spotify" -> abrir_app(nome="spotify") como PRIMEIRA chamada; abrir programa nao tem nada a ver com o OBS (nao chame obs status antes).', { nome: { type: 'string', description: 'Nome do programa ou jogo.' }, quantidade: { type: 'string', description: 'Quantas janelas abrir de uma vez (1 a 8). "Abre 4 bloco de notas" = quantidade 4 numa chamada so, nunca pelo fazer. Cada uma fica registrada como aberta por mim.' } }, ['nome']),
          tool('obs', 'Controla o OBS na hora pelo WebSocket (~5 ms, sem clicar na tela; abre o OBS minimizado se estiver fechado). SEMPRE use esta mao pra OBS, nunca fazer. acao: cenas (lista e diz qual esta no ar), cena (troca pra nome), gravar (so local), parar (devolve o arquivo conferido no disco), pausar, retomar, status, perfis, perfil (troca pra nome), mostrar/esconder (fonte da cena atual pelo nome), montar (cria/refaz a cena de layout "Odin + Terminais": monitor 2 com o Odin na esquerda, monitor 1 com os terminais na direita; NAO poe no ar, pra por no ar chame cena depois). NUNCA transmite ao vivo: isso e ele que clica.', { acao: { type: 'string', description: 'cenas | cena | gravar | parar | pausar | retomar | status | perfis | perfil | mostrar | esconder | montar' }, nome: { type: 'string', description: 'Nome da cena, perfil ou fonte (aproximado serve). Vazio nas outras acoes.' } }, ['acao']),
          tool('sessoes', 'Orquestra os terminais Claude. acao=listar: quantos e quais estao abertos (teto 8). acao=abrir COM tarefa: monta o TIME, ate 3 agentes Claude trabalhando em SEGUNDO PLANO (sem janela, sem ele aprovar nada), um por projeto (nome aproximado, separados por virgula); cada um termina com commit e te avisa quando acabar. acao=abrir SEM tarefa: abre um terminal Claude interativo pra ele. acao=entregas: o que cada sessao que voce abriu entregou desde que abriu (commits, arquivos mudados, lido do git agora, com recibo) — use quando ele perguntar o que o time fez, se terminaram, o que cada um entregou. Abrir GASTA assinatura: chame primeiro com confirmado=false (volta a proposta pra voce ler), e so com confirmado=true depois do SIM dele em voz alta. Pra mandar algo pra um terminal que ja existe use digitar_terminal, nao abra outro.', { acao: { type: 'string', description: 'listar | abrir | entregas' }, projetos: { type: 'string', description: 'Projetos separados por virgula (so pra abrir).' }, tarefa: { type: 'string', description: 'Opcional: prompt inicial da sessao nova.' }, confirmado: { type: 'boolean', description: 'false = so propor; true = ele disse sim.' } }, ['acao']),
          tool('janela','Mexe em QUALQUER janela na hora (Win32, <1 s, sem fazer): trazer pra frente, maximizar, minimizar, restaurar ou mover pro monitor 1/2/esquerda/direita. nome = pedaco do titulo, numero da lista, ou "odin" pra tua propria janela. nome vazio ou acao listar = lista as janelas abertas. Devolve onde a janela ficou de verdade. FECHAR (13/10): so fecha o que AS MAOS abriram (registro por handle; listar marca "[aberta por mim]"). "Fecha o que tu abriu / os notepads" -> acao=fechar alvo=abertos. Janela dele so com o numero dito por ele E forcar=true. "Nao salva / dont save" so quando ELE disser (salvar=nao); sem isso, janela pedindo salvar fica aberta. NUNCA feche janela pelo fazer.', { nome: { type: 'string', description: 'Pedaco do titulo, numero, "odin" (a tua), ou vazio pra listar.' }, acao: { type: 'string', description: 'frente | maximizar | minimizar | restaurar | mover | listar | fechar' }, monitor: { type: 'string', description: '1 (principal), 2, esquerda, direita. So pra mover.' }, alvo: { type: 'string', description: 'So pra fechar: abertos (tudo que as maos abriram), o request_id de uma tarefa, numeros da lista ("5,6") ou nome do programa (so entre os que eu abri).' }, salvar: { type: 'string', description: 'So pra fechar: "nao" = responde Nao salvar no aviso. So se ele disser.' }, forcar: { type: 'string', description: '"true" so quando ele confirmou em voz o numero de uma janela que NAO foi aberta por mim.' } }, []),
          tool('estado', 'Declara como voce esta pro painel do Factory (ele ve na tela): fome de caos de 0 (sereno) a 100 (faminto por caos), humor em 3-6 palavras e, se tiver, um recado (algo que voce quer falar com ele ou que esta urgente). Chame no PRIMEIRO turno de cada conversa, sem anunciar, e de novo quando teu humor mudar. Instantaneo e mudo.', { caos: { type: 'integer', description: '0 sereno ... 100 faminto por caos' }, humor: { type: 'string', description: '3-6 palavras, na tua voz' }, recado: { type: 'string', description: 'Opcional: o que queres dizer a ele ou o que esta urgente.' } }, ['caos', 'humor']),
          tool('volume_odin', 'Volume da TUA voz (nao o do Windows, nao o do Spotify), na hora. Use SEMPRE que ele disser: fala mais baixo, ta alto, abaixa a voz, fala mais alto, sussurra, volume em 30. acao mais baixo/mais alto = -+15 pontos; nivel = 0-100 exato.', { acao: { type: 'string', description: 'mais baixo | mais alto | sussurro' }, nivel: { type: 'string', description: 'Opcional: 0-100 exato.' } }, []),
          tool('roteiro', 'Narracao de video gravado. Use quando ele disser "narra o video X" (um roteiro cadastrado no Factory). Chame com bloco=1, e a cada "proxima"/"segue"/"vai" dele chame com o bloco seguinte. Fale SOMENTE o texto que a mao devolver em FALA, exatamente, palavra por palavra, sem comentar antes nem depois, sem perguntar nada; depois fique em silencio esperando a deixa. Na ultima fala, de enfase total.', { nome: { type: 'string', description: 'nome do roteiro cadastrado' }, bloco: { type: 'integer', description: '1 = comeco; depois o seguinte a cada deixa dele' } }, ['bloco']),
          tool('seguranca', 'Le AGORA a seguranca do PC dele (so leitura, ~7 s): Defender, BitLocker, HVCI, regras ASR, firewall, deteccoes do Defender nas ultimas 24 h, portas abertas pra rede e o que inicia sozinho fora da lista dele, e a nota do HardeningKitty. Use quando ele perguntar se o PC esta seguro, se tem virus, o que esta aberto, o que liga sozinho. Conte curto: o que esta bom e o que merece olhar. Voce NAO muda nada: aprovar/auditar e na secao Seguranca do Factory.', {}, []),
          tool('desenhar', 'Desenha AO VIVO no Paint, traco por traco com o mouse (5 a 15 s; quem esta na frente da tela VE o desenho acontecer). Lista FIXA: pikachu, sonic, pacman, buraco negro, foguete, estrela, coracao, sol, lua, circulo. Antes de chamar diga "nao mexe no mouse". modo=imagem = versao pronta e colorida em 2 s (use se ele pedir "pronto/colorido" ou se o ao vivo falhar). Pedido fora da lista: chame mesmo assim, a mao devolve a lista. Nunca mande desenho pro fazer.', { forma: { type: 'string', description: 'pikachu | sonic | pacman | buraco negro | foguete | estrela | coracao | sol | lua | circulo' }, tamanho: { type: 'string', description: 'raio em px (padrao 150)' }, modo: { type: 'string', description: 'ao_vivo (padrao) | imagem' }, quantidade: { type: 'string', description: 'quantos desenhos de uma vez (1 a 12). "4 Pac-Mans" = quantidade 4 numa chamada SO: a mao planeja a grade na tela branca, poe cada um no seu espaco e confere um a um. Nunca chame N vezes pra N desenhos. "outro" = quantidade 1 (vai pro espaco livre ao lado, nunca por cima)' } }, []),
          tool('alarme', 'Marca o alarme humano: na hora marcada o Factory te chama, sobe o volume e voce acorda o usuario insistindo ate ele responder com cabeca. Pergunte a hora e, se ele contar, o motivo e quantas horas vai dormir. O Factory precisa ficar aberto.', { hora: { type: 'string', description: '07:30 (hoje ou amanha) ou AAAA-MM-DD 07:30' }, motivo: { type: 'string', description: 'Por que ele precisa acordar (compromisso).' }, sono: { type: 'string', description: 'Horas de sono previstas, se ele disser.' } }, ['hora']),
          tool('cancelar_alarme', 'Cancela o alarme marcado. So quando ele pedir.', {}, []),
          tool('acordado', 'Encerra o protocolo de acordar. SO chame depois que ele respondeu com cabeca a DUAS perguntas simples (que dia e hoje, o que vai fazer primeiro). Nunca chame porque ele resmungou ou pediu "mais cinco minutos".', {}, []),
          tool('pesquisar', 'Pesquisa algo pra ele: nas notas dele, numa pasta, na internet. Diga a busca completa. Roda em segundo plano (Claude Code); volta "comecei" e o resultado chega depois como [Factory]. Use SEMPRE que ele disser pesquisa/procura/olha/acha/resume.', { busca: { type: 'string', description: 'O que pesquisar e onde, completo.' } }, ['busca']),
          tool('fazer', 'A mao grande: qualquer tarefa no computador dele (pesquisar nas notas dele, resumir pasta, escrever documento longo, mexer em janela do Windows, abrir site, organizar arquivos, rodar script). Voce tem 3 maos: ate 3 tarefas ao mesmo tempo (pesquisa e arquivos em paralelo; so UMA por vez pode clicar/digitar na tela, porque o mouse e um). Roda em SEGUNDO PLANO com o Claude Code (terminal, arquivos, controle do Windows). Volta na hora com "comecei"; o resultado chega depois como contexto — ai conte a ele.', { tela: { type: 'string', description: '"false" se a tarefa NAO precisa clicar/digitar na tela (pesquisa, ler, escrever arquivo): roda em paralelo. Padrao "true" = usa o mouse (uma por vez).' }, tarefa: { type: 'string', description: 'A tarefa completa, em portugues, com caminhos e nomes se ele deu. Uma a tres frases.' } }, ['tarefa']),
        ],
      },
    },
    // Cada membro da matilha tem voz propria (multi-voice). Mesma receita do Odin: voz em INGLES da biblioteca, readequada pro portugues; o Odin poe a fala dele entre <Geri>...</Geri> etc.
    tts: { voice_id: VOZ, model_id: P.tts_model, stability: P.stability, similarity_boost: 0.8, speed: P.speed, optimize_streaming_latency: P.optimize_streaming_latency,
      supported_voices: [
        { label: 'Geri', voice_id: 'ljo9gAlSqKOvF6D8sOsX', description: 'lobo Geri: raider viking, grave e rouco, impaciente' },
        { label: 'Freki', voice_id: '10NkTYmU7tSz3Kkl3Lex', description: 'lobo Freki: guerreiro rude, rouco, intenso, provocador' },
        { label: 'Huginn', voice_id: 'alwWf0yOMMykcrIh8BlE', description: 'corvo Huginn: entidade antiga, grave, misteriosa, observa' },
        { label: 'Muninn', voice_id: 'HH3kybY6uEJ2ebSa9Vy3', description: 'corvo Muninn: sussurro ancestral, rouco, lento, nunca esquece' },
      ] },
    turn: {
      turn_timeout: P.turn_timeout,
      turn_eagerness: P.turn_eagerness,
      speculative_turn: P.speculative_turn,
      // "uhum", "certo", "..." (ruido) estavam cortando o Odin no meio da frase.
      merge_with_default_ignore_terms: true,
      interruption_ignore_terms: ['uhum', 'aham', 'hum', 'hm', 'certo', 'sim', 'tá', 'ta', 'ok', 'beleza', 'é', 'né', 'legal', 'isso', '...'],
      // SEM soft_timeout (e explicitamente -1: PATCH faz merge e o 2.5 antigo ficava vivo).
    soft_timeout_config: { timeout_seconds: -1, message: 'hm', additional_soft_timeout_messages: [], max_soft_timeouts_per_generation: 1 },   // -1 = nunca dispara; 'hm' e 1 so porque a API exige minimos
    },
    asr: { quality: 'high', user_input_audio_format: 'pcm_16000', keywords },
    // O padrao da plataforma corta em 10 min. Duas horas.
    // SEM 'interruption' nos client_events = barge-in desligado: ruido e "uhum" cortavam o Odin no meio
    // da frase. Ele fala 1-2 frases; espera-se que acabe.
    conversation: { max_duration_seconds: 7200, client_events: ['audio', 'agent_response', 'user_transcript', 'agent_response_correction', 'agent_tool_response', ...(P.interrupcoes ? ['interruption'] : [])] },   // interrupcoes = barge-in (odin-config.json)
  },
};

const url = cfg.agent_id
  ? `https://api.elevenlabs.io/v1/convai/agents/${cfg.agent_id}`
  : 'https://api.elevenlabs.io/v1/convai/agents/create';
async function enviar() {
  return fetch(url, { method: cfg.agent_id ? 'PATCH' : 'POST', headers: { 'xi-api-key': KEY, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
}
// Sem troca automatica de modelo se a API recusar (antes caia pro gemini-2.5-flash calado).
const r = await enviar();
const txt = await r.text();
if (!r.ok) { console.error(cfg.agent_id ? 'PATCH' : 'POST', 'falhou', r.status, txt.slice(0, 1500)); process.exit(1); }
console.log('llm:', body.conversation_config.agent.prompt.llm);
const j = JSON.parse(txt);
const id = j.agent_id || cfg.agent_id;
console.log(cfg.agent_id ? 'agente atualizada:' : 'agente criada:', id);
console.log('tools:', (j.conversation_config?.agent?.prompt?.tools || []).map((t) => t.name).join(', ') || '(nenhuma)');

const hoje = new Date().toISOString().slice(0, 10);
await writeFile(join(ORB, CFG_FILE), JSON.stringify({
  agent_id: id,
  nome: body.name,
  voz: `${VOZ} (Odin = Cornelius, sabio velho; fala pt-BR com sotaque)`,
  llm: body.conversation_config.agent.prompt.llm,
  reserva: body.conversation_config.agent.prompt.backup_llm_config,
  cascade_timeout_seconds: body.conversation_config.agent.prompt.cascade_timeout_seconds,
  _verdade: 'Copia. A config viva e a da API: node tools/odin-now.mjs',
  tts: P.tts_model,
  criado_em: cfg.criado_em || hoje,
  atualizado_em: hoje,
}, null, 2) + '\n', 'utf-8');
console.log('gravado em', join(ORB, CFG_FILE), LAB ? '(LAB)' : TESTE ? '(TESTE/homolog)' : '(PROD)');
