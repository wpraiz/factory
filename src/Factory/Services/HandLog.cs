using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Factory.Services;

/// <summary>
/// Receipt of every Odin hand run in the hand-result/v1 contract. Append-only in
/// &lt;data&gt;\odin\hands\YYYY-MM.jsonl (user data). Tres estados separados: a linha
/// existir = mao chamada; estado_execucao = terminou ou nao; veredito = objetivo cumprido.
/// "confirmado" so com evidencia confiavel (arquivo lido do disco); tela, fiscal LLM e o que a mao
/// disse sao sempre confiavel=false. Nunca lanca: recibo que falha nao derruba a mao.
/// </summary>
public static partial class HandLog
{
    public static readonly string Dir = Path.Combine(AppPaths.Odin, "hands");

    /// <summary>De onde veio a chamada: origem (voz|online|texto|interno) e, na voz, a conversa ElevenLabs.
    /// Episode = "conv_x#mao@epochms" — o SDK nao entrega tool_call_id ao client tool; um importador acha o turno
    /// pela API com conversa + mao + horario. String vira Fonte sozinha (chamadas antigas continuam valendo).</summary>
    public sealed record Fonte(string Origem, string? Conv = null, string? Episode = null)
    {
        public static implicit operator Fonte(string origem) => new(origem);
    }
    private static readonly object Gate = new();

    private static readonly Lazy<Dictionary<string, string[]>> Tags = new(LoadTags);

    /// <summary>Commit do build (publish passa -p:SourceRevisionId; o SDK poe no InformationalVersion depois do '+').</summary>
    /// Always 7 characters: a build outside publish (SDK SourceLink) puts the full hash, and both formats
    /// ended up mixed in the receipts.
    private static readonly string Commit =
        typeof(HandLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } v
        && v.IndexOf('+') is var i and >= 0 ? Curto(v[(i + 1)..]) : "";

