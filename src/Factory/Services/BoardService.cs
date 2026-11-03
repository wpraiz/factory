using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Factory.Models;

namespace Factory.Services;

public interface IPlanoService
{
    /// <summary><dados>\factory\decisoes.jsonl — unico arquivo que a secao PLANO escreve.</summary>
    string DecisoesPath { get; }
    /// <summary>Reads the sources (read-only). Arquivo ausente vira linha em Faltas, nunca card inventado.</summary>
    PlanoLeitura Ler();
    /// <summary>Decisao mais recente por id (ultima linha valida do arquivo vence).</summary>
    IReadOnlyDictionary<string, DecisaoPlano> LerDecisoes(out int linhasInvalidas);
    /// <summary>Acrescenta uma linha decisao/v1. Nunca reescreve o arquivo.</summary>
    DecisaoPlano Gravar(BoardCard card, string decisao, string nota);
}

/// <summary>
/// Builds the BOARD cards from: the Factory STATUS.md (the 3 newest "Onde paramos" sections, filtered by
/// aberto/pendente/fila/proxima/decisao/aguarda; "Proxima acao"; "Bloqueios"; "Fila") and docs\plano-*.md (each ##
/// section). Writes only decisoes.jsonl, append-only.
/// </summary>
public sealed partial class BoardService : IPlanoService
{
    private static readonly string FactoryDir = AppPaths.Repo;
    public static readonly string StatusPath = Path.Combine(FactoryDir, "STATUS.md");
    public static readonly string PlanosDir = Path.Combine(FactoryDir, "docs");

    public string DecisoesPath { get; init; } = Path.Combine(AppPaths.Factory, "decisoes.jsonl");

    private const int ResumoMaxChars = 600;
    private const int ResumoMaxLinhas = 8;
    private const int OndeParamosLidos = 3;
    private readonly object _gravando = new();

    // ---------------------------------------------------------------- leitura

    public PlanoLeitura Ler()
    {
        var cards = new List<BoardCard>();
        var faltas = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        void Tentar(string nome, Action acao)
        {
            try { acao(); }
            catch (Exception ex) { faltas.Add($"{nome}: não deu pra ler ({ex.Message})"); }
        }

        if (File.Exists(StatusPath)) Tentar("STATUS.md", () => LerStatus(cards, ids));
        else faltas.Add($"faltou {StatusPath}");

        var planos = Directory.Exists(PlanosDir)
            ? Directory.GetFiles(PlanosDir, "plano-*.md").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (planos.Length == 0) faltas.Add($"nenhum plano-*.md em {PlanosDir}");
        foreach (var p in planos) Tentar(Path.GetFileName(p), () => LerPlano(p, cards, ids));

        return new PlanoLeitura(cards, faltas);
    }

    private static string[] Linhas(string path) => File.ReadAllText(path).Replace("\r", "").Split('\n');

    /// <summary>Blocos "## titulo" com a linha (1-based) do cabecalho; ignora ## dentro de ```.</summary>
    private static List<(string Titulo, int Linha, List<(string Texto, int Linha)> Corpo)> Secoes(string[] linhas)
    {
        var secoes = new List<(string, int, List<(string, int)>)>();
        var cerca = false;
        for (var i = 0; i < linhas.Length; i++)
        {
            var l = linhas[i];
            if (l.TrimStart().StartsWith("```")) cerca = !cerca;
            if (!cerca && l.StartsWith("## "))
            {
                secoes.Add((l[3..].Trim(), i + 1, []));
                continue;
            }
            if (secoes.Count > 0) secoes[^1].Item3.Add((l, i + 1));
        }
        return secoes;
    }

    /// <summary>Itens de lista (- , * , 1. ) com as linhas de continuacao (indentadas) juntas.</summary>
    private static List<(string Texto, int Linha)> Itens(List<(string Texto, int Linha)> corpo, Regex inicio, bool paragrafo = false)
    {
        var itens = new List<(string, int)>();
        StringBuilder? atual = null;
        var linhaAtual = 0;
        foreach (var (texto, linha) in corpo)
        {
            if (inicio.IsMatch(texto))
            {
                if (atual is not null) itens.Add((atual.ToString().Trim(), linhaAtual));
                atual = new StringBuilder(texto);
                linhaAtual = linha;
            }
            // paragrafo (Fila): continua ate a linha em branco; lista: so linha indentada continua
            else if (atual is not null && texto.Trim().Length > 0 && (paragrafo || char.IsWhiteSpace(texto[0])))
                atual.Append(' ').Append(texto.Trim()); // quebra suave do .md: junta (negrito que atravessa linha)
            else if (atual is not null)
            {
                itens.Add((atual.ToString().Trim(), linhaAtual));
                atual = null;
            }
        }
        if (atual is not null) itens.Add((atual.ToString().Trim(), linhaAtual));
        return itens;
    }

