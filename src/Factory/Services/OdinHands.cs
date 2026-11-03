using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Factory.Services;

public interface IOdinHands
{
    /// <summary>Executa uma ferramenta pedida pela agente de voz e devolve o texto que ela vai ler. Nunca lanca.
    /// Toda execucao vira recibo hand-result/v1 (HandLog). origem: voz | online | texto | interno.</summary>
    Task<string> RunAsync(string name, JsonElement args, CancellationToken ct, string origem = "voz", string? conv = null, string? episode = null);
    /// <summary>Uma tarefa `fazer` terminou (thread de fundo!). Task = o pedido; Result = o que o Claude Code respondeu.</summary>
    event EventHandler<(string Task, string Result, bool Ok)>? TaskFinished;
    /// <summary>Tarefa `fazer` ainda rodando ou presa na fila do mouse (thread de fundo!): vira contexto curto pro Odin
    /// nao ficar mudo (otherwise the user waited minutes in silence to hear "operation was canceled").</summary>
    event EventHandler<(string Task, string Msg)>? TaskProgress;
    /// <summary>O Odin declarou como esta (mao `estado`): fome de caos 0-100, humor curto e, se houver, um recado pra ele.</summary>
    event EventHandler<(int Caos, string Humor, string Recado)>? StateDeclared;
    /// <summary>Alarme gravado/cancelado (mao `alarme`/`cancelar_alarme`): a UI recarrega alarme.json.</summary>
    event EventHandler? AlarmChanged;
    /// <summary>Ele provou que acordou (mao `acordado`): a UI encerra o protocolo.</summary>
    event EventHandler? WokeUp;
}

/// <summary>
/// As maos do ODIN: o que a agente de voz consegue fazer no Factory. Cada ferramenta e uma "client
/// tool" declarada em tools\create-orb-agent.mjs; a plataforma chama, o JS repassa, isto executa.
/// Design rule: Odin is free to act. Maos rapidas criam/abrem arquivo em qualquer
/// lugar (so nome = Area de Trabalho); a mao `fazer` delega ao Claude Code headless com Bash, arquivos e
/// windows-mcp, em segundo plano, e o resultado volta pro Odin como contexto. Log de cada tarefa em
/// .factory\odin\tarefas\. Linhas que ficam: nao apaga sem pedido explicito, nao gasta, nao fala com terceiro.
/// </summary>
public sealed class OdinHands : IOdinHands
{
    public static readonly string NotesDir = Path.Combine(AppPaths.Odin);
    public static readonly string TasksDir = Path.Combine(NotesDir, "tarefas");
    private static readonly string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public event EventHandler<(string Task, string Result, bool Ok)>? TaskFinished;
    public event EventHandler<(string Task, string Msg)>? TaskProgress;
    public event EventHandler<(int Caos, string Humor, string Recado)>? StateDeclared;
    public event EventHandler? AlarmChanged;
    public event EventHandler? WokeUp;

    private static string HandsPrompt =>
        "Voce e as MAOS do Odin, a voz do dono deste computador, dentro do cockpit Factory. Ele pediu a tarefa abaixo em voz alta. " +
        $"Faca a tarefa COMPLETA agora, sem perguntar. Area de Trabalho do usuario: {Desktop}. Projetos em {AppPaths.Root}. Notas do Odin: {AppPaths.Odin}. " +
        "Voce tem terminal (PowerShell via bash), arquivos e o windows-mcp (Snapshot/Click/Type/App pra mexer em janelas; prefira PowerShell quando der). " +
        "Nao apague nada que a tarefa nao mande apagar explicitamente; nao gaste dinheiro; nao mande mensagem a ninguem. " +
        $"Plano, analise, relatorio ou nota que a tarefa nao mandou salvar num lugar especifico vai em {AppPaths.Odin}, NUNCA dentro de um repositorio. " +
        "Ao terminar responda em pt-BR, no maximo 5 linhas, dizendo o que fez e os caminhos, como quem fala em voz alta. Sem markdown.";

    private readonly ProjectCatalog _catalog;
    private readonly ILauncher _launcher;
    private readonly ISessionIndex _index;
    private readonly IChatService _chat;
    private readonly IVoiceAgent _voice;
    private readonly NavigationService _nav;

    public OdinHands(ProjectCatalog catalog, ILauncher launcher, ISessionIndex index, IChatService chat, IVoiceAgent voice, NavigationService nav)
    {
        _catalog = catalog;
        _launcher = launcher;
        _index = index;
        _chat = chat;
        _voice = voice;
        _nav = nav;
    }

    /// <summary>Dados do recibo que a mao longa carrega pro segundo plano (linha final do hand-result).</summary>
    private sealed record Recibo(string Rid, string Mao, HandLog.Fonte Origem, JsonElement Args, DateTimeOffset Inicio);

    private static bool Longa(string name) => name is "fazer" or "pesquisar" or "ver_tela";

    public async Task<string> RunAsync(string name, JsonElement args, CancellationToken ct, string origem = "voz", string? conv = null, string? episode = null)
    {
        // origem + conversa ElevenLabs: vai pro recibo (episode_id/conv_id do hand-result/v1)
        HandLog.Fonte fonte = new(origem, conv, episode);
        var rid = HandLog.NovoId();
        var inicio = DateTimeOffset.Now;
        // copia: o JsonDocument do chamador morre antes da mao longa terminar em segundo plano
        var copia = args.ValueKind == JsonValueKind.Undefined ? default : args.Clone();
        var recibo = new Recibo(rid, name, fonte, copia, inicio);
        // Router: o cerebro as vezes manda coisa simples pro `fazer`; se a frase inteira e um comando
        // conhecido, resolve na hora com a mao rapida em vez de subir um Claude Code de minutos.
        if (name == "fazer" && await Rotear(Str(args, "tarefa"), ct) is { } rapido)
        {
            var (faladoR, provaR) = HandLog.SepararProva(rapido, name);
            HandLog.Rapida(rid, name, fonte, copia, inicio, faladoR, faladoR.StartsWith("A mao falhou"), provaR);
            return faladoR;
        }
        if (Longa(name)) HandLog.Inicio(rid, name, fonte, copia, inicio);

        string result; var excecao = false;
        try { result = await Executar(name, args, ct, recibo); }
        catch (Exception ex) { result = $"A mao falhou: {ex.Message}"; excecao = true; }
        excecao |= result.StartsWith("A mao falhou"); // o switch engole a excecao e devolve esse texto

        // linha "[prova] {json}" (estado lido depois da acao) vira evidencia no recibo e sai do texto que o Odin fala
        if (!Longa(name))
        {
            var (falado, prova) = HandLog.SepararProva(result, name);
            HandLog.Rapida(rid, name, fonte, copia, inicio, falado, excecao, prova);
            return falado;
        }
        // mao longa que nem comecou (tarefa vazia, sem Claude Code): fecha o recibo aqui mesmo
        if (excecao || !(result.StartsWith("Comecei") || result.StartsWith("Na fila")))
            HandLog.Longa(rid, name, fonte, copia, inicio, result, null, null, erro: true);
        return $"{result} (request_id={rid})";
    }

