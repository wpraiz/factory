using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Factory.Models;

namespace Factory.Services;

public interface IInboxService
{
    /// <summary><dados>\factory\destinos.json (favoritos, editavel a mao). A user choice,
    /// so it lives with the data, not in %LOCALAPPDATA% (cache only, outside backups).</summary>
    string DestinosFile { get; }

    /// <summary>Favoritos + projetos do catalogo + subpastas de 2o nivel que parecem projeto.</summary>
    IReadOnlyList<Destino> Destinos(IReadOnlyList<ProjectInfo> catalog);

    /// <summary>`claude -p` (texto, sem tools) le o contexto e propoe destino/tipo/prioridade/titulo.</summary>
    Task<Sugestao> SugerirAsync(string contexto, IReadOnlyList<Destino> destinos, IReadOnlyList<ProjectInfo> catalog, CancellationToken ct);

    /// <summary>Grava &lt;destino&gt;\inbox\AAAAMMDD-HHmm-slug.md (entrada/v1) e devolve o caminho.</summary>
    string Salvar(Destino destino, string tipo, string prioridade, string titulo, int quantidade, string contexto);

    /// <summary>Entradas mais recentes de todos os destinos (le so o frontmatter).</summary>
    IReadOnlyList<InboxItem> Recentes(IEnumerable<Destino> destinos, int max = 20);
}

