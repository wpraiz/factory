# Guia de UI/UX do Factory (e de apps WinUI irmãos)

Régua usada pelos agentes `ux-reviewer` e `ui-reviewer` (em `~/.claude/agents/`). Toda tela nova
ou alterada passa nos dois ANTES de compilar. Regra de uso: `Agent(ux-reviewer)` com a lista de
arquivos; corrige os ALTOs; depois `Agent(ui-reviewer)`; corrige; aí `dotnet build`.

## Princípio

Quem usa abre isso todo dia, no próprio PC, com atenção curta. **Um clique abre. O estado está na
tela. O texto diz o que vai acontecer.** Bonito é consequência de contido: Fluent do Windows 11
com a paleta Cyber por cima, não efeito em cima de efeito.

## Estrutura de tela

- Primeira tela = ODIN (orb + caixa de comando). APPS logo depois. Nada de lista
  genérica na frente.
- Cada seção: título `// NOME` (CyberTitle), uma linha de contexto (CyberBody), ações da seção à
  direita na mesma linha, depois o conteúdo. Status/erros numa linha de caption logo abaixo.
- Ação principal da seção é o único botão `ArcadeButtonGo` (verde) visível. Secundárias em
  `ArcadeButton` (cyan). Perigosas em `ArcadeButtonDanger` e só com confirmação.
- Botões em verbo, CAIXA ALTA, curtos: `ABRIR`, `GERAR`, `ESCOLHER APPS`, `✓ PRONTO`.
- Modo de edição é um toggle no mesmo botão (rótulo muda), nunca uma tela separada.

## Estado

- Rodando/parado: LED de 8px (`StatusBrush` + `LiveOpacity`), sempre no mesmo canto.
- Carregando: `ProgressRing` de 18–20px ao lado da ação que disparou, nunca overlay bloqueante.
- Gasta crédito ou mata processo: só no clique, com tooltip dizendo isso, e status dizendo quanto
  ("1x por dia", "N chars").
- Falta configuração: o status diz o que fazer ("defina X e reabra"), não só "erro".
- Salvo automaticamente: o status diz onde ficou (nome do arquivo).

## Visual

- Paleta e estilos vêm de `Themes/Cyber.xaml`. Nada de cor hex ou FontSize solto no XAML de
  página sem motivo escrito em comentário.
- Neon é acento: ação principal, LED, aviso. Texto corrido é `CyberText`; secundário é
  `CyberDim`. Máximo 2 cores de acento por região.
- Espaçamento só em 4/8/12/16/20/28, pelos tokens do tema: `PagePadding`, `SectionGap` 20,
  `BandGap` 28, `TileGap` 16. Cards: raio 6 (console) ou `TileRadius` 10 (tile), 1px
  `CyberPanelEdge`, padding ≥ 16. Caixa de ícone do tile: `TileIconBox` (48, raio 10) em APPS e VIDA.
- Tipografia: CyberDisplay 32 · CyberTitle 24 (branco, 1 por tela) · CyberHeading 16 · CyberMetric 20 ·
  CyberBody 14 · CyberCaption 12 · CyberLabel 11 (caixa alta). Mínimo absoluto 11. Mono sempre. FontSize solto só com comentário.
- **Contraste (medido sobre Panel #11161F):** texto secundário `CyberDim` #8A98AD = 6,2:1 (era #5E6B80 = 3,4:1, reprovava);
  corpo #C8D3E0 = 12:1; borda de controle `CyberControlEdge` #5E6B80 = 3,4:1 (ok pra 1.4.11); `CyberMute` #3A4558 nunca é texto.
  Texto pequeno precisa de 4,5:1. Hex solto em página é bug: use os tokens (`CyberWell`, `CyberRaised`, `ChartA/ChartB`...).
- **Cor por papel:** ciano = interativo (foco, seleção, aba ativa, link); verde = vivo/ok e UMA ação principal por tela;
  âmbar = aviso/pendente; magenta = erro/destrutivo; violeta = ODIN/IA e série secundária. Botão neutro = contorno cinza.
- **Nada de reticências em frase.** `MaxLines=1` + `CharacterEllipsis` só em identificador curto (nome de processo, caminho) com
  tooltip; detalhe que carrega "o que fazer" quebra em 2–3 linhas. Uma ação principal por linha de lista; o resto vai num menu "…".
- Uma cor de acento por região (card, faixa, cabeçalho). Estado = LED, aresta ou rótulo curto
  (`CyberLabel`), nunca a frase inteira em neon. Aviso = `CyberCardWarn` (âmbar); vivo = `CyberCardAccent`
  (ciano). Chip de estado = `CyberChip`. Ícone-só secundário = `GhostButton`.
- Ícones: Segoe Fluent Icons. Ícone real do executável (thumbnail do shell) quando existir;
  glyph como fallback. Glyphs conhecidos: E701 wifi · E711 cancel · E720 mic · E722 camera ·
  E756 terminal · E768 play · E76B/E76C chevrons · E774 globe · E8B7 folder · E8D6 audio ·
  E823 clock · E71D all-apps · E946 info · E7BA warning · E700 global-nav (hambúrguer) ·
  E72A/E72B forward/back · E72C refresh · E723 attach (clipe) · E8C8 copy · E77B contact.
- Texto em container de largura fixa: `TextTrimming="CharacterEllipsis"` e `MaxLines`.

## Movimento

- 150–350 ms, easing padrão (cubic 0.1,0.9 / 0.2,1), só no que muda de estado.
- Hover: escala 1.03 com spring; press: 0.97; entrada de grid: fade + sobe 14px em cascata de
  35 ms por item, só na primeira pintura da página.
- Nada em loop. Nada que bloqueie clique. Nada de animar por animar.

## Áudio

- Toca DENTRO do app (barra do rodapé, `AudioPlayer`), nunca abre programa externo.
- Gerar áudio custa crédito: só por clique, cache por conteúdo/data, status diz que foi cache.
- Ctrl+Espaço pausa/continua de qualquer seção.

## Teclado

- Ctrl+K palette em qualquer tela. Esc fecha overlays. Enter executa o item focado.
- Toda ação principal alcançável sem mouse.