    private async Task<string> Executar(string name, JsonElement args, CancellationToken ct, Recibo recibo)
    {
        try
        {
            return name switch
            {
                "abrir_projeto" => AbrirProjeto(Str(args, "nome")),
                "abrir_terminal" => Com(Str(args, "nome"), p => { _launcher.OpenTerminal(p.Path); return $"Terminal aberto em {p.Name}."; }),
                "abrir_pasta" => Com(Str(args, "nome"), p => { _launcher.OpenExplorer(p.Path); return $"Pasta de {p.Name} aberta."; }),
                "ler_status" => Com(Str(args, "nome"), LerStatus),
                "perguntar_claude" => await PerguntarClaude(Str(args, "pergunta"), ct),
                "criar_nota" => CriarNota(Str(args, "titulo"), Str(args, "conteudo")),
                "gravar_status" => Com(Str(args, "nome"), p => GravarStatus(p, Str(args, "texto"), Bool(args, "confirmado"))),
                "lembrar" => Lembrar(Str(args, "texto")),
                "estado" => Estado(args),
                // Trust phase 1: em 5 testes com prova de pixel so 2 desenhos sairam inteiros (o Paint engole parte do
                // arrasto). Rule: o que nao funciona sai do ar e o Odin diz que ainda nao sabe.
                "desenhar" => DesenharLigado ? await Task.Run(() => Desenhar(Str(args, "forma"), Str(args, "tamanho"), Str(args, "modo"), Str(args, "quantidade")))
                    : "Nao consegui: a mao desenhar esta desligada ate funcionar de verdade (em 5 testes so 2 sairam inteiros). Diga a ele, curto, que desenhar no Paint voce ainda nao sabe fazer direito.",
                "alarme" => Alarme(Str(args, "hora"), Str(args, "motivo"), Str(args, "dor"), Str(args, "sono")),
                "cancelar_alarme" => CancelarAlarme(),
                "acordado" => Acordado(),
                "criar_arquivo" => CriarArquivo(Str(args, "caminho"), Str(args, "conteudo")),
                "abrir_arquivo" => AbrirArquivo(Str(args, "caminho")),
                "fazer" => Fazer(Str(args, "tarefa"), mouse: !Str(args, "tela").Equals("false", StringComparison.OrdinalIgnoreCase), recibo: recibo),
                // busca vazia passava pelo "Tarefa vazia" (o prefixo enchia a string) e gastava executor + fiscal a toa
                "pesquisar" => string.IsNullOrWhiteSpace(Str(args, "busca")) ? "Tarefa vazia: pesquisar sem tema. Pergunte a ele o que pesquisar."
                    : Fazer("Pesquise e me conte em ate 5 frases: " + Str(args, "busca"), mouse: false, recibo: recibo, semTela: true),   // pesquisa nao olha a tela: sem uvx windows-mcp
                "terminais" => CommandLine.Fold(Str(args, "acao")) is "organizar" or "organiza" or "lado a lado" ? await Task.Run(() => OrganizarTerminais(Str(args, "monitor"))) : Terminais(),
                "focar_terminal" => ComTerminal(Str(args, "nome"), w => WindowsHands.Focus(w.Handle) ? $"Terminal '{w.Title}' na frente.\n{WindowsHands.Prova(w.Handle, "frente").Replace("\"mao\":\"janela\"", "\"mao\":\"focar_terminal\"")}" : $"Achei '{w.Title}' mas o Windows nao deixou trazer pra frente. Clique nele."),
                "digitar_terminal" => ComTerminal(Str(args, "nome"), w => Digitar(w, Str(args, "texto"), Bool(args, "enter"))),
                "fechar_terminal" => ComTerminal(Str(args, "nome"), w => WindowsHands.Close(w.Handle) ? $"Pedi pra fechar '{w.Title}'. Se tiver algo rodando, o terminal vai perguntar." : "Nao consegui mandar fechar."),
                "reportar_falha" => ReportarFalha(Str(args, "texto")),
                // musica/abrir_app/janela esperam o Windows responder (ate 4 s) pra ter prova: fora da thread de UI
                "musica" => await MusicaAsync(Str(args, "acao"), Str(args, "termo"), Str(args, "nivel"), Str(args, "tipo")),
                "abrir_app" => await Task.Run(() => AbrirAppVarias(Str(args, "nome"), Str(args, "quantidade"))),
                "obs" => await ObsHands.RunAsync(Str(args, "acao"), Str(args, "nome"), ct),
                "sessoes" => Sessoes(Str(args, "acao"), Str(args, "projetos"), Str(args, "tarefa"), Bool(args, "confirmado")),
                "janela" => await Task.Run(() => Janela(Str(args, "nome"), Str(args, "acao"), Str(args, "monitor"), Str(args, "alvo"), Str(args, "salvar"), Str(args, "forcar"))),
                "ver_tela" when !CommandLine.Fold(Str(args, "monitor")).StartsWith("todo") && await Task.Run(() => WindowsHands.CapturarMonitor(Str(args, "monitor") is { Length: > 0 } mm ? mm : "principal")) is { } png
                    => Fazer(model: "haiku", mouse: false, recibo: recibo, imagem: png, tarefa:
                        $"Leia a imagem {png} com a ferramenta Read: e o print de UM monitor do usuario, capturado agora pelo Factory. "
                        + "Descreva em ate 4 frases o que esta na tela: janelas abertas, o que cada uma mostra, e qualquer coisa que pareca erro ou pedido de atencao. "
                        // the capture was right but the model mislabelled the monitor once. Factory owns the monitor numbering.
                        + $"Comece EXATAMENTE com \"{WindowsHands.RotuloMonitor(Str(args, "monitor"))}:\" e nao deduza numero nem lado de monitor por conta propria. Nao use outra ferramenta alem do Read."),
                "ver_tela" => Fazer(model: "haiku", mouse: false, recibo: recibo, tarefa:$"Tire um print do monitor {(Str(args, "monitor") is { Length: > 0 } m ? m : "principal")} com o windows-mcp (Screenshot) e descreva em ate 4 frases o que esta na tela: janelas abertas, o que cada uma mostra, e qualquer coisa que pareca erro ou pedido de atencao. "
                    + $"NUMERACAO DOS MONITORES DO USUARIO (use esta, NAO a do windows-mcp; confira pelas coordenadas das janelas no Snapshot antes de descrever): {WindowsHands.DescreverMonitores()}. Comece a resposta dizendo qual monitor (numero e esquerda/direita) voce descreveu."),
                // Windows/Defender/ports/startup posture read on demand (read-only, ~7 s, 60 s cache)
                "seguranca" => await App.Services.GetRequiredService<IHostSecurityService>().ResumoAsync() + "\n" + App.Services.GetRequiredService<ISecurityWatch>().ResumoAlertas(),
                "roteiro" => Roteiro(Str(args, "nome"), args.ValueKind == JsonValueKind.Object && args.TryGetProperty("bloco", out var bl) && bl.ValueKind == JsonValueKind.Number ? bl.GetInt32().ToString() : Str(args, "bloco")),
                "volume_odin" => await VolumeOdin(Str(args, "acao"), Str(args, "nivel")),
                "desligar" => "Desligando.",
                "gesto" => "Feito no rosto.",   // executa no cliente (factory-orb.js); aqui so pra regra dos 3 lugares
                _ => $"Nao tenho a mao '{name}'.",
            };
        }
        catch (Exception ex)
        {
            return $"A mao falhou: {ex.Message}";
        }
    }

    // ---------- projetos ----------

    private ProjectInfo? Achar(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var q = CommandLine.Fold(nome.Trim());
        var all = _catalog.All.ToList();
        return all.FirstOrDefault(p => CommandLine.Fold(p.Name) == q)
            ?? all.Where(p => CommandLine.Fold(p.Name).Contains(q) || q.Contains(CommandLine.Fold(p.Name)))
                  .OrderByDescending(p => p.LastTouch).FirstOrDefault()
            ?? all.Select(p => (p, s: PaletteMatcher.Score(q, p.Name, ""))).Where(x => x.s > 60)
                  .OrderByDescending(x => x.s).Select(x => x.p).FirstOrDefault();
    }

    private string Com(string nome, Func<ProjectInfo, string> acao)
    {
        var p = Achar(nome);
        if (p is null) return $"Nao achei projeto parecido com '{nome}' no catalogo. Peca o nome de novo.";
        return acao(p);
    }

    private string AbrirProjeto(string nome) => Com(nome, p =>
    {
        App.Services.GetRequiredService<ProjectsViewModel>().SelectByPath(p.Path);
        var last = _index.ForProject(p.Path).FirstOrDefault();
        _launcher.OpenClaude(p.Path, last?.Id);
        return last is null
            ? $"Abri {p.Name} com o Claude limpo: nao havia sessao anterior nessa pasta."
            : $"Abri {p.Name} retomando a sessao '{last.Title}' de {ProjectInfo.Humanize(last.LastWrite)}.";
    });

    private static string LerStatus(ProjectInfo p)
    {
        var f = Path.Combine(p.Path, "STATUS.md");
        if (!File.Exists(f)) return $"{p.Name} nao tem STATUS.md.";
        var t = File.ReadAllText(f);
        return t.Length > 3000 ? t[..3000] + "\n(cortado)" : t;
    }

    private async Task<string> PerguntarClaude(string pergunta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pergunta)) return "Pergunta vazia.";
        if (!_chat.Available) return "O Claude de texto nao esta disponivel nesta maquina.";
        // O Claude de texto precisa saber o que o Odin consegue fazer, senao responde "nao sei" pra pergunta
        // sobre as maos (e.g. "como toco uma musica pelo nome?").
        const string maos = "Maos do Odin (client tools): abrir_projeto, abrir_terminal, abrir_pasta, ler_status, perguntar_claude, criar_nota, gravar_status, lembrar, estado, "
            + "criar_arquivo, abrir_arquivo, fazer (Claude Code em segundo plano com windows-mcp), pesquisar, ver_tela, terminais, focar_terminal, digitar_terminal, fechar_terminal, "
            + "reportar_falha, musica (acao tocar/pausar/proxima/anterior/subir/descer/mudo; termo=nome toca uma musica/banda pelo Spotify), abrir_app, desligar. "
            + "Responda em ate 3 frases, dizendo qual mao usar e com que argumento.";
        var r = await _chat.AskAsync("[pergunta feita por voz, via Odin. " + maos + "] " + pergunta, _catalog.All.ToList(), ct);
        return r.Text;
    }

    // ---------- escrita (limitada) ----------

    private static string CriarNota(string titulo, string conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) return "Nota vazia, nao gravei.";
        Directory.CreateDirectory(NotesDir);
        var slug = Slug(string.IsNullOrWhiteSpace(titulo) ? "nota" : titulo);
        var f = Path.Combine(NotesDir, $"{DateTime.Now:yyyy-MM-dd-HHmm}-{slug}.md");
        File.WriteAllText(f, $"# {titulo.Trim()}\n\n{conteudo.Trim()}\n\n_ditado a Odin em {DateTime.Now:yyyy-MM-dd HH:mm}_\n", new UTF8Encoding(false));
        return $"Nota gravada: {Path.GetFileName(f)} em .factory\\odin.";
    }

    private static string GravarStatus(ProjectInfo p, string texto, bool confirmado)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "Texto vazio, nao gravei.";
        // Gate no codigo, nao so no prompt: sem confirmado=true nada toca o disco.
        if (!confirmado)
            return $"PROPOSTA (nada gravado ainda) para o STATUS de {p.Name}:\n{texto.Trim()}\nLeia pra ele e pergunte se grava. Se disser sim, chame de novo com confirmado=true.";
        var f = Path.Combine(p.Path, "STATUS.md");
        if (File.Exists(f)) File.Copy(f, f + ".bak", overwrite: true);
        File.AppendAllText(f, $"\n\n## Odin — {DateTime.Now:yyyy-MM-dd HH:mm}\n{texto.Trim()}\n", new UTF8Encoding(false));
        return $"Gravei no STATUS de {p.Name}, com backup .bak.";
    }

    private string Lembrar(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "Nada pra lembrar.";
        _voice.AppendMemory(texto.Trim());
        return "Guardado.";
    }

    // ---------- livre: arquivos em qualquer lugar e a mao grande ----------

    private static string CriarArquivo(string caminho, string conteudo)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return "Sem nome de arquivo.";
        var f = Resolver(caminho, paraCriar: true);
        if (!Path.HasExtension(f)) f += ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        var existia = File.Exists(f);
        File.WriteAllText(f, conteudo ?? "", new UTF8Encoding(false));
        return (existia ? "Sobrescrevi " : "Criei ") + f;
    }

    private string AbrirArquivo(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return "Sem nome.";
        var f = Resolver(caminho, paraCriar: false);
        if (f.Length == 0) return $"Nao achei '{caminho}' na Area de Trabalho, na raiz de projetos nem nas notas.";
        if (Directory.Exists(f)) _launcher.OpenExplorer(f); else _launcher.OpenFile(f);
        return $"Abri {f}.";
    }

    /// <summary>Caminho absoluto usa direto; so nome procura na Area de Trabalho, <raiz> e notas (pra criar: Area de Trabalho).</summary>
    private static string Resolver(string caminho, bool paraCriar)
    {
        var c = caminho.Trim().Trim('"');
        var low = CommandLine.Fold(c);
        if (low.StartsWith("area de trabalho")) c = Path.Combine(Desktop, c[16..].TrimStart('\\', '/', ' '));
        else if (low.StartsWith("desktop")) c = Path.Combine(Desktop, c[7..].TrimStart('\\', '/', ' '));
        if (Path.IsPathRooted(c)) return c;
        if (paraCriar) return Path.Combine(Desktop, c);
        foreach (var dir in new[] { Desktop, ProjectScanner.Root, NotesDir })
        {
            var direto = Path.Combine(dir, c);
            if (File.Exists(direto) || Directory.Exists(direto)) return direto;
            try
            {
                var hit = Directory.EnumerateFileSystemEntries(dir).FirstOrDefault(e =>
                    CommandLine.Fold(Path.GetFileName(e)) == low || CommandLine.Fold(Path.GetFileNameWithoutExtension(e)) == low);
                if (hit is not null) return hit;
            }
            catch (Exception) { }
        }
        return "";
    }

    // 3 HANDS and 3 EYES (parallel executors). Um so MOUSE: tarefa
    // que clica/digita na tela pega o MouseGate, senao dois agentes brigam pelo cursor. Pesquisa, arquivos e
    // olhos (screenshot) rodam em paralelo sem mouse.
    private const int MAOS = 3, OLHOS = 3;
    private static readonly SemaphoreSlim FazerGate = new(MAOS, MAOS);
    private static int _slotSeq;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> EmCurso = new();
    /// <summary>Tarefas que as maos (agentes do fazer) estao executando agora, na ordem em que comecaram. So leitura (FROTA).</summary>
    public static string[] TrabalhandoAgora() => EmCurso.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
    private static readonly SemaphoreSlim MouseGate = new(1, 1);
    private static readonly SemaphoreSlim OlhosGate = new(OLHOS, OLHOS);
    private static int _fila;

    // THE FISCAL: depois que o executor termina, um segundo Claude Code, haiku, SO COM OLHOS
    // (screenshot/arvore da tela, sem clique nem teclado), confere se o que o executor DISSE bate com a tela.
    // O fiscal sozinho nao confirma: o selo que o Odin recebe sai do veredito do recibo (ver Fazer).
    private const string FiscalPrompt = "Voce e o FISCAL do Factory. Voce NAO executa nada: so olha (tela: Screenshot/Snapshot; disco: Read/Glob/Grep) e responde. "
        + "Recebe uma TAREFA e o que o EXECUTOR alegou. Confira AGORA se a ALEGACAO e verdadeira (ex.: 'musica tocando' = player mostra a faixa em reproducao; "
        + "'bola desenhada no Paint' = ha um circulo no canvas do Paint; 'arquivo salvo' = o arquivo existe). Seja cetico: alegacao sem evidencia = false. "
        + "Voce julga a ALEGACAO, nao se a tarefa deu certo: se o executor disse que NAO achou / NAO conseguiu / algo NAO existe e a evidencia bate, ok=true "
        + "(exemplo: 'nao ha Evernote instalado' foi confirmado pelo fiscal e mesmo assim saiu false, e o Odin disse que nao foi feito). "
        + "Alegacao sobre arquivo, pasta, config ou conteudo em disco: confira com Read/Glob/Grep, nao pela tela. Tela so quando a alegacao e sobre o que aparece na tela. "
        + "Diferenca trivial NAO reprova (±1 numa contagem, arredondamento, formatacao, linha vazia final que o Read mostra): reprove so o que muda o sentido pra ele "
        + "(exemplo: arquivo de 51 linhas, o Read mostrou 52 e o fiscal reprovou uma resposta certa). "
        + "Alegacao sobre config do Odin (cerebro/modelo/reserva) so vale se o executor mostrou a saida de odin-now.mjs; se citou agente-*.json = false. "
        + "Responda SOMENTE um JSON numa linha: {\"ok\": true|false, \"evidencia\": \"o que voce viu, 1 frase\"}";

    private static readonly Regex FiscalOk = new("\"ok\"\\s*:\\s*(true|false|null)", RegexOptions.IgnoreCase);
    private static readonly Regex FiscalEv = new("\"evidencia\"\\s*:\\s*\"([^\"]{0,300})", RegexOptions.IgnoreCase);

    private static async Task<(bool? Ok, string Evidencia)> FiscalizarAsync(string tarefa, string alegacao, bool semMcp = false)
    {
        try
        {
            await OlhosGate.WaitAsync();
            ClaudeReply r;
            var como = !semMcp ? "Confira na tela."
                : tarefa.Contains(".png", StringComparison.OrdinalIgnoreCase) ? "A tarefa cita uma imagem ja capturada: abra a MESMA imagem com Read e confira contra ela."
                : "Esta checagem nao tem tela: confira pelo disco (Read/Glob/Grep). Se a alegacao so pode ser checada na internet (voce nao tem web), responda {\"ok\": null, \"evidencia\": \"nao verificavel daqui: <por que>\"}.";
            try { r = await ClaudeCli.RunAgentAsync(FiscalPrompt, $"TAREFA: {tarefa}\n\nO EXECUTOR ALEGOU: {alegacao}\n\n{como} Responda o JSON.", ProjectScanner.Root, CancellationToken.None, "haiku", readOnly: true, maxMinutes: 3, semMcp: semMcp); }
            finally { OlhosGate.Release(); }
            var m = FiscalOk.Match(r.Text ?? "");
            if (!m.Success) return (null, "fiscal nao respondeu no formato: " + (r.Text ?? "").Replace('\n', ' ')[..Math.Min(160, (r.Text ?? "").Length)]);
            var e = FiscalEv.Match(r.Text ?? "");
            var ev = e.Success ? e.Groups[1].Value : "(sem evidencia)";
            // ok:null = "nao verificavel daqui" (ex.: fato de internet): vira "? NAO CONFIRMADO", o Odin diz que nao confirmou
            return m.Groups[1].Value.ToLowerInvariant() switch { "true" => (true, ev), "false" => (false, ev), _ => (null, ev) };
        }
        catch (Exception ex) { return (null, "fiscal falhou: " + ex.Message); }
    }

    private string Fazer(string tarefa, string model = "sonnet", bool mouse = true, Recibo? recibo = null, string? imagem = null, bool semTela = false)
    {
        if (string.IsNullOrWhiteSpace(tarefa)) return "Tarefa vazia.";
        if (!ClaudeCli.Available) return "O Claude Code nao esta disponivel nesta maquina; a mao grande nao funciona.";
        var t = tarefa.Trim();
        var pos = Interlocked.Increment(ref _fila);
        _ = Task.Run(async () =>
        {
            string result; var ok = true; var t0 = DateTime.Now;
            // deadline 8 -> 3 min. Tarefa que passa disso quase sempre e clique em janela travada (5 no OBS morreram
            // em 324-480 s); melhor falhar cedo e dividir do que o Odin ficar mudo 8 min.
            var prazo = model == "haiku" ? 2 : 3;
            await FazerGate.WaitAsync();
            var slot = Interlocked.Increment(ref _slotSeq); EmCurso[slot] = t;   // FROTA/squad mostra quem esta trabalhando
            if (mouse && !await MouseGate.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                TaskProgress?.Invoke(this, (t, "esta na fila esperando outra tarefa de tela largar o mouse (so uma clica por vez)"));
                await MouseGate.WaitAsync();
            }
            using var fim = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                // warn only near the cut-off (a real problem), not on a timer
                try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, prazo - 1)), fim.Token); } catch (OperationCanceledException) { return; }
                TaskProgress?.Invoke(this, (t, $"vai estourar o prazo: falta 1 min dos {prazo}, depois eu corto e aviso"));
            });
            // snapshot of the windows before; o que aparecer durante a tarefa de tela entra no registro de abertos (quem abriu, fecha)
            var fotoAntes = mouse ? OpenedWindows.Foto() : null;
            try { result = (await ClaudeCli.RunAgentAsync(HandsPrompt, t, ProjectScanner.Root, CancellationToken.None, model, maxMinutes: prazo, semMouse: !mouse, semMcp: imagem is not null || semTela)).Text; }
            catch (OperationCanceledException) { result = $"A mao grande falhou: estourou o prazo de {prazo} min e eu cortei. Proponha dividir em passos menores ou usar uma mao rapida (obs, abrir_app, musica, terminais)."; ok = false; }
            catch (Exception ex) { result = "A mao grande falhou: " + ex.Message; ok = false; }
            finally
            {
                if (fotoAntes is not null) try { OpenedWindows.RegistrarNovas(fotoAntes, "fazer", recibo?.Rid ?? ""); } catch (Exception) { }
                fim.Cancel(); EmCurso.TryRemove(slot, out _); if (mouse) MouseGate.Release(); FazerGate.Release(); Interlocked.Decrement(ref _fila);
            }
            var dur = (int)(DateTime.Now - t0).TotalSeconds;
            var alegado = result;
            bool? fiscalOk = null; string? fiscalEv = null;
            var erroExecutor = !ok || string.IsNullOrWhiteSpace(result) || result.StartsWith("A mao grande falhou");
            // Fiscal: confere antes de o Odin abrir a boca.
            if (!erroExecutor)
                (fiscalOk, fiscalEv) = await FiscalizarAsync(t, result.Length > 600 ? result[..600] : result, semMcp: imagem is not null || semTela);
            // screenshot of the user's screen is user data: deleted as soon as executor and fiscal have read it
            if (imagem is not null) try { File.Delete(imagem); } catch (Exception) { }
            // A fala sai do MESMO veredito do recibo (diagnosis: 23 of 24 spoken "VERIFICADO" were
            // inconclusivo no recibo; o Odin dizia "verificado e pronto" com revisao de LLM). Tres niveis:
            // comprovado (arquivo relido no disco) / revisado por IA (fiscal concordou, sem prova) / nao confirmado.
            string veredito, motivo;
            if (recibo is not null)
                (veredito, motivo) = HandLog.Longa(recibo.Rid, recibo.Mao, recibo.Origem, recibo.Args, recibo.Inicio, alegado ?? "", fiscalOk, fiscalEv, erroExecutor);
            else (_, veredito, motivo) = HandLog.AvaliarLonga(alegado ?? "", fiscalOk, fiscalEv, new DateTimeOffset(t0), erroExecutor);
            if (erroExecutor) { ok = false; result = $"✖ FALHOU ({dur}s): {(string.IsNullOrWhiteSpace(result) ? "sem resposta (tempo estourado ou processo morto)" : result)}"; }
            else if (fiscalOk == false) { ok = false; result = $"✖ NAO CONFIRMADO ({dur}s). O executor alegou: \"{(result.Length > 240 ? result[..240] : result).Replace('\n', ' ')}\" mas o fiscal viu: {fiscalEv}. {(mouse ? "Diga a ele que NAO foi feito." : "Diga a ele que o resultado NAO foi confirmado e o que o fiscal viu (a tarefa rodou; o que falta e a confirmacao).")}"; }
            else if (veredito == "falhou") { ok = false; result = $"✖ NAO CONFIRMADO ({dur}s): {motivo}. Diga que NAO foi confirmado.\nExecutor alegou: {result}"; }
            else if (veredito == "confirmado") result = $"✔ COMPROVADO ({dur}s): {motivo}. Fiscal: {fiscalEv}\nExecutor: {result}";
            else if (fiscalOk == true) result = $"◐ REVISADO POR IA ({dur}s), sem prova conferida: {fiscalEv}\nExecutor: {result}\nDiga que fez e que a revisao por IA concordou; NAO diga 'verificado' nem 'comprovado'.";
            else result = $"? NAO CONFIRMADO ({dur}s): {fiscalEv}\nExecutor (sem confirmacao, diga isso): {result}";
            if (recibo is not null) result += $" (request_id={recibo.Rid})";
            try
            {
                Directory.CreateDirectory(TasksDir);
                File.AppendAllText(Path.Combine(TasksDir, DateTime.Now.ToString("yyyy-MM-dd") + ".md"),
                    $"\n## {DateTime.Now:HH:mm} · {t}\n{result}\n", new UTF8Encoding(false));
            }
            catch (Exception) { }
            TaskFinished?.Invoke(this, (t, result, ok));
        });
        return pos > MAOS ? $"Na fila (posicao {pos - MAOS}, as {MAOS} maos ocupadas): {t}. Aviso quando terminar."
             : mouse && pos > 1 ? $"Comecei (mao {pos} de {MAOS}; se outra estiver usando o mouse, espera ela): {t}. Aviso quando terminar."
             : $"Comecei (mao {pos} de {MAOS}): {t}. Roda em segundo plano; aviso quando terminar.";
    }

    // ---------- sessoes: orchestrate the Claude terminals so they coordinate ----------
    // Abrir gasta assinatura e tela: so com confirmado=true (o usuario disse sim em voz alta), teto 8.
    private const int TetoSessoes = 8;

    private string Sessoes(string acao, string projetos, string tarefa, bool confirmado)
    {
        var abertos = WindowsHands.ListTerminals();
        if (CommandLine.Fold(acao ?? "") is "" or "listar")
            return $"{abertos.Count} terminais abertos (teto {TetoSessoes}): " + string.Join(" | ", abertos.Select((w, i) => $"{i + 1}. {(w.Title.Length > 45 ? w.Title[..45] : w.Title)}"));
        if (CommandLine.Fold(acao ?? "") is "entregas" or "prova" or "relatorio" or "resultado") return Entregas();
        if (CommandLine.Fold(acao ?? "") != "abrir") return $"Acao '{acao}' desconhecida. Aceito: listar, abrir, entregas.";

        var nomes = (projetos ?? "").Split(',', ';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (nomes.Count == 0) return "Diga em quais projetos abrir (ex.: projetos=\"site, api\").";
        var achados = nomes.Select(n => (Nome: n, P: Achar(n))).ToList();
        var faltam = achados.Where(a => a.P is null).Select(a => a.Nome).ToList();
        if (faltam.Count > 0) return $"Nao achei no catalogo: {string.Join(", ", faltam)}. Peca o nome de novo.";
        var livres = TetoSessoes - abertos.Count;
        if (livres <= 0) return $"Ja tem {abertos.Count} terminais abertos (teto {TetoSessoes}). Feche ou reuse um antes (digitar_terminal).";
        var vao = achados.Take(livres).Select(a => a.P!).ToList();
        var corte = achados.Count > livres ? $" So cabem {livres} (teto {TetoSessoes}); ficaram de fora: {string.Join(", ", achados.Skip(livres).Select(a => a.Nome))}." : "";
        var comTarefa = string.IsNullOrWhiteSpace(tarefa) ? "sessao limpa" : $"ja com a tarefa \"{(tarefa.Length > 80 ? tarefa[..80] + "…" : tarefa)}\"";
        if (!confirmado)
            return $"PROPOSTA (nada aberto ainda): abrir {vao.Count} sessao(oes) Claude em {string.Join(", ", vao.Select(p => p.Name))}, {comTarefa}.{corte} Cada uma gasta assinatura. Leia isso pra ele e so chame de novo com confirmado=true se ele disser sim.";
        var missao = LerMissao();
        // with a task, the team runs WITHOUT a window (open terminals needed approval and never closed),
        // pelo motor do fazer (claude -p em segundo plano, sem pedir aprovacao, ate MAOS em paralelo) e termina com commit.
        // Cada um avisa o Odin ao terminar (TaskFinished); "entregas" le a prova no git.
        if (!string.IsNullOrWhiteSpace(tarefa))
        {
            var time = achados.Take(MAOS).Select(a => a.P!).ToList();
            var falas = new List<string>();
            foreach (var p in time)
            {
                missao.RemoveAll(m => m.Path == p.Path);
                missao.Add(new Missao(p.Name, p.Path, Git(p.Path, "rev-parse HEAD")?.Trim() ?? "", DateTime.Now, tarefa.Trim()));
                falas.Add(Fazer(mouse: false, semTela: true, tarefa:
                    $"Trabalhe SO dentro da pasta {p.Path} (projeto {p.Name}); nao toque em nada fora dela. Tarefa: {tarefa.Trim()} "
                    + "Ao terminar, rode git add e git commit nessa pasta com mensagem curta em portugues dizendo o que entregou, e responda em 1 frase com o hash do commit."));
            }
            File.WriteAllText(MissaoFile, JsonSerializer.Serialize(missao, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            var fora = achados.Count > MAOS ? $" So {MAOS} por vez; ficaram de fora: {string.Join(", ", achados.Skip(MAOS).Select(a => a.Nome))}." : "";
            return $"Time no ar, sem janela: {time.Count} agente(s) em {string.Join(", ", time.Select(p => p.Name))}.{fora} Cada um avisa quando terminar; depois chame sessoes acao=entregas pra prova no git.\n" + string.Join("\n", falas);
        }
        foreach (var p in vao)
        {
            // AI team: guarda o HEAD de partida; "entregas" mede o que mudou desde aqui (commit/diff = prova)
            missao.RemoveAll(m => m.Path == p.Path);
            missao.Add(new Missao(p.Name, p.Path, Git(p.Path, "rev-parse HEAD")?.Trim() ?? "", DateTime.Now, tarefa?.Trim() ?? ""));
            // prova = commit: sem commit, "entregas" so ve arquivo sujo. Edicao e git add/commit liberados pra nao travar pedindo permissao.
            if (string.IsNullOrWhiteSpace(tarefa)) _launcher.OpenClaude(p.Path);
            else _launcher.OpenClaudeWithPrompt(p.Path, tarefa.Trim() + " Ao terminar, faca git add e git commit com mensagem curta em portugues dizendo o que entregou.",
                "--permission-mode acceptEdits --allowedTools 'Bash(git add:*)' 'Bash(git commit:*)' 'Bash(git status:*)'");
        }
        File.WriteAllText(MissaoFile, JsonSerializer.Serialize(missao, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        // prova: conta as janelas de terminal de novo depois de abrir
        Thread.Sleep(2500);
        var depois = WindowsHands.ListTerminals().Count;
        return $"Feito: pedi {vao.Count} sessao(oes) em {string.Join(", ", vao.Select(p => p.Name))}, {comTarefa}.{corte} Terminais: {abertos.Count} antes, {depois} agora.";
    }

    // ---------- roteiro: narrates a video script block by block, from a notes file *video-<name>*.md ----------
    private static string Roteiro(string nome, string bloco)
    {
        var arq = Directory.GetFiles(NotesDir, $"*video-{(string.IsNullOrWhiteSpace(nome) ? "" : CommandLine.Fold(nome).Trim())}*.md").OrderByDescending(f => f).FirstOrDefault();
        if (arq is null) return "Nao achei o roteiro em .factory\\odin (arquivo *video-<nome>*.md).";
        // linhas da tabela: | n | tela | "fala" |
        var falas = File.ReadAllLines(arq).Select(l => Regex.Match(l, @"^\|\s*(\d+)\s*\|.*\|\s*(?:\*\*)?""(.+?)""(?:\*\*)?\s*\|\s*$")).Where(m => m.Success)
            .Select(m => m.Groups[2].Value).ToList();
        if (falas.Count == 0) return "O roteiro nao tem a tabela de falas (| n | tela | \"fala\" |).";
        var n = int.TryParse(bloco, out var b) ? b : 1;
        if (n > falas.Count) return $"FIM: o roteiro tem {falas.Count} blocos e ja acabou. Fique em silencio.";
        n = Math.Max(1, n);
        return $"FALA (bloco {n} de {falas.Count}{(n == falas.Count ? ", o ULTIMO: com enfase" : "")}): {falas[n - 1]}";
    }

    // ---------- time de IA: o que cada sessao aberta pelo Odin entregou, com prova do git ----------
    private sealed record Missao(string Nome, string Path, string Head, DateTime Inicio, string Tarefa);
    private static readonly string MissaoFile = System.IO.Path.Combine(NotesDir, "time-ia.json");
    private static List<Missao> LerMissao()
    {
        Directory.CreateDirectory(NotesDir);
        try { return File.Exists(MissaoFile) ? JsonSerializer.Deserialize<List<Missao>>(File.ReadAllText(MissaoFile)) ?? [] : []; }
        catch (Exception) { return []; }
    }

    private static string? Git(string dir, string args)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", $"-C \"{dir}\" {args}")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 })!;
            var o = p.StandardOutput.ReadToEnd();
            return p.WaitForExit(8000) && p.ExitCode == 0 ? o : null;
        }
        catch (Exception) { return null; }
    }

    private static string Entregas()
    {
        var missao = LerMissao().Where(m => m.Inicio > DateTime.Now.AddDays(-2)).ToList();
        if (missao.Count == 0) return "Nenhuma sessao aberta por mim nas ultimas 48 h. Abra com sessoes acao=abrir.";
        var sb = new StringBuilder($"Entregas de {missao.Count} sessao(oes), medidas no git agora:\n");
        var recibo = new StringBuilder($"# Time de IA — entregas {DateTime.Now:yyyy-MM-dd HH:mm}\n\n");
        foreach (var m in missao)
        {
            var desde = $"{(int)(DateTime.Now - m.Inicio).TotalMinutes} min atras";
            if (Git(m.Path, "rev-parse HEAD") is null) { sb.AppendLine($"- {m.Nome}: sem git, nao tenho prova."); continue; }
            var commits = string.IsNullOrEmpty(m.Head) ? [] : (Git(m.Path, $"log --format=%h-%s {m.Head}..HEAD") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var stat = (string.IsNullOrEmpty(m.Head) ? "" : Git(m.Path, $"diff --shortstat {m.Head}") ?? "").Trim();
            var sujos = (Git(m.Path, "status --porcelain") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            var linha = commits.Length == 0 && sujos == 0 ? "nada mudou ainda"
                : $"{commits.Length} commit(s){(commits.Length > 0 ? " (" + string.Join("; ", commits.Take(3).Select(c => c.Length > 60 ? c[..60] : c)) + ")" : "")}, {sujos} arquivo(s) sem commit{(stat.Length > 0 ? ", " + stat : "")}";
            sb.AppendLine($"- {m.Nome} (aberto {desde}): {linha}.");
            recibo.AppendLine($"## {m.Nome}\n- tarefa: {(m.Tarefa.Length > 0 ? m.Tarefa : "(sessao limpa)")}\n- HEAD de partida: `{m.Head}`\n- {linha}\n");
            foreach (var c in commits) recibo.AppendLine($"  - `{c}`");
        }
        var f = System.IO.Path.Combine(NotesDir, "tarefas", $"{DateTime.Now:yyyy-MM-dd-HHmm}-time-ia.md");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)!);
        File.WriteAllText(f, recibo.ToString(), new UTF8Encoding(false));
        return sb.Append($"Recibo: .factory\\odin\\tarefas\\{System.IO.Path.GetFileName(f)}. Conte a ele so o que o git mostra; 'nada mudou' = ainda nao entregou.").ToString();
    }

    // ---------- janela: qualquer janela, na hora (Win32, sem Claude Code) ----------
    // English Windows: spoken pt name -> title fragment ("bloco de notas" vs "Notepad")
    private static readonly Dictionary<string, string> TituloApelido = new()
    {
        ["bloco de notas"] = "notepad", ["bloco"] = "notepad", ["notas"] = "notepad",
        ["explorador"] = "explor", ["explorador de arquivos"] = "explor", ["pasta"] = "explor", ["arquivos"] = "explor",
        ["navegador"] = "chrome", ["google"] = "chrome", ["configuracoes"] = "settings", ["ajustes"] = "settings", ["calculadora"] = "calculator",
    };
    private static string Janela(string nome, string acao, string monitor, string alvoFechar = "", string salvar = "", string forcar = "")
    {
        var all = WindowsHands.ListWindows();
        var q = CommandLine.Fold(nome ?? "");
        if (CommandLine.Fold(acao ?? "") is "fechar" or "fecha" or "close") return Fechar(all, alvoFechar is { Length: > 0 } ? alvoFechar : nome, salvar, forcar);
        var nossos = OpenedWindows.Vivos().Select(i => i.Hwnd).ToHashSet();
        if (q is "" or "listar" || CommandLine.Fold(acao ?? "") == "listar")
            return $"Janelas abertas ({WindowsHands.MonitorCount} monitores): " + string.Join(" | ", all.Take(20).Select((w, i) => $"{i + 1}. {(w.Title.Length > 50 ? w.Title[..50] : w.Title)}{(nossos.Contains(w.Handle.ToInt64()) ? " [aberta por mim]" : "")}"));
        // "odin", "factory", "voce", "esta": a propria janela do Factory
        // "odin"/"voce": a janela deste processo (titulo varia: "ODIN · HOMOLOG", "FACTORY"...)
        if (q is "odin" or "factory" or "voce" or "esta" or "essa" or "a sua" or "tua" or "sua")
        {
            var eu = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            var minha = all.FirstOrDefault(w => w.Handle == eu);
            if (minha.Handle != IntPtr.Zero) all = [minha, .. all.Where(w => w.Handle != eu)];
            q = minha.Handle != IntPtr.Zero ? CommandLine.Fold(minha.Title) : "odin";
        }
        // English Windows: the spoken pt name does not match the title ("bloco de notas" vs "Notepad")
        if (TituloApelido.TryGetValue(q, out var titulo0)) q = titulo0;
        var alvo = int.TryParse(q, out var n) && n >= 1 && n <= all.Count ? all[n - 1]
            : all.FirstOrDefault(w => CommandLine.Fold(w.Title).Contains(q)) is { Handle: var h } hit && h != IntPtr.Zero ? hit
            : all.Select(w => (w, s: PaletteMatcher.Score(q, w.Title, ""))).Where(x => x.s > 60).OrderByDescending(x => x.s).Select(x => x.w).FirstOrDefault();
        // "minimiza o Spotify" falhava porque o titulo do Spotify e a faixa tocando. Tenta pelo nome do programa.
        if (alvo.Handle == IntPtr.Zero && q.Length >= 3)
        {
            var hp = System.Diagnostics.Process.GetProcesses().Where(p => CommandLine.Fold(p.ProcessName).Contains(q.Replace(" ", "")))
                .Select(p => p.MainWindowHandle).FirstOrDefault(x => x != IntPtr.Zero && all.Any(w => w.Handle == x));
            if (hp != IntPtr.Zero) alvo = all.First(w => w.Handle == hp);
        }
        if (alvo.Handle == IntPtr.Zero) return $"Nao achei janela com '{nome}'. Abertas: {string.Join(" | ", all.Take(10).Select(w => w.Title.Length > 40 ? w.Title[..40] : w.Title))}";
        var ac = string.IsNullOrWhiteSpace(acao) ? "frente" : acao;
        var r = WindowsHands.Arrange(alvo.Handle, ac, monitor);
        var titulo = alvo.Title.Length > 50 ? alvo.Title[..50] : alvo.Title;
        return r.StartsWith("o Windows") || r.Contains("nao existe") || r.Contains("desconhecida")
            ? $"Nao consegui: '{titulo}' {r}."
            : $"Feito: '{titulo}' {r} (lido de volta do Windows).\n{WindowsHands.Prova(alvo.Handle, ac)}";
    }

    // ---------- fechar: only what the hands opened (`fazer` once closed the user's own Notepads) ----------

    // Dialogo "Salvar?" do Notepad do Win11 e conteudo DENTRO da janela (nao janela propria): acha pelo UIA so naquela janela.
    private const string ScriptSalvar = """
param([long]$hwnd, [string]$acao)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
try { $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$hwnd) } catch { 'SUMIU'; exit }
$botao = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$nao = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $botao) | Where-Object { $_.Current.Name -match "^(Don.t save|N.o salvar|Nao salvar)$" } | Select-Object -First 1
if (-not $nao) { 'NADA'; exit }
if ($acao -ne 'naosalvar') { 'DIALOGO'; exit }
$nao.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); 'CLICOU'
""";

    private static string DialogoSalvar(IntPtr h, bool naoSalvar)
    {
        var ps1 = Path.Combine(AppPaths.Cache, "janela-salvar.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(ps1)!);
        if (!File.Exists(ps1) || File.ReadAllText(ps1) != ScriptSalvar) File.WriteAllText(ps1, ScriptSalvar, new System.Text.UTF8Encoding(true));
        var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ps1}\" -hwnd {h.ToInt64()} -acao {(naoSalvar ? "naosalvar" : "ver")}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var saida = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(8000);
        return saida.Split('\n').LastOrDefault()?.Trim() ?? "";
    }

    private static string Fechar(List<(IntPtr Handle, string Title)> all, string alvo, string salvar, string forcar)
    {
        var nossos = OpenedWindows.Vivos();
        var a = CommandLine.Fold(alvo ?? "");
        var forca = CommandLine.Fold(forcar ?? "") is "true" or "sim" or "1";
        var naoSalvar = CommandLine.Fold(salvar ?? "") is "nao" or "false" or "no" or "dont save" or "sem salvar";
        var recusadas = new List<string>();
        List<(IntPtr Handle, string Title)> alvos;

        if (a is "" or "abertos" or "aberta" or "os que abri" or "tudo que abri" or "minhas" or "todas" || a.Contains("abri") || a.Contains("abert"))
            alvos = [.. all.Where(w => nossos.Any(i => i.Hwnd == w.Handle.ToInt64()))];
        else if (a.StartsWith("r-"))
            alvos = [.. all.Where(w => nossos.Any(i => i.Hwnd == w.Handle.ToInt64() && CommandLine.Fold(i.RequestId) == a))];
        else if (a.Split(',', ' ', ';').Where(x => x.Length > 0).All(x => int.TryParse(x, out _)))
        {
            alvos = [];
            foreach (var x in a.Split(',', ' ', ';').Where(x => x.Length > 0))
            {
                var n = int.Parse(x);
                if (n < 1 || n > all.Count) { recusadas.Add($"#{n} (não existe na lista)"); continue; }
                var w = all[n - 1];
                if (OpenedWindows.EhProtegida(w.Handle)) { recusadas.Add($"#{n} (protegida: Factory/terminal)"); continue; }
                if (!forca && !nossos.Any(i => i.Hwnd == w.Handle.ToInt64())) { recusadas.Add($"#{n} '{(w.Title.Length > 30 ? w.Title[..30] : w.Title)}' (não fui eu que abri)"); continue; }
                alvos.Add(w);
            }
        }
        else
            // por nome: SO entre as nossas (nunca fecha janela do usuario pelo titulo)
            alvos = [.. all.Where(w => nossos.Any(i => i.Hwnd == w.Handle.ToInt64() && (CommandLine.Fold(i.Processo).Contains(a.Replace(" ", "")) || CommandLine.Fold(w.Title).Contains(a))))];

        if (alvos.Count == 0)
            return recusadas.Count > 0
                ? $"Nao fechei nada: {string.Join("; ", recusadas)}. Janela que eu não abri só fecho se ele confirmar o número em voz (chame de novo com forcar=true)."
                : $"Nao fechei nada: não há janela aberta por mim{(a.Length > 0 ? $" com '{alvo}'" : "")}. As suas janelas eu não fecho sem você dizer o número.";

        var fechadas = new List<string>(); var pedindoSalvar = new List<string>(); var falhou = new List<string>();
        foreach (var w in alvos)
        {
            var nome = w.Title.Length > 30 ? w.Title[..30] : w.Title;
            WindowsHands.Close(w.Handle);
            var foi = false;
            for (var i = 0; i < 10 && !foi; i++) { Thread.Sleep(200); foi = !WindowsHands.Existe(w.Handle); }
            if (!foi)
            {
                var d = DialogoSalvar(w.Handle, naoSalvar);
                if (d == "CLICOU") for (var i = 0; i < 10 && !foi; i++) { Thread.Sleep(200); foi = !WindowsHands.Existe(w.Handle); }
                else if (d == "DIALOGO") { pedindoSalvar.Add(nome); continue; }
            }
            (foi ? fechadas : falhou).Add(nome);
        }
        // prova: relê o Windows; quantas janelas do mesmo programa continuam (as do usuario, intocadas)
        var depois = WindowsHands.ListWindows();
        var procs = alvos.Select(w => WindowsHands.Pid(w.Handle)).ToHashSet();
        var intocadas = depois.Count(w => procs.Contains(WindowsHands.Pid(w.Handle)) && !alvos.Any(x => x.Handle == w.Handle));
        var txt = $"Fechei {fechadas.Count} de {alvos.Count} janela(s) que eu abri";
        if (pedindoSalvar.Count > 0) txt += $"; {pedindoSalvar.Count} pediram pra salvar e DEIXEI ABERTAS ({string.Join(", ", pedindoSalvar)}) — se ele disser 'não salva', chame de novo com salvar=nao";
        if (falhou.Count > 0) txt += $"; {falhou.Count} não fecharam ({string.Join(", ", falhou)})";
        if (recusadas.Count > 0) txt += $"; recusei {string.Join("; ", recusadas)}";
        txt += $". Não toquei em nenhuma outra janela{(intocadas > 0 ? $" ({intocadas} do mesmo programa continuam abertas, são suas)" : "")}.";
        var prova = JsonSerializer.Serialize(new { mao = "janela", acao = "fechar", coerente = falhou.Count == 0, alvos = alvos.Count, fechadas = fechadas.Count, pedindo_salvar = pedindoSalvar.Count, recusadas = recusadas.Count, fonte = "registro de abertos + Windows relido", lido_em = DateTimeOffset.Now.ToString("o") });
        return (fechadas.Count > 0 ? txt : "Nao consegui: " + txt) + "\n[prova] " + prova;
    }

    // ---------- roteador: frase inteira = comando conhecido -> mao rapida ----------
    // Conservador de proposito: ancorado no inicio e no fim, frase curta, sem "e depois". Na duvida devolve null
    // e o `fazer` segue normal. Cada regra aqui e uma tarefa que deixou de levar minutos.
    private static readonly Regex RtAbrir = new(@"^(abr[ae]|abrir|inicia|iniciar|liga|ligar)( o| a)? (?<app>[a-z0-9 ]{2,24})$");
    private static readonly Regex RtCena = new(@"^(no obs,? )?(troca|trocar|troque|muda|mudar|mude|bota|coloca|vai)( pra| para| a)?( a)? cena( do obs)?( pra| para)? (?<cena>.{2,40}?)( no obs)?$");
    private static readonly Regex RtGravar = new(@"^(no obs,? )?(comeca|comecar|inicia|iniciar|liga|ligar|bota)( a)? (gravar|gravacao|a gravacao)( no obs)?$");
    private static readonly Regex RtParar = new(@"^(no obs,? )?(para|parar|pare|encerra|encerrar|desliga|desligar)( a)? (gravar|gravacao|a gravacao)( no obs)?$");
    private static readonly Regex RtMusica = new(@"^(?<a>pausa|pausar|pause|toca|tocar|play|proxima|proxima musica|pula|pular|anterior|volta a musica)( a musica| o spotify| no spotify)?$");
    private static readonly Regex RtVolume = new(@"^(no spotify,? )?(?<d>aumenta|aumentar|sobe|subir|abaixa|abaixar|baixa|baixar|diminui|diminuir|desce|descer|coloca|bota|poe)?( o)? volume( interno)?( do spotify| no spotify)?( (pra|para|em) (?<n>\d{1,3})( ?%| por cento)?)?( no spotify)?$");
    private static readonly Regex RtTocarNome = new(@"^(no spotify,? )?(toca|tocar|toque|bota|botar|coloca|colocar|poe)( a| o| as| os| uma| um)?( (?<tipo>musica|banda|playlist|album|som))?( do| da| de)? (?<t>[a-z0-9][a-z0-9 '&.-]{1,48}?)( no spotify| pra mim| ai)?$");
    private static readonly Regex RtOrganizar = new(@"^(organiza|organizar|organize|arruma|arrumar|arrume|alinha|alinhar)( os| as)?( \w+)? (terminais|terminal|janelas dos terminais)( lado a lado| em colunas)?( no monitor \d)?$");

    private static async Task<string?> Rotear(string tarefa, CancellationToken ct)
    {
        var q = CommandLine.Fold((tarefa ?? "").Trim());
        if (q.Length == 0 || q.Length > 70 || q.Contains(" e depois") || q.Contains(" e entra") || q.Contains(',') && !q.StartsWith("no obs")) return null;
        Match m;
        if ((m = RtCena.Match(q)).Success) return "Feito na hora (roteador, sem Claude Code): " + await ObsHands.RunAsync("cena", m.Groups["cena"].Value, ct);
        if (RtGravar.IsMatch(q)) return "Feito na hora (roteador): " + await ObsHands.RunAsync("gravar", "", ct);
        if (RtParar.IsMatch(q)) return "Feito na hora (roteador): " + await ObsHands.RunAsync("parar", "", ct);
        if ((m = RtMusica.Match(q)).Success)
        {
            var a = m.Groups["a"].Value;
            return "Feito na hora (roteador): " + await SpotifyHands.Controlar(a.StartsWith("pro") || a.StartsWith("pul") ? "proxima" : a.StartsWith("ant") || a.StartsWith("volta") ? "anterior" : a.StartsWith("paus") ? "pausar" : "tocar");
        }
        // os pedidos que mais caiam no `fazer` (2,5 min cada) eram Spotify e terminais
        if ((m = RtVolume.Match(q)).Success && (m.Groups["n"].Success || m.Groups["d"].Value is "aumenta" or "aumentar" or "sobe" or "subir" or "abaixa" or "abaixar" or "baixa" or "baixar" or "diminui" or "diminuir" or "desce" or "descer"))
            return "Feito na hora (roteador): " + (m.Groups["n"].Success ? await SpotifyHands.Volume(null, Math.Clamp(int.Parse(m.Groups["n"].Value), 0, 100))
                : await SpotifyHands.Volume(m.Groups["d"].Value is "aumenta" or "aumentar" or "sobe" or "subir" ? +15 : -15, null));
        if ((m = RtTocarNome.Match(q)).Success) return "Feito na hora (roteador): " + await SpotifyHands.TocarPorNome(m.Groups["t"].Value, m.Groups["tipo"].Value);
        if (RtOrganizar.IsMatch(q)) return "Feito na hora (roteador): " + OrganizarTerminais("");
        if ((m = RtAbrir.Match(q)).Success && Apps.ContainsKey(m.Groups["app"].Value.Trim()))
            return "Feito na hora (roteador): " + AbrirApp(m.Groups["app"].Value.Trim());
        if (m.Success && m.Groups["app"].Value.Trim() is "obs" or "o obs" or "obs studio")
            return "Feito na hora (roteador): " + await ObsHands.RunAsync("abrir", "", ct);
        return null;
    }

    // ---------- terminais (Windows Terminal / consoles), sem windows-mcp ----------

    private static string Terminais()
    {
        var all = WindowsHands.ListTerminals();
        if (all.Count == 0) return "Nenhum terminal aberto agora.";
        return string.Join("\n", all.Select((w, i) => $"{i + 1}. {(w.Title.Length > 0 ? w.Title : "(sem titulo)")}"));
    }

    // "organize os tres terminais" foi 3x pro `fazer` (minutos, windows-mcp). Agora Win32 direto, com prova.
    private static string OrganizarTerminais(string monitor)
    {
        var all = WindowsHands.ListTerminals();
        if (all.Count == 0) return "Nao consegui: nenhum terminal aberto pra organizar.";
        var (r, noLugar) = WindowsHands.LadoALado(all.Select(w => w.Handle).ToList(), monitor);
        if (r.StartsWith("monitor")) return "Nao consegui: " + r;
        return noLugar == all.Count
            ? $"Feito: {all.Count} terminais lado a lado {r} (lido de volta do Windows).\n[prova] " +JsonSerializer.Serialize(new { mao = "terminais", acao = "organizar", coerente = true, terminais = all.Count, no_lugar = noLugar, onde = r, titulos = all.Select(w => w.Title).ToArray(), lido_em = DateTimeOffset.Now.ToString("o") })
            : $"Nao consegui: so {noLugar} de {all.Count} terminais ficaram no lugar.";
    }

    private static string ComTerminal(string nome, Func<TerminalWindow, string> acao)
    {
        var w = WindowsHands.Find(nome);
        if (w is null)
        {
            var all = WindowsHands.ListTerminals();
            return all.Count == 0 ? "Nenhum terminal aberto." : $"Nao achei terminal parecido com '{nome}'. OpenedWindows: {string.Join(" · ", all.Select(x => x.Title))}";
        }
        return acao(w);
    }

    private static string Digitar(TerminalWindow w, string texto, bool enter)
    {
        if (string.IsNullOrEmpty(texto)) return "Texto vazio, nao digitei.";
        if (!WindowsHands.Focus(w.Handle)) return $"Nao consegui focar '{w.Title}'; nao digitei pra nao cair em outra janela.";
        Thread.Sleep(150);
        WindowsHands.Type(texto, enter);
        var resumo = texto.Length > 60 ? texto[..60] + "…" : texto;
        return enter ? $"Digitei e executei em '{w.Title}': {resumo}" : $"Digitei em '{w.Title}', sem Enter: {resumo}. Ele confere e aperta.";
    }

    private static string ReportarFalha(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "Sem texto.";
        try
        {
            Directory.CreateDirectory(NotesDir);
            File.AppendAllText(Path.Combine(NotesDir, "falhas.md"), $"- {DateTime.Now:yyyy-MM-dd HH:mm} · {texto.Trim()}\n", new UTF8Encoding(false));
        }
        catch (Exception ex) { return "Nao consegui registrar: " + ex.Message; }
        return "Registrado em .factory\\odin\\falhas.md.";
    }

    // Factory panel: how the user is doing (state declared by voice).
    // Nao escreve nada: so avisa a UI. O Odin chama no comeco da conversa e quando o humor muda.
    private string Estado(JsonElement args)
    {
        var caos = 50;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("caos", out var c))
        {
            if (c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n)) caos = n;
            else if (c.ValueKind == JsonValueKind.String && int.TryParse(c.GetString(), out var m)) caos = m;
        }
        caos = Math.Clamp(caos, 0, 100);
        var humor = Str(args, "humor").Trim();
        var recado = Str(args, "recado").Trim();
        StateDeclared?.Invoke(this, (caos, humor.Length > 0 ? humor : "sem palavras", recado));
        return "Anotado no painel.";
    }

    // ---------- ACORDAR: human alarm, Odin keeps talking until the user answers ----------
    public static readonly string AlarmFile = Path.Combine(AppPaths.Cache, "alarme.json");

    private string Alarme(string hora, string motivo, string dor, string sono)
    {
        // hora: "07:30" (hoje se ainda nao passou, senao amanha) ou "2026-09-27 07:30"
        hora = hora.Trim();
        DateTime quando;
        if (DateTime.TryParse(hora, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.AssumeLocal, out var full) && hora.Length > 5) quando = full;
        else if (TimeSpan.TryParse(hora, out var ts)) { quando = DateTime.Today + ts; if (quando <= DateTime.Now) quando = quando.AddDays(1); }
        else return $"Nao entendi a hora '{hora}'. Diga como 07:30 ou 2026-09-27 07:30.";
        var obj = new System.Text.Json.Nodes.JsonObject
        {
            ["quando"] = quando.ToString("yyyy-MM-ddTHH:mm:ss"), ["motivo"] = motivo.Trim(), ["dor"] = dor.Trim(), ["sono"] = sono.Trim(),
            ["ativo"] = true, ["disparado"] = false, ["criado"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        try { Directory.CreateDirectory(Path.GetDirectoryName(AlarmFile)!); File.WriteAllText(AlarmFile, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"); }
        catch (Exception ex) { return "Nao consegui gravar o alarme: " + ex.Message; }
        AlarmChanged?.Invoke(this, EventArgs.Empty);
        var em = quando - DateTime.Now;
        return $"Alarme marcado pra {quando:ddd dd/MM HH:mm} (daqui a {(int)em.TotalHours}h{em.Minutes:00}). Vou chamar pelo nome, subir o volume e insistir ate ele responder com cabeca; a camera vigia movimento. O Factory precisa ficar aberto.";
    }

    private string CancelarAlarme()
    {
        try { if (File.Exists(AlarmFile)) File.Delete(AlarmFile); } catch (Exception ex) { return "Nao consegui cancelar: " + ex.Message; }
        AlarmChanged?.Invoke(this, EventArgs.Empty);
        return "Alarme cancelado.";
    }

    private string Acordado()
    {
        try
        {
            if (File.Exists(AlarmFile))
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(AlarmFile)) as System.Text.Json.Nodes.JsonObject;
                if (node is not null) { node["ativo"] = false; node["acordou"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); File.WriteAllText(AlarmFile, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"); }
            }
        }
        catch (Exception) { }
        WokeUp?.Invoke(this, EventArgs.Empty);
        return "Registrado: ele acordou. Alarme encerrado.";
    }

    // voice control of Odin's own volume, by voice. Mexe no mesmo
    // controle do painel (persiste em painel.json) e le de volta o valor aplicado.
    private static async Task<string> VolumeOdin(string acao, string nivel)
    {
        var vm = App.Services.GetRequiredService<ChatViewModel>();
        var tcs = new TaskCompletionSource<(double Antes, double Depois)>();
        var a = CommandLine.Fold(acao ?? "");
        App.Window?.DispatcherQueue.TryEnqueue(() =>
        {
            var antes = vm.Volume;
            var alvo = int.TryParse(nivel, out var n) ? n
                : a is "mais baixo" or "baixo" or "abaixar" or "diminuir" or "descer" ? antes - 15
                : a is "mais alto" or "alto" or "aumentar" or "subir" ? antes + 15
                : a is "sussurro" or "minimo" ? 5 : antes;
            vm.Volume = Math.Clamp(alvo, 0, 100);
            tcs.TrySetResult((antes, vm.Volume));
        });
        var feito = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        if (feito != tcs.Task) return "Nao consegui: a janela do Factory nao respondeu pra mudar o volume.";
        var (de, para) = tcs.Task.Result;
        if (Math.Abs(de - para) < 0.5) return $"Minha voz ja esta em {para:0}%{(para <= 0 ? " (mudo)" : para >= 100 ? " (maximo)" : "")}; nao mudou. Diga isso curto.";
        return $"Minha voz: {de:0}% -> {para:0}% (volume da tua voz no Factory, o do Windows nao mexeu).\n[prova] "
            + JsonSerializer.Serialize(new { mao = "volume_odin", acao = a, coerente = true, antes = de, depois = para, lido_em = DateTimeOffset.Now.ToString("o") });
    }

    private const bool DesenharLigado = true;

    // Desenho DETERMINISTICO no Paint (mouse de verdade, sem LLM): abre o Paint se preciso, espera a janela e traca.
    // com o mouse de verdade o Paint engolia parte do arrasto (2-3 de 5 inteiros) e, com ele mexendo no
    // mouse ao mesmo tempo, saia rabisco. Agora o desenho e feito em imagem (System.Drawing, ~1 s, sem tocar no mouse
    // do usuario) e aberto no Paint. Prova: arquivo gravado agora + janela do Paint com o nome do arquivo.
    private static string Desenhar(string forma, string tamanho, string modo = "", string quantidade = "")
    {
        var f = CommandLine.Fold(forma is { Length: > 0 } ? forma : "circulo");
        // catalogo fixo de desenhos prontos em Assets\drawings.ps1
        var tipo = f.Contains("pac") || f.Contains("come come") || f.Contains("comecome") ? "pacman"
            : f.Contains("pika") || f.Contains("pica") || f.Contains("pokemon") ? "pikachu"
            : f.Contains("estrel") || f.Contains("star") ? "estrela"
            : f.Contains("cora") || f.Contains("heart") || f.Contains("amor") ? "coracao"
            : f.Contains("sol") || f.Contains("sun") ? "sol"
            : f.Contains("lua") || f.Contains("moon") ? "lua"
            : f.Contains("foguet") || f.Contains("nave") || f.Contains("rocket") ? "foguete"
            : f.Contains("sonic") ? "sonic"
            : f.Contains("buraco") || f.Contains("black hole") ? "buraconegro"
            : f.Contains("circ") || f.Contains("bola") || f.Contains("redond") || f.Contains("zero") ? "circulo"
            : "";
        if (tipo.Length == 0) return $"Nao consegui: '{forma}' nao esta na minha lista. Sei desenhar: Pikachu, Sonic, Pac-Man, buraco negro, foguete, estrela, coracao, sol, lua e circulo. Ofereca essas a ele.";
        var raio = Math.Clamp(int.TryParse(tamanho, out var t) ? t : 150, 30, 400);
        var dir = Path.Combine(NotesDir, "desenhos");
        Directory.CreateDirectory(dir);
        var nome = $"{DateTime.Now:yyyy-MM-dd-HHmmss}-{tipo}.png";
        var arq = Path.Combine(dir, nome);
        // default is LIVE, stroke by stroke with the mouse in
        // Paint, so the user can watch it draw. modo=imagem e a versao pronta (garantida, colorida).
        if (CommandLine.Fold(modo ?? "") is not ("imagem" or "pronta" or "pronto"))
        {
            var hp = WindowsHands.FindByTitle("Paint");
            if (hp == IntPtr.Zero)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mspaint") { UseShellExecute = true }); } catch (Exception ex) { return "Nao consegui abrir o Paint: " + ex.Message; }
                for (var i = 0; i < 40 && hp == IntPtr.Zero; i++) { Thread.Sleep(250); hp = WindowsHands.FindByTitle("Paint"); }
                if (hp == IntPtr.Zero) return "Nao consegui: abri o Paint mas a janela nao apareceu em 10 s. Diga a ele que nao desenhei.";
                OpenedWindows.Registrar(hp, "desenhar");
                Thread.Sleep(1500);   // o Paint novo demora a montar a tela
            }
            // N desenhos = UMA chamada com quantidade (grade planejada na tela branca), nunca N chamadas no centro
            var (ok, texto, prova) = LiveDrawing.Compor(hp, tipo, int.TryParse(quantidade, out var q) ? q : 1, raio, dir, 15);
            if (ok) return $"Feito AO VIVO no Paint: {texto}.\n[prova] " + JsonSerializer.Serialize(prova);
            return $"Nao consegui: {texto}. Diga isso a ele em uma frase, sem dizer que desenhou o que nao saiu; ofereca tentar de novo sem mexer no mouse.";
        }
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var args = tipo switch
        {
            "pacman" => $"-ExecutionPolicy Bypass -File \"{Path.Combine(assets, "pacman-arcade.ps1")}\" -saida \"{arq}\" -r {Math.Clamp(raio, 60, 200)}",   // cena de fliperama (labirinto neon, 4 fantasmas)
            "circulo" => "-Command \"Add-Type -AssemblyName System.Drawing; "
                + $"$b = New-Object Drawing.Bitmap {raio * 3}, {raio * 3}; $g = [Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::White); "
                + $"$caneta = New-Object Drawing.Pen ([Drawing.Color]::Black), {Math.Max(3, raio / 25)}; $g.DrawEllipse($caneta, {raio / 2}, {raio / 2}, {2 * raio}, {2 * raio}); "
                + $"$b.Save('{arq.Replace("'", "''")}', [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()\"",
            _ => $"-ExecutionPolicy Bypass -File \"{Path.Combine(assets, "drawings.ps1")}\" -forma {tipo} -saida \"{arq}\" -r {raio}",
        };
        var psi = new System.Diagnostics.ProcessStartInfo("powershell", "-NoProfile -NonInteractive " + args) { UseShellExecute = false, CreateNoWindow = true };
        using (var p = System.Diagnostics.Process.Start(psi)) p?.WaitForExit(10000);
        if (!File.Exists(arq) || new FileInfo(arq).Length < 200) return "Nao consegui desenhar: a imagem nao foi gravada. Diga a ele que nao saiu.";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mspaint", $"\"{arq}\"") { UseShellExecute = true }); }
        catch (Exception ex) { return "Nao consegui abrir o Paint: " + ex.Message; }
        var janela = IntPtr.Zero;
        var semExt = Path.GetFileNameWithoutExtension(nome);
        for (var i = 0; i < 40 && janela == IntPtr.Zero; i++) { Thread.Sleep(250); janela = WindowsHands.FindByTitle(semExt); }
        if (janela == IntPtr.Zero) return $"Nao consegui confirmar: desenhei em {nome}, mas a janela do Paint com ele nao apareceu em 10 s. Diga isso a ele.";
        return $"Desenhei {tipo} e abri no Paint ({nome}, em .factory\\odin\\desenhos).\n[prova] "
            + JsonSerializer.Serialize(new { mao = "desenhar", forma = tipo, coerente = true, arquivo = arq, bytes = new FileInfo(arq).Length, janela_paint = true, lido_em = DateTimeOffset.Now.ToString("o") });
    }

    // ---------- instantaneas: musica e apps (nunca passam pelo Claude Code) ----------

    // Trust phase 1: tudo pelo Spotify direto (SpotifyHands), nunca pelo `fazer`. "toca banda X" = termo; volume =
    // so o do Spotify (the app's own volume, not the system one); "geral" so se o usuario disser.
    private static async Task<string> MusicaAsync(string acao, string termo, string nivel, string tipo = "")
    {
        var a = CommandLine.Fold(acao ?? "");
        if (termo.Trim().Length > 0 && a is not ("subir" or "descer" or "volume" or "pausar" or "proxima" or "anterior")) return await SpotifyHands.TocarPorNome(termo, tipo);
        if (int.TryParse(nivel, out var n)) return await SpotifyHands.Volume(null, Math.Clamp(n, 0, 100));
        if (a is "subir" or "aumentar" or "mais alto") return await SpotifyHands.Volume(+15, null);
        if (a is "descer" or "abaixar" or "mais baixo") return await SpotifyHands.Volume(-15, null);
        if (a is "geral_subir" or "geral_descer" or "mudo" or "mute") return await Task.Run(() => Musica(a.StartsWith("geral_s") ? "subir" : a.StartsWith("geral_d") ? "descer" : "mudo"));
        if (a is "buscar") return "Nao consegui: buscar precisa do termo (nome da musica/banda).";
        return await SpotifyHands.Controlar(a);
    }

    // Volume GERAL do Windows e mudo (tecla de midia). Play/pause/faixa NAO passam mais aqui: a tecla ia pra
    // qualquer app com midia (o proprio Odin no WebView2) e o recibo dizia "pausado" com o Spotify tocando.
    private static string Musica(string acao)
    {
        var a = CommandLine.Fold(acao);
        byte vk = a switch
        {
            "tocar" or "play" or "pausar" or "pause" or "continuar" or "parar" => 0xB3,   // VK_MEDIA_PLAY_PAUSE
            "proxima" or "next" or "pular" => 0xB0,                                          // VK_MEDIA_NEXT_TRACK
            "anterior" or "prev" or "voltar" => 0xB1,                                        // VK_MEDIA_PREV_TRACK
            "subir" or "aumentar" or "mais alto" => 0xAF,                                    // VK_VOLUME_UP
            "descer" or "abaixar" or "mais baixo" => 0xAE,                                   // VK_VOLUME_DOWN
            "mudo" or "mute" => 0xAD,                                                        // VK_VOLUME_MUTE
            _ => 0,
        };
        if (vk == 0) return $"Nao entendi a acao '{acao}'. Aceito: tocar, pausar, proxima, anterior, subir, descer, mudo.";
        if (vk == 0xB3 && !System.Diagnostics.Process.GetProcessesByName("Spotify").Any())
        {
            // Nada tocando: abre o Spotify e da play depois de subir.
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("spotify:") { UseShellExecute = true }); } catch (Exception) { }
            _ = Task.Run(async () => { await Task.Delay(4000); WindowsHands.MediaKey(0xB3); });
            return "Spotify abrindo; dou play em 4 segundos.";
        }
        var reps = vk is 0xAF or 0xAE ? 5 : 1;   // volume: 5 toques = ~10%
        var antes = SpotifyTitulo();
        for (var i = 0; i < reps; i++) WindowsHands.MediaKey(vk);
        var texto = vk switch { 0xB3 => "Feito: play/pause.", 0xB0 => "Proxima.", 0xB1 => "Anterior.", 0xAF => "Volume subiu.", 0xAE => "Volume desceu.", _ => "Mudo alternado." };
        if (antes is null || vk is 0xAF or 0xAE or 0xAD) return texto;
        // Proof: o titulo da janela do Spotify e a faixa quando toca e "Spotify ..." quando pausa.
        Thread.Sleep(900);
        var depois = SpotifyTitulo() ?? "";
        var tocando = !depois.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) && depois.Length > 0;
        if (vk is 0xB0 or 0xB1 && depois == antes)
            return $"Mandei {(vk == 0xB0 ? "proxima" : "anterior")}, mas a faixa no Spotify nao mudou ('{antes}'). Nao diga que trocou.";
        var estado = tocando ? $"tocando '{depois}'" : "pausado";
        return $"{texto} Spotify agora: {estado}.\n[prova] " + JsonSerializer.Serialize(new { mao = "musica", acao, spotify_antes = antes, spotify_depois = depois, tocando, lido_em = DateTimeOffset.Now.ToString("o") });
    }

    private static string? SpotifyTitulo() =>
        System.Diagnostics.Process.GetProcessesByName("Spotify").Select(p => p.MainWindowTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

    private static readonly Dictionary<string, string> Apps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"] = "spotify:", ["paint"] = "mspaint", ["mspaint"] = "mspaint", ["pincel"] = "mspaint", ["notepad"] = "notepad", ["bloco"] = "notepad", ["calc"] = "calc", ["configuracoes"] = "ms-settings:", ["ajustes"] = "ms-settings:", ["camera"] = "microsoft.windows.camera:", ["captura"] = "ms-screenclip:", ["recorte"] = "ms-screenclip:", ["chrome"] = "chrome", ["edge"] = "msedge", ["code"] = "code", ["vscode"] = "code",
        ["explorer"] = "explorer", ["explorador"] = "explorer", ["bloco de notas"] = "notepad", ["notepad"] = "notepad",
        ["calculadora"] = "calc", ["whatsapp"] = "whatsapp:", ["discord"] = "discord:", ["terminal"] = "wt",
        ["obsidian"] = "obsidian:", ["steam"] = "steam:", ["anydesk"] = "anydesk",
    };

    // Proof: "Abri" so vale se uma janela NOVA apareceu ou a janela da frente mudou em ate 4 s. Before, Odin
    // disse "abri o Paint" duas vezes e o Paint nao estava na tela.
    private static string AbrirApp(string nome)
    {
        // "abre o Spotify" com ele ja aberto dava "nenhuma janela nova" e o Odin dizia que nao abriu; e o spotify:
        // cru abre sem --disable-gpu (it crashed). Ja aberto = traz pra frente; fechado = SpotifyHands.Abrir e espera.
        if (CommandLine.Fold(nome?.Trim() ?? "") == "spotify")
        {
            var h = WindowsHands.JanelaDoProcesso("Spotify");
            if (h == IntPtr.Zero && SpotifyHands.Abrir())
                for (var i = 0; i < 50 && h == IntPtr.Zero; i++) { Thread.Sleep(400); h = WindowsHands.JanelaDoProcesso("Spotify"); }
            if (h == IntPtr.Zero) return "Nao consegui abrir o Spotify: em 20 s nenhuma janela do Spotify apareceu. Diga isso ao usuario.";
            WindowsHands.Focus(h);
            return $"Spotify aberto e na frente.\n{WindowsHands.Prova(h, "abrir_app").Replace("\"mao\":\"janela\"", "\"mao\":\"abrir_app\"")}";
        }
        var antes = WindowsHands.ListWindows().Select(w => w.Handle).ToHashSet();
        var frenteAntes = WindowsHands.Foreground();
        var res = AbrirAppCru(nome);
        if (!res.StartsWith("Abri")) return res;
        (IntPtr Handle, string Title) nova = default;
        for (var i = 0; i < 16 && nova.Handle == IntPtr.Zero; i++)
        {
            Thread.Sleep(250);
            nova = WindowsHands.ListWindows().FirstOrDefault(w => !antes.Contains(w.Handle));
            if (nova.Handle == IntPtr.Zero && WindowsHands.Foreground() is var f && f != frenteAntes && f != IntPtr.Zero)
                nova = WindowsHands.ListWindows().FirstOrDefault(w => w.Handle == f);
        }
        if (nova.Handle == IntPtr.Zero)
            return $"Pedi pro Windows abrir {nome}, mas em 4 s nenhuma janela nova apareceu nem a da frente mudou. NAO diga que abriu: diga que pediu e pergunte se apareceu (pode ter aberto em segundo plano).";
        // so entra no registro de abertos se a janela e NOVA de verdade (a "da frente mudou" pode ser janela do usuario)
        if (!antes.Contains(nova.Handle)) OpenedWindows.Registrar(nova.Handle, "abrir_app");
        return $"{res} Janela: '{(nova.Title.Length > 50 ? nova.Title[..50] : nova.Title)}'.\n{WindowsHands.Prova(nova.Handle, "abrir_app").Replace("\"mao\":\"janela\"", "\"mao\":\"abrir_app\"")}";
    }

    /// <summary>"abre 4 bloco de notas" ia pro `fazer` (2,5 min, abriu 8). Agora N de uma vez, cada uma registrada.</summary>
    private static string AbrirAppVarias(string nome, string quantidade)
    {
        var n = Math.Clamp(int.TryParse(quantidade, out var q) ? q : 1, 1, 8);
        if (n == 1) return AbrirApp(nome);
        var antes = OpenedWindows.Foto();
        var falhas = new List<string>();
        for (var i = 0; i < n; i++)
        {
            var r = AbrirApp(nome);
            if (!r.StartsWith("Abri")) falhas.Add(r.Split('\n')[0]);
        }
        var novas = WindowsHands.ListWindows().Where(w => !antes.Contains(w.Handle)).ToList();
        foreach (var w in novas) OpenedWindows.Registrar(w.Handle, "abrir_app");
        var lista = string.Join(" | ", novas.Select(w => w.Title.Length > 30 ? w.Title[..30] : w.Title));
        return novas.Count >= n
            ? $"Abri {novas.Count} de {n} ({nome}), todas registradas como abertas por mim: {lista}.\n[prova] " + JsonSerializer.Serialize(new { mao = "abrir_app", coerente = true, pedidas = n, abertas = novas.Count, handles = novas.Select(w => w.Handle.ToInt64()), lido_em = DateTimeOffset.Now.ToString("o") })
            : $"Nao consegui abrir todas: {novas.Count} de {n} janelas novas de {nome} apareceram. {string.Join(" ", falhas.Take(2))} Diga exatamente quantas abriram.";
    }

    private static string AbrirAppCru(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "Sem nome.";
        var key = CommandLine.Fold(nome.Trim());
        var target = Apps.TryGetValue(key, out var t) ? t : nome.Trim();
        // nome fora da lista -> atalho do Menu Iniciar/Desktop ou jogo da Steam
        string? achado = null;
        if (!Apps.ContainsKey(key) && AppResolver.Resolver(nome) is { } alvo) { target = alvo.Caminho; achado = $"{alvo.Nome} ({alvo.Fonte})"; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            return achado is null ? $"Abri {nome}." : $"Abri {achado}.";
        }
        catch (Exception)
        {
            try
            {
                // Ultimo recurso: o Start do Windows resolve nomes de apps instalados. ESPERA o resultado: antes
                // devolvia "Pedi pro Windows abrir" mesmo quando o Start-Process falhava, e o Odin dizia que abriu
                // ("o paint nao abriu" and the agent claimed success twice).
                var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"try {{ Start-Process '{target.Replace("'", "''")}' -ErrorAction Stop; 'OK' }} catch {{ 'ERRO: ' + $_.Exception.Message }}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var ps = System.Diagnostics.Process.Start(psi)!;
                var saida = ps.StandardOutput.ReadToEnd().Trim();
                ps.WaitForExit(6000);
                if (saida.StartsWith("OK")) return $"Abri {nome} pelo Windows.";
                return $"Nao consegui abrir {nome}: o Windows nao conhece esse nome ({saida.Replace("ERRO: ", "")}). Diga a ele que nao abriu; se souber o executavel, tente de novo com o nome certo.";
            }
            catch (Exception ex) { return $"Nao consegui abrir {nome}: {ex.Message}"; }
        }
    }

    // ---------- util ----------

    private static bool Bool(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString()?.ToLowerInvariant() is "true" or "sim"));

    private static string Str(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Slug(string s)
    {
        var f = CommandLine.Fold(s);
        var sb = new StringBuilder();
        foreach (var c in f) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        var r = sb.ToString().Trim('-');
        while (r.Contains("--")) r = r.Replace("--", "-");
        return r.Length > 40 ? r[..40].TrimEnd('-') : r.Length == 0 ? "nota" : r;
    }
}