/// <summary>
/// ENTRADA: colar contexto -> vira item no pipe do projeto, como .md em &lt;destino&gt;\inbox\.
/// Contrato entrada/v1 (frontmatter), readable by external collectors: tipo "falha"
/// e o gancho de reciclagem. Nada aqui publica, envia ou gasta — so grava arquivo novo.
/// </summary>
public sealed class InboxService : IInboxService
{
    public static readonly string[] Tipos = ["tarefa", "feature", "artigos", "ideia", "falha", "contexto"];
    public static readonly string[] Prioridades = ["P1", "P2", "P3"];

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "dist", "build", "out", "venv", ".venv", "__pycache__", "inbox",
    };

    public string DestinosFile { get; } = Path.Combine(
        AppPaths.Factory, "destinos.json");

    private sealed record Favorito(string nome, string caminho);

    public IReadOnlyList<Destino> Destinos(IReadOnlyList<ProjectInfo> catalog)
    {
        var list = new List<Destino>();
        foreach (var f in LerFavoritos())
            if (Directory.Exists(f.caminho)) list.Add(new Destino(f.nome, f.caminho, "favorito"));

        foreach (var p in catalog.OrderByDescending(p => p.LastTouch))
        {
            list.Add(new Destino(p.Name, p.Path, "projeto"));
            foreach (var sub in Subprojetos(p.Path))
                list.Add(new Destino($"{p.Name}/{Path.GetFileName(sub)}", sub, "subpasta"));
        }

        return list.DistinctBy(d => d.Caminho.TrimEnd('\\').ToLowerInvariant()).ToList();
    }

    /// <summary>Subpasta direta que tem cara de projeto (STATUS.md, CLAUDE.md ou factory.json).</summary>
    private static IEnumerable<string> Subprojetos(string root)
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root).ToList(); }
        catch (Exception) { yield break; }

        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith('.') || name.StartsWith('_') || SkipDirs.Contains(name)) continue;
            bool pareceProjeto;
            try
            {
                pareceProjeto = File.Exists(Path.Combine(d, "STATUS.md")) || File.Exists(Path.Combine(d, "CLAUDE.md"))
                                || File.Exists(Path.Combine(d, ProjectManifest.FileName));
            }
            catch (Exception) { continue; }
            if (pareceProjeto) yield return d;
        }
    }

    private List<Favorito> LerFavoritos()
    {
        try
        {
            if (!File.Exists(DestinosFile))
            {
                // primeiro uso: semeia com o caso que motivou a ENTRADA (artigos no projeto aninhado)
                Directory.CreateDirectory(Path.GetDirectoryName(DestinosFile)!);
                var seed = new List<Favorito>();
                File.WriteAllText(DestinosFile, JsonSerializer.Serialize(seed, new JsonSerializerOptions { WriteIndented = true }));
                return seed;
            }
            return JsonSerializer.Deserialize<List<Favorito>>(File.ReadAllText(DestinosFile)) ?? [];
        }
        catch (Exception) { return []; }
    }

    // ---------- sugerir ----------

    private const string SystemPrompt =
        "Voce classifica um pedido do usuario (dono de dezenas de projetos) e diz pra onde ele vai. " +
        "Responda SO um objeto JSON, sem texto em volta, no formato: " +
        "{\"destino\":\"<nome EXATO de um item da lista DESTINOS, ou null se nenhum servir>\"," +
        "\"tipo\":\"tarefa|feature|artigos|ideia|falha|contexto\",\"prioridade\":\"P1|P2|P3\"," +
        "\"titulo\":\"<ate 70 caracteres, verbo no infinitivo>\",\"quantidade\":<inteiro, 1 se nao for lote>}. " +
        "P1 = dinheiro/cliente/prazo curto ou algo quebrado; P3 = ideia sem prazo. " +
        "Se o pedido for gerar artigos/posts/conteudo, tipo=artigos e quantidade=o numero pedido. " +
        "Se for relato de erro de IA/agente, tipo=falha. Nunca invente destino fora da lista.";

    public async Task<Sugestao> SugerirAsync(string contexto, IReadOnlyList<Destino> destinos, IReadOnlyList<ProjectInfo> catalog, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DESTINOS (nome · caminho):");
        foreach (var d in destinos) sb.Append("- ").Append(d.Nome).Append(" · ").AppendLine(d.Caminho);
        sb.AppendLine();
        sb.AppendLine(ChatService.BuildCatalog(catalog));
        sb.AppendLine("PEDIDO:");
        sb.AppendLine(contexto.Length > 6000 ? contexto[..6000] : contexto);

        var reply = await ClaudeCli.RunAsync(SystemPrompt, sb.ToString(), null, "sonnet", ct);
        return Parse(reply.Text, destinos);
    }

    private static Sugestao Parse(string text, IReadOnlyList<Destino> destinos)
    {
        var m = Regex.Match(text, @"\{[\s\S]*\}");
        if (!m.Success) throw new InvalidOperationException("sugestao sem JSON: " + Clip(text, 120));
        using var doc = JsonDocument.Parse(m.Value);
        var r = doc.RootElement;
        string Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var destino = Str("destino");
        var achado = destinos.FirstOrDefault(d => d.Nome.Equals(destino, StringComparison.OrdinalIgnoreCase))
                     ?? destinos.FirstOrDefault(d => d.Caminho.Equals(destino, StringComparison.OrdinalIgnoreCase));
        var tipo = Tipos.Contains(Str("tipo")) ? Str("tipo") : "tarefa";
        var prio = Prioridades.Contains(Str("prioridade")) ? Str("prioridade") : "P2";
        var qtd = r.TryGetProperty("quantidade", out var q) && q.TryGetInt32(out var n) ? Math.Clamp(n, 1, 50) : 1;
        return new Sugestao(achado?.Nome, tipo, prio, Str("titulo"), qtd);
    }

    // ---------- gravar / listar ----------

    public string Salvar(Destino destino, string tipo, string prioridade, string titulo, int quantidade, string contexto)
    {
        var dir = Path.Combine(destino.Caminho, "inbox");
        Directory.CreateDirectory(dir);

        var agora = DateTimeOffset.Now;
        var baseName = $"{agora:yyyyMMdd-HHmm}-{Slug(titulo)}";
        var path = Path.Combine(dir, baseName + ".md");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{baseName}-{i}.md");

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("schema: entrada/v1");
        sb.Append("titulo: ").AppendLine(Yaml(titulo));
        sb.Append("projeto: ").AppendLine(Yaml(destino.Nome));
        sb.Append("tipo: ").AppendLine(tipo);
        sb.Append("prioridade: ").AppendLine(prioridade);
        sb.Append("quantidade: ").AppendLine(quantidade.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("status: novo");
        sb.AppendLine("origem: factory-entrada");
        sb.Append("criado_em: ").AppendLine(agora.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# ").AppendLine(titulo);
        sb.AppendLine();
        sb.AppendLine(contexto.Trim());
        sb.AppendLine();
        sb.AppendLine("## Como executar");
        sb.AppendLine(tipo == "artigos"
            ? $"Gerar {quantidade} artigo(s) pelo fluxo deste projeto. Deixar como rascunho; publicar so com OK do usuario."
            : "Seguir o CLAUDE.md/STATUS.md deste projeto. Parar antes de publicar, enviar ou gastar.");
        sb.AppendLine("Ao terminar, trocar `status: novo` por `status: feito` neste arquivo e registrar no STATUS.md.");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    public IReadOnlyList<InboxItem> Recentes(IEnumerable<Destino> destinos, int max = 20)
    {
        var itens = new List<InboxItem>();
        foreach (var d in destinos)
        {
            var dir = Path.Combine(d.Caminho, "inbox");
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.md").ToList(); }
            catch (Exception) { continue; }
            foreach (var f in files)
            {
                var item = Ler(f, d);
                if (item is not null) itens.Add(item);
            }
        }
        return itens.OrderByDescending(i => i.Criado).Take(max).ToList();
    }

    private static InboxItem? Ler(string file, Destino d)
    {
        try
        {
            var head = File.ReadLines(file).Take(20).ToList();
            if (head.Count == 0 || head[0] != "---") return null;
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in head.Skip(1))
            {
                if (line == "---") break;
                var i = line.IndexOf(':');
                if (i > 0) kv[line[..i].Trim()] = line[(i + 1)..].Trim().Trim('"');
            }
            if (!kv.TryGetValue("schema", out var s) || s != "entrada/v1") return null;
            var criado = kv.TryGetValue("criado_em", out var c) && DateTimeOffset.TryParse(c, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt : new DateTimeOffset(File.GetLastWriteTime(file));
            return new InboxItem(
                kv.GetValueOrDefault("titulo", Path.GetFileNameWithoutExtension(file)),
                kv.GetValueOrDefault("projeto", d.Nome),
                kv.GetValueOrDefault("tipo", "?"),
                kv.GetValueOrDefault("prioridade", "?"),
                kv.GetValueOrDefault("status", "?"),
                criado, file, d.Caminho);
        }
        catch (Exception) { return null; }
    }

    // ---------- texto ----------

    private static string Yaml(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace('\n', ' ').Trim() + "\"";

    private static string Slug(string s)
    {
        var folded = CommandLine.Fold(s);
        var slug = Regex.Replace(folded, "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 50) slug = slug[..50].TrimEnd('-');
        return slug.Length == 0 ? "entrada" : slug;
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
