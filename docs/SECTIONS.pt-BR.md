# Factory

Cockpit desktop (WinUI 3 / .NET 8, Mica) dos projetos numa pasta raiz (`FACTORY_ROOT`): o que existe, em que pé está,
onde parei, o que o Windows sabe de cada um, e um clique pra retomar. Mesma base visual do
um app irmão (tema Cyber). **Ctrl+K** abre a palette em qualquer tela.

## Seções

| Seção | O que faz |
|---|---|
| **Odin** (1ª tela) | Orb que fala + caixa de comando. **Voz:** agente ElevenLabs (voz Cornelius, pt-BR) com persona, num WebView2; clique no orb, VOZ ou **Ctrl+Alt+O** de qualquer lugar. Sabe o catálogo, a memória (`odin-memoria.md`) e a última conversa. **Painel** (coluna direita, Ctrl+P): fome de caos, humor e recado que o próprio Odin declara pela mão `estado`; LEDs *quer falar* / *urgente* / *mãos*; sliders de voz do Odin e intensidade do orb; MIC MUDO. **Mãos:** abrir projeto (wt.exe + `claude --resume`), terminal, pasta, ler STATUS, perguntar ao Claude, criar/abrir arquivo em qualquer lugar (só nome = Área de Trabalho), nota em `<dados>\odin\`, gravar STATUS (com confirmação), lembrar, ver tela, desligar, e `fazer` = qualquer tarefa via Claude Code headless (Bash, arquivos, windows-mcp; log em `<dados>\odin\tarefas\`). Cobra minutos de agente só enquanto ligada. **Caixa:** `ir <projeto>` abre o terminal com a última sessão; `novo`, `terminal`, `pasta`, `ver`, `status`, `copiar <projeto>` e nome de seção (sem IA). Outra frase vai pro Claude de texto (`claude -p`, sem tools) com o catálogo. Agente: `node tools\create-orb-agent.mjs`; plano: `docs\plano-odin-2026-09-25.md`. |
| **Projetos** | Lista agrupada (FIXADOS · ATIVO 7d · ÚLTIMOS 30d · PARADO 30d+ · NUNCA TOCADO), nuvem de tags, filtro `#tag`. Selecionar abre o **canvas** (drag = pan, roda = zoom, ← → histórico) com 10 cards: SISTEMA, PASTA (+ stack real, ▶ START), BANCOS SQLite, MANIFESTO, SESSÕES NESTA PASTA, SESSÕES DE OUTRAS PASTAS QUE CITAM, MEMÓRIA·WIKI·_MYLOOP·PROJETOS, STATUS, BRIEFING, e o projeto no centro. |
| **Busca** | Qualquer termo em todas as sessões do Claude Code (todas as pastas), memória, wiki, pasta de dados (`FACTORY_DATA`) e raiz dos projetos. Resultados com ↗ pro projeto de origem. |
| **Contextos** | Próxima ação de todos os projetos com STATUS + **JUNTAR TUDO** → `<dados>\contextos\factory-contextos-<data>.md`. |
| **Grana** | Mapa **potencial × esforço** (tamanho = confiança, anel verde = 1º real ≤ 14 d), ranking ICE/RICE, grupos por estágio. Só o que está declarado no `factory.json`. |
| **Fábrica** | Cria `<raiz>\<nome>` com README, CLAUDE.md, STATUS.md canônico, `factory.json` e `git init`. |
| **Estúdio** | Texto (STATUS do projeto, briefing, próximas ações, índice de contextos ou o que colar) → MP3 na sua voz (ElevenLabs). Saída em `<dados>\deliverables\audio`. Gasta crédito só no botão. |

## Sessões do Claude Code

- Índice de `~/.claude/projects/<slug>/*.jsonl` + `subagents/*.jsonl` (cache versionado em
  `%LOCALAPPDATA%\Factory\sessions-index.json`, incremental por mtime). Guarda título, prompts,
  texto, **arquivos tocados com a tool**, branch, timestamps, últimas mensagens.
- Por sessão: LED verde = ativa agora (arquivo mexido < 2 min) · badge **fresh / stale / drifted**
  (pasta mudou depois? branch diferente?) · "▾ N arquivos mudados" (Write/Edit dentro do projeto) ·
  `◈` **briefing de retomada** via `claude -p` (headless, sem tools, ~8 s) · `▶` `claude --resume`.
- Briefing → **GRAVAR COMO STATUS.md**: preview editável, backup `STATUS.md.bak` se já existia.

## Card SISTEMA

Processos vivos cuja linha de comando cita a pasta (WMI, com MATAR + confirmação), atalhos `.lnk`
do Desktop/Menu Iniciar que apontam pra pasta, tasks agendadas cuja ação cita a pasta. Só leitura,
sem UAC.

## Contrato que o cockpit lê

- `STATUS.md` na raiz — formato da skill `/status` (`Atualizado:`, `## Próxima ação`, `## Bloqueios`).
- `factory.json` na raiz, editável no card MANIFESTO:

| Campo | Valores |
|---|---|
| `kind` | produto · cliente · interno · lab · conteudo |
| `stage` | ideia · construindo · no-ar · vendendo · pausado · morto |
| `model` | adsense · infoproduto · saas · servico · afiliado · aposta · salario · nenhum |
| `potential`, `effort`, `confidence`, `reach` | 1–5 (0 = não avaliado) |
| `first_money_days` | dias até o primeiro real (0 = não sei); ≤ 14 vira anel verde |
| `revenue_monthly`, `revenue_target` | R$/mês hoje · meta em 90 d |
| `tags`, `pinned`, `money`, `url`, `notes` | livres |

**ICE** = potencial × confiança ÷ esforço (confiança ausente conta 3). **RICE** = alcance × ICE.
Tags inferidas (python, node, dotnet, winui3, wordpress, sqlite…) vêm dos manifestos
da raiz e não vão pro `factory.json`.

## Onde o app escreve

Pelo app: `factory.json` do projeto selecionado (SALVAR), `STATUS.md` do projeto (com confirmação e
`.bak`), `<dados>\contextos\` (JUNTAR TUDO), `<dados>\deliverables\audio\` (ESTÚDIO),
`<raiz>\<nome>` (FÁBRICA) e caches em `%LOCALAPPDATA%\Factory\` (índice, briefings, apps.json,
`chat\` com o papo por dia, `odin-memoria.md`, `odin-ultima-conversa.md`).
Pelo **Odin** (só por pedido falado do usuário): `<dados>\odin\` (notas, `tarefas\`),
qualquer arquivo que ele mandar criar (`criar_arquivo`; só nome = Área de Trabalho), STATUS.md do projeto
com `.bak` e gate de confirmação, e o que a mão `fazer` fizer via Claude Code (logado em `tarefas\`).

## Build

```powershell
dotnet build src\Factory\Factory.csproj
dotnet publish src\Factory -c Release -r win-x64 --self-contained -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false
```

`Directory.Build.props` (do rotinas) é o que faz WinUI 3 buildar sem Visual Studio. Não mover pro csproj.
NuGets: WindowsAppSDK 1.7, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, System.Management, TaskScheduler.
Raiz varrida: env `FACTORY_ROOT` (padrão `%USERPROFILE%\Projects` se existir, senão modo demo; `FACTORY_DEMO=1` força o demo); dados em `FACTORY_DATA` (padrão `<raiz>\.factory`); perfil em `FACTORY_PROFILE` (padrão `<dados>\profile`). ElevenLabs: env `ELEVENLABS_API_KEY`.

Pesquisa e plano que geraram a versão atual: `docs\pesquisa-2026-09-03\`.