    [GeneratedRegex(@"^[-*]\s+")] private static partial Regex BulletRx();
    [GeneratedRegex(@"^\d+[.)]\s+")] private static partial Regex NumeradoRx();
    [GeneratedRegex(@"^\*\*")] private static partial Regex ParagrafoNegritoRx();
    [GeneratedRegex(@"\b(aberto|aberta|abertos|pendente|pendentes|fila|proxima|decisao|aguarda|aguardando)\b")]
    private static partial Regex PendenciaRx();

    private void LerStatus(List<BoardCard> cards, HashSet<string> ids)
    {
        var secoes = Secoes(Linhas(StatusPath));
        var arquivo = Path.GetFileName(StatusPath);

        // 1) os 3 "Onde paramos" do topo (o /status grava o mais novo em cima), so bullets com pendencia
        foreach (var s in secoes.Where(s => s.Titulo.StartsWith("Onde paramos", StringComparison.OrdinalIgnoreCase)).Take(OndeParamosLidos))
        {
            var quando = QuandoDoCabecalho(s.Titulo);
            foreach (var (texto, linha) in Itens(s.Corpo, BulletRx()))
            {
                if (!PendenciaRx().IsMatch(Normalizar(texto))) continue;
                var corpo = BulletRx().Replace(texto, "");
                var titulo = TituloDe(corpo);
                if (quando.Length > 0) titulo = $"{titulo} · {quando}";
                Add(cards, ids, "status", "STATUS", StatusPath, arquivo, $"Onde paramos {quando}".Trim(), titulo, SemNegritoInicial(corpo), linha);
            }
        }

        // 2) Proxima acao (itens numerados), Bloqueios (bullets), Fila (paragrafos que abrem em **negrito**)
        foreach (var s in secoes)
        {
            var t = Normalizar(s.Titulo);
            (Regex rx, string secao)? alvo =
                t.StartsWith("proxima acao") ? (NumeradoRx(), "Próxima ação") :
                t.StartsWith("bloqueios") ? (BulletRx(), "Bloqueios") :
                t.StartsWith("fila") ? (ParagrafoNegritoRx(), "Fila") : null;
            if (alvo is not { } a) continue;

            var paragrafo = a.secao == "Fila";
            foreach (var (texto, linha) in Itens(s.Corpo, a.rx, paragrafo))
            {
                var corpo = paragrafo ? texto : a.rx.Replace(texto, "");
                Add(cards, ids, "status", "STATUS", StatusPath, arquivo, a.secao, TituloDe(corpo), SemNegritoInicial(corpo), linha);
            }
        }
    }

    private void LerPlano(string path, List<BoardCard> cards, HashSet<string> ids)
    {
        var arquivo = Path.GetFileName(path);
        var secao = Path.GetFileNameWithoutExtension(path);
        foreach (var s in Secoes(Linhas(path)))
        {
            var corpo = string.Join('\n', s.Corpo.Select(c => c.Texto)).Trim();
            // docs\plano-rumo.md is the north star + scoreboard: its own source, on top, with more lines per card
            var rumo = arquivo.Equals("plano-rumo.md", StringComparison.OrdinalIgnoreCase);
            Add(cards, ids, rumo ? "rumo" : "planos", rumo ? "RUMO" : "PLANO", path, arquivo, secao, LimparMd(s.Titulo), corpo, s.Linha, rumo ? 40 : ResumoMaxLinhas);
        }
    }

    private static void Add(List<BoardCard> cards, HashSet<string> ids, string fonte, string selo, string caminho,
                            string arquivo, string secao, string titulo, string resumo, int linha, int maxLinhas = ResumoMaxLinhas)
    {
        titulo = Regex.Replace(titulo, @"\s+", " ").Trim();
        var id = $"{arquivo}#{Hash(titulo)}";
        // titulo repetido na mesma fonte: sufixo pela ordem (estavel enquanto a ordem nao mudar)
        for (var n = 2; !ids.Add(id); n++) id = $"{arquivo}#{Hash(titulo)}-{n}";
        cards.Add(new BoardCard
        {
            Id = id, Fonte = fonte, Selo = selo, Caminho = caminho, Secao = secao,
            Titulo = titulo, Resumo = Recortar(resumo, maxLinhas), Linha = linha,
        });
    }