    private static string Curto(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static Dictionary<string, string[]> LoadTags()
    {
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "Assets", "hands.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            return doc.RootElement.GetProperty("maos").EnumerateArray().ToDictionary(
                m => m.GetProperty("nome").GetString() ?? "",
                m => m.GetProperty("tags").EnumerateArray().Select(t => t.GetString() ?? "").ToArray());
        }
        catch (Exception) { return []; }
    }

    /// <summary>Tags de seguranca da mao; mao fora do manifesto = "executa" (o pior caso, nunca "leitura").</summary>
    public static string[] TagsDe(string mao) => Tags.Value.TryGetValue(mao, out var t) && t.Length > 0 ? t : ["executa"];

    /// <summary>Nomes do manifesto (o teste compara com o switch do OdinHands).</summary>
    public static IReadOnlyCollection<string> NomesDoManifesto => Tags.Value.Keys;

    public static string NovoId() => $"r-{DateTime.Now:yyyyMMdd-HHmmss}-{RandomNumberGenerator.GetHexString(4, lowercase: true)}";

    public static string Agente(string origem) => origem switch
    {
        "voz" => App.IsHomolog ? "lab" : "prod",
        _ => "outro",
    };

    /// <summary>1a linha de mao longa (fazer/pesquisar/ver_tela): em_execucao, inconclusivo.</summary>
    public static void Inicio(string rid, string mao, Fonte origem, JsonElement args, DateTimeOffset inicio) =>
        Gravar(Base(rid, mao, origem, args, inicio, null, "em_execucao", null,
            [], "inconclusivo", "rodando em segundo plano; a linha final vem com o mesmo request_id"));

    /// <summary>Linha final de mao rapida: avalia pelo que da pra ler de verdade. prova = estado lido depois da
    /// acao que a propria mao devolveu na linha "[prova] {json}" (SepararProva), ja fora do texto falado.</summary>
    public static void Rapida(string rid, string mao, Fonte origem, JsonElement args, DateTimeOffset inicio, string resultado, bool excecao,
        JsonObject? prova = null)
    {
        var (evidencias, veredito, motivo) = Avaliar(mao, resultado, excecao, inicio, prova);
        Gravar(Base(rid, mao, origem, args, inicio, DateTimeOffset.Now, excecao ? "erro" : "encerrada", resultado, evidencias, veredito, motivo));
    }

    /// <summary>
    /// Hand→receipt contract: a hand may end its return with ONE line
    /// "[prova] {json}" com o estado lido de volta depois de agir (obs: cena/gravando/arquivo; janela: hwnd/
    /// titulo/foreground). Aqui ela sai do texto que o Odin fala e vira evidencia confiavel. Em falha a mao nao
    /// emite prova. JSON invalido = ignora (fica so a declaracao).
    /// Before: any JSON counted. Mao rapida que devolve texto de terceiro (ler_status,
    /// perguntar_claude, gravar_status) podia trazer uma linha "[prova]" e virar confirmado. Agora so vale se o
    /// campo "mao" da prova e uma das que produzem prova E bate com a mao chamada (ou o roteador do fazer).
    /// </summary>
    public static (string Falado, JsonObject? Prova) SepararProva(string resultado, string mao)
    {
        var linhas = resultado.Replace("\r", "").Split('\n').ToList();
        var i = linhas.FindLastIndex(l => l.TrimStart().StartsWith("[prova]", StringComparison.Ordinal));
        if (i < 0) return (resultado, null);
        var json = linhas[i].TrimStart()["[prova]".Length..].Trim();
        linhas.RemoveAt(i);
        var falado = string.Join("\n", linhas).TrimEnd();
        try
        {
            if (JsonNode.Parse(json) is not JsonObject p || p["mao"]?.GetValue<string>() is not { } dono) return (falado, null);
            return ProduzemProva.Contains(dono) && (dono == mao || mao == "fazer") ? (falado, p) : (falado, null);
        }
        catch (Exception) { return (falado, null); }
    }

    /// <summary>Maos que leem o estado de volta e emitem "[prova]" (ObsHands, WindowsHands.Prova, musica;
    /// terminais organizar = retangulos lidos; desenhar = pixels do contorno antes/depois).</summary>
    private static readonly HashSet<string> ProduzemProva = ["obs", "janela", "abrir_app", "musica", "focar_terminal", "terminais", "desenhar", "volume_odin"];

    /// <summary>Linha final da mao longa. Fiscal (LLM olhando a tela) nunca confirma sozinho; o que confirma e
    /// arquivo citado pelo executor e lido no disco (existe + gravado depois do inicio).</summary>
    /// Devolve o veredito: Odin's spoken line comes from it (23 of 24 spoken "VERIFICADO" were inconclusive here).
    public static (string Veredito, string Motivo) Longa(string rid, string mao, Fonte origem, JsonElement args, DateTimeOffset inicio,
        string resultado, bool? fiscalOk, string? fiscalEvidencia, bool erro)
    {
        var (ev, veredito, motivo) = AvaliarLonga(resultado, fiscalOk, fiscalEvidencia, inicio, erro);
        Gravar(Base(rid, mao, origem, args, inicio, DateTimeOffset.Now, erro ? "erro" : "encerrada", resultado, ev, veredito, motivo));
        return (veredito, motivo);
    }

    /// <summary>Mesma regua do recibo, sem gravar (mao longa chamada sem recibo).</summary>
    public static (JsonArray Evidencias, string Veredito, string Motivo) AvaliarLonga(string resultado, bool? fiscalOk,
        string? fiscalEvidencia, DateTimeOffset inicio, bool erro)
    {
        var ev = new JsonArray { Evidencia("declaracao", "texto devolvido pelo executor (Claude Code)", Clip(resultado, 300), false) };
        if (fiscalOk is not null)
            ev.Add(Evidencia("fiscal_llm", "haiku com Screenshot/Snapshot, sem clique (FiscalizarAsync)",
                $"{(fiscalOk == true ? "ok" : "nao confirmou")}: {fiscalEvidencia}", false));

        // arquivos que o executor diz ter tocado: existe? foi gravado agora? (fazer had 0/20 confirmed)
        var citados = erro ? [] : CaminhoAbsoluto().Matches(resultado).Select(m => m.Value.TrimEnd('.', ',', ')', ';'))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        var inexistente = false; var gravado = false;
        foreach (var f in citados)
        {
            var e = LerArquivo(f, inicio);
            ev.Add(e.Evidencia);
            // a screenshot taken by the executor itself does not prove the action (Spotify "COMPROVADO" from its own prints) and
            // print temporario apagado nao prova falha (play verdadeiro saiu NAO CONFIRMADO). Imagem nao decide.
            if (EhImagem(f)) continue;
            if (!File.Exists(f)) inexistente = true;
            gravado |= e.Ok;
        }

        var (veredito, motivo) = erro ? ("falhou", "executor falhou ou estourou o tempo")
            : fiscalOk == false ? ("falhou", "fiscal viu a tela e nao confirmou a alegacao")
            : inexistente ? ("falhou", "executor citou arquivo que nao existe no disco")
            : gravado ? ("confirmado", "arquivo citado pelo executor existe e foi gravado depois do inicio")
            : ("inconclusivo", citados.Count > 0 ? "arquivos citados existem mas nenhum foi gravado agora (leitura?)"
                : fiscalOk == true ? "fiscal LLM concordou, mas tela nao e evidencia confiavel" : "sem fiscal");
        return (ev, veredito, motivo);
    }

    // ---------- avaliacao ----------

    [GeneratedRegex(@"^(A mao falhou|Nao achei|Nao tenho a mao|Nao consegui|Tarefa vazia|Nota vazia|Texto vazio|Sem texto|Sem nome|O Claude Code nao esta)", RegexOptions.IgnoreCase)]
    private static partial Regex Falha();

    [GeneratedRegex(@"[A-Za-z]:\\[^\r\n""'<>|*?]+?\.[A-Za-z0-9]{1,6}\b")]
    private static partial Regex CaminhoAbsoluto();

    private static bool EhImagem(string f) =>
        Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif";

    /// <summary>Motivo se o estado lido contradiz a acao pedida; null se nao contradiz.</summary>
    public static string? ProvaDesmente(JsonObject p)
    {
        if (p["coerente"] is JsonValue c && c.TryGetValue<bool>(out var ok) && !ok) return "a propria prova marca coerente=false";
        var acao = CommandLine.Fold(p["acao"]?.ToString() ?? "");
        bool? Tocando(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
        var tocando = Tocando(p["tocando"]) ?? Tocando((p["depois"] as JsonObject)?["tocando"]);
        if (acao is "pausar" or "pause" or "parar" && tocando == true) return "prova diz que continua tocando depois de pausar";
        if (acao is "tocar" or "play" or "continuar" && tocando == false) return "prova diz que continua pausado depois de tocar";
        if (acao is "proxima" or "anterior" && p["spotify_antes"]?.ToString() is { } a && a == p["spotify_depois"]?.ToString()) return "prova diz que a faixa nao mudou";
        if (p["minimizada"] is JsonValue mz && mz.TryGetValue<bool>(out var min) && min && acao is "frente" or "maximizar" or "abrir_app") return "prova diz que a janela continua minimizada";
        return null;
    }

    private static (JsonArray, string, string) Avaliar(string mao, string resultado, bool excecao, DateTimeOffset inicio, JsonObject? prova)
    {
        var declarou = Evidencia("declaracao", "texto devolvido pela mao", Clip(resultado, 300), false);
        if (excecao || Falha().IsMatch(resultado.TrimStart()))
            return ([declarou], "falhou", "a propria mao devolveu erro/recusa");

        // estado lido de volta pela propria mao depois de agir (obs pelo WebSocket, janela pelo Win32)
        // the proof can contradict the hand. 3 recibos "confirmado" de pausar com tocando=true na propria prova.
        if (prova is not null && ProvaDesmente(prova) is { } desmente)
            return ([declarou, Evidencia("app", $"estado lido depois da acao pela mao {mao} (linha [prova])", prova.DeepClone(), true)],
                "falhou", desmente);
        if (prova is not null)
            return ([declarou, Evidencia("app", $"estado lido depois da acao pela mao {mao} (linha [prova])", prova.DeepClone(), true)],
                "confirmado", "estado real lido de volta depois da acao (a mao so emite prova em sucesso)");

        if (!TagsDe(mao).Contains("escrita"))
            return ([declarou], "inconclusivo", "mao sem escrita: so a declaracao dela, nada lido no disco");

        var ev = new JsonArray { declarou };
        var alvos = Alvos(mao, resultado);
        if (alvos.Count == 0) return (ev, "inconclusivo", "mao de escrita sem caminho verificavel no retorno");

        var todosOk = true;
        foreach (var f in alvos)
        {
            var e = LerArquivo(f, inicio);
            ev.Add(e.Evidencia);
            todosOk &= e.Ok;
        }
        return todosOk
            ? (ev, "confirmado", "arquivo existe e foi gravado depois do inicio da mao")
            : (ev, "falhou", "arquivo alegado nao existe ou nao foi alterado agora");
    }

    /// <summary>Onde a mao de escrita diz que gravou (caminho absoluto no retorno, ou os fixos da casa).</summary>
    private static List<string> Alvos(string mao, string resultado)
    {
        var l = CaminhoAbsoluto().Matches(resultado).Select(m => m.Value.TrimEnd('.', ',', ')')).ToList();
        var notas = OdinHands.NotesDir;
        if (mao == "criar_nota" && Regex.Match(resultado, @"Nota gravada: (\S+\.md)") is { Success: true } n)
            l.Add(Path.Combine(notas, n.Groups[1].Value));
        if (mao == "reportar_falha") l.Add(Path.Combine(notas, "falhas.md"));
        return l.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (bool Ok, JsonObject Evidencia) LerArquivo(string f, DateTimeOffset inicio)
    {
        try
        {
            if (!File.Exists(f))
                return (false, Evidencia("arquivo", "File.Exists", new JsonObject { ["caminho"] = f, ["existe"] = false }, true));
            var info = new FileInfo(f);
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))[..8].ToLowerInvariant();
            // 2 s de folga: relogio de arquivo tem granularidade propria
            var gravadoAgora = info.LastWriteTime >= inicio.LocalDateTime.AddSeconds(-2);
            var valor = new JsonObject
            {
                ["caminho"] = f, ["bytes"] = info.Length, ["sha8"] = sha,
                ["modificado_em"] = new DateTimeOffset(info.LastWriteTime).ToString("o"), ["gravado_agora"] = gravadoAgora,
            };
            return (gravadoAgora, Evidencia("arquivo", "File.Exists + FileInfo.Length + LastWriteTime + sha256 (8 primeiros)", valor, true));
        }
        catch (Exception ex)
        {
            return (false, Evidencia("arquivo", "leitura falhou: " + ex.Message, new JsonObject { ["caminho"] = f }, true));
        }
    }

    // ---------- montagem ----------

    private static JsonObject Evidencia(string tipo, string metodo, JsonNode? valor, bool confiavel) => new()
    {
        ["tipo"] = tipo, ["metodo"] = metodo, ["valor"] = valor,
        ["lido_em"] = DateTimeOffset.Now.ToString("o"), ["confiavel"] = confiavel,
    };

    private static JsonObject Base(string rid, string mao, Fonte origem, JsonElement args, DateTimeOffset inicio,
        DateTimeOffset? fim, string estado, string? alegado, JsonArray evidencias, string veredito, string motivo)
    {
        var o = new JsonObject
        {
            ["schema"] = "hand-result/v1",
            ["request_id"] = rid,
            ["episode_id"] = origem.Episode,
            ["conv_id"] = origem.Conv,
            ["agente"] = Agente(origem.Origem),
            ["mao"] = mao,
            ["mao_tags"] = new JsonArray(TagsDe(mao).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["origem"] = origem.Origem,
            ["params_resumo"] = Ocultar(args),
            ["iniciado_em"] = inicio.ToString("o"),
            ["terminado_em"] = fim?.ToString("o"),
            ["estado_execucao"] = estado,
            ["resultado_alegado"] = alegado is null ? null : Clip(Ocultar(alegado), 600),
            ["evidencias"] = evidencias,
            ["veredito"] = veredito,
            ["veredito_motivo"] = motivo,
            ["ocultacao"] = new JsonObject { ["politica"] = "segredos" },
        };
        if (Commit.Length > 0) o["factory_commit"] = Commit;
        return o;
    }

    private static void Gravar(JsonObject o)
    {
        try
        {
            var line = o.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM") + ".jsonl"), line + "\n", new UTF8Encoding(false));
            }
        }
        catch (Exception) { /* recibo nunca derruba a mao */ }
    }

    // ---------- redaction (secrets only; PII is handled by whoever imports the receipts) ----------

    [GeneratedRegex(@"(sk[-_][A-Za-z0-9_\-]{16,}|xi-[A-Za-z0-9]{16,}|ghp_[A-Za-z0-9]{20,}|AIza[0-9A-Za-z_\-]{20,}|eyJ[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,})")]
    private static partial Regex Segredo();

    [GeneratedRegex(@"(?i)(senha|password|token|secret|api[_-]?key)\s*[:=]\s*\S+")]
    private static partial Regex ChaveValor();

    private static string Ocultar(string s) => ChaveValor().Replace(Segredo().Replace(s, "[segredo]"), m => m.Groups[1].Value + "=[segredo]");

    private static string Ocultar(JsonElement args)
    {
        try { return Clip(Ocultar(args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText()), 300); }
        catch (Exception) { return "{}"; }
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