    /// <summary>Hash estavel do titulo: SHA-1 do texto normalizado (espacos colapsados), 10 hex.</summary>
    public static string Hash(string titulo)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(Regex.Replace(titulo, @"\s+", " ").Trim()));
        return Convert.ToHexString(bytes)[..10].ToLowerInvariant();
    }

    /// <summary>"**Negrito inicial**" vira titulo; senao, o comeco do texto sem markdown, cortado em palavra.</summary>
    private static string TituloDe(string texto)
    {
        var t = texto.TrimStart();
        if (t.StartsWith("**"))
        {
            var fim = t.IndexOf("**", 2, StringComparison.Ordinal);
            if (fim > 2 && fim < 140) return t[2..fim].Trim().TrimEnd('.', ':').Trim();
        }
        var limpo = LimparMd(t.Split('\n')[0]);
        if (limpo.Length <= 90) return limpo.TrimEnd('.', ':');
        var corte = limpo.LastIndexOf(' ', 90);
        return (corte > 40 ? limpo[..corte] : limpo[..90]).TrimEnd('.', ',', ':', ';') + "…";
    }

    /// <summary>Tira o "**Titulo**:" do comeco quando ele ja virou o titulo do card (nao repetir).</summary>
    private static string SemNegritoInicial(string texto)
    {
        var t = texto.TrimStart();
        if (!t.StartsWith("**")) return texto;
        var fim = t.IndexOf("**", 2, StringComparison.Ordinal);
        if (fim <= 2 || fim >= 140) return texto;
        return t[(fim + 2)..].TrimStart(' ', ':', ',', '.', '—', '-').Trim();
    }

    private static string LimparMd(string s) => s.Replace("**", "").Replace("`", "").Trim();

    private static string Recortar(string md, int max = ResumoMaxLinhas)
    {
        var linhas = md.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        var cortou = linhas.Count > max;
        var texto = string.Join('\n', linhas.Take(max));
        var maxChars = ResumoMaxChars * max / ResumoMaxLinhas;
        if (texto.Length > maxChars)
        {
            var corte = texto.LastIndexOf(' ', maxChars);
            texto = texto[..(corte > maxChars / 2 ? corte : maxChars)];
            // nao deixar ** ou ` aberto (MarkdownLite mostraria literal)
            if (texto.Split("**").Length % 2 == 0) texto += "**";
            if (texto.Count(c => c == '`') % 2 == 1) texto += "`";
            cortou = true;
        }
        return cortou ? texto.TrimEnd() + " …" : texto;
    }

    /// <summary>"Onde paramos (dd/mm hh:mm — ...)" → "dd/mm hh:mm".</summary>
    private static string QuandoDoCabecalho(string titulo)
    {
        var a = titulo.IndexOf('(');
        if (a < 0) return "";
        var resto = titulo[(a + 1)..];
        var fim = resto.IndexOfAny(['—', ')']);
        return (fim >= 0 ? resto[..fim] : resto).Trim().TrimEnd(',');
    }

    private static string Normalizar(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- decisoes

    public IReadOnlyDictionary<string, DecisaoPlano> LerDecisoes(out int linhasInvalidas)
    {
        linhasInvalidas = 0;
        var mapa = new Dictionary<string, DecisaoPlano>(StringComparer.Ordinal);
        if (!File.Exists(DecisoesPath)) return mapa;

        string texto;
        using (var fs = new FileStream(DecisoesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sr = new StreamReader(fs, Encoding.UTF8))
            texto = sr.ReadToEnd();

        foreach (var linha in texto.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;
            try
            {
                var o = JsonDocument.Parse(linha).RootElement;
                string S(string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var id = S("id");
                var decisao = S("decisao");
                if (id.Length == 0 || decisao is not ("bom" or "ruim" or "depois")) { linhasInvalidas++; continue; }
                DateTimeOffset.TryParse(S("em"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var em);
                mapa[id] = new DecisaoPlano(id, S("fonte"), S("titulo"), decisao, S("nota"), em); // ultima vence
            }
            catch (JsonException) { linhasInvalidas++; }
        }
        return mapa;
    }

    public DecisaoPlano Gravar(BoardCard card, string decisao, string nota)
    {
        if (decisao is not ("bom" or "ruim" or "depois")) throw new ArgumentException($"decisão inválida: {decisao}");
        nota = Regex.Replace(nota ?? "", @"\s+", " ").Trim();
        var d = new DecisaoPlano(card.Id, card.Caminho, card.Titulo, decisao, nota, DateTimeOffset.Now);

        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("schema", "decisao/v1");
            w.WriteString("id", d.Id);
            w.WriteString("fonte", d.Fonte);
            w.WriteString("titulo", d.Titulo);
            w.WriteString("decisao", d.Decisao);
            w.WriteString("nota", d.Nota);
            w.WriteString("em", d.Em.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        var linha = Encoding.UTF8.GetString(buffer.ToArray()) + "\n";

        lock (_gravando)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DecisoesPath)!);
            using var fs = new FileStream(DecisoesPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = new UTF8Encoding(false).GetBytes(linha);
            fs.Write(bytes, 0, bytes.Length);
        }
        return d;
    }
}
