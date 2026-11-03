using System.Text.Json;
using Factory.Models;

namespace Factory.Services;

public interface ISetupInventory
{
    /// <summary>Le ~/.claude (skills, agentes, plugins, hooks), ~/.claude.json (MCP) e
    /// <raiz>\*\.claude (skills/agentes de projeto). So leitura; nunca escreve em ~/.claude.</summary>
    SetupInventario Ler();

    /// <summary>Maos do Odin (Assets\hands.json) e scripts de tools\ com a 1a linha de comentario.</summary>
    (IReadOnlyList<MaoOdin> Maos, IReadOnlyList<ScriptFactory> Scripts) LerMapa();

    /// <summary><dados>\factory\setup-joias.json — user marks, kept with the data.</summary>
    string JoiasFile { get; }
    HashSet<string> LerJoias();
    void GravarJoias(IEnumerable<string> chaves);
}

/// <summary>Item cru do inventario (sem uso). Vira SetupItem no ViewModel.</summary>
public sealed record ItemInventario(string Tipo, string Nome, string Descricao, string Origem, string Caminho,
    bool Desligado = false, bool Medido = true, string? PluginDono = null);

public sealed record SetupInventario(IReadOnlyList<ItemInventario> Itens, bool ClaudeMdGlobal, int ClaudeMdProjetos);

public sealed class SetupInventory : ISetupInventory
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string ClaudeDir = Path.Combine(Home, ".claude");
    private static readonly string CodesRoot = global::Factory.Services.AppPaths.Root;

    public string JoiasFile { get; } = Path.Combine(global::Factory.Services.AppPaths.Factory, "setup-joias.json");

    public SetupInventario Ler()
    {
        var itens = new List<ItemInventario>();

        // ---- skills e agentes do usuario
        itens.AddRange(Skills(Path.Combine(ClaudeDir, "skills"), "usuário", prefixo: null, desligado: false, dono: null));
        itens.AddRange(Agentes(Path.Combine(ClaudeDir, "agents"), "usuário", prefixo: null, desligado: false, dono: null));

        // ---- settings.json: plugins ligados/desligados + hooks
        var ligados = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var settingsPath = Path.Combine(ClaudeDir, "settings.json");
        using (var settings = AbrirJson(settingsPath))
        {
            if (settings is not null && settings.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (settings.RootElement.TryGetProperty("enabledPlugins", out var ep) && ep.ValueKind == JsonValueKind.Object)
                    foreach (var p in ep.EnumerateObject())
                        ligados[p.Name.Split('@')[0]] = p.Value.ValueKind != JsonValueKind.False;

                if (settings.RootElement.TryGetProperty("hooks", out var hooks) && hooks.ValueKind == JsonValueKind.Object)
                    itens.AddRange(Hooks(hooks, settingsPath));
            }
        }

        // ---- plugins: instalados (installed_plugins.json -> cache) + sincronizados (plugins\synced\*\*)
        foreach (var (raiz, nomePasta) in RaizesDePlugin())
        {
            var nome = NomeDoPlugin(raiz) ?? nomePasta;
            var desligado = ligados.TryGetValue(nome, out var on) && !on;
            var desc = DescricaoDoPlugin(raiz);
            itens.Add(new ItemInventario("plugin", nome, desc, "plugin", raiz, desligado, PluginDono: nome));
            itens.AddRange(Skills(Path.Combine(raiz, "skills"), $"plugin {nome}", nome, desligado, nome));
            itens.AddRange(Agentes(Path.Combine(raiz, "agents"), $"plugin {nome}", nome, desligado, nome));
            foreach (var srv in ServidoresMcp(Path.Combine(raiz, ".mcp.json")))
                itens.Add(new ItemInventario("mcp", $"plugin_{nome}_{srv.Nome}", srv.Desc, $"plugin {nome}",
                    Path.Combine(raiz, ".mcp.json"), desligado, PluginDono: nome));
        }

        // ---- MCP de ~/.claude.json (chaves que so diferem por caixa: JsonDocument, nunca dicionario)
        var claudeJson = Path.Combine(Home, ".claude.json");
        using (var cj = AbrirJson(claudeJson))
        {
            if (cj is not null && cj.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var srv in ServidoresMcp(cj.RootElement))
                    itens.Add(new ItemInventario("mcp", srv.Nome, srv.Desc, "usuário", claudeJson));

                if (cj.RootElement.TryGetProperty("projects", out var projs) && projs.ValueKind == JsonValueKind.Object)
                    foreach (var proj in projs.EnumerateObject())
                    {
                        if (proj.Value.ValueKind != JsonValueKind.Object) continue;
                        var pasta = Path.GetFileName(proj.Name.TrimEnd('/', '\\'));
                        foreach (var srv in ServidoresMcp(proj.Value))
                            itens.Add(new ItemInventario("mcp", srv.Nome, srv.Desc, $"projeto {pasta}", proj.Name));
                    }
            }
        }

        // ---- skills/agentes de projeto (<raiz>\*\.claude) + contagem de CLAUDE.md
        var claudeMdProjetos = 0;
        foreach (var proj in Subfolders(CodesRoot))
        {
            var nome = Path.GetFileName(proj);
            if (File.Exists(Path.Combine(proj, "CLAUDE.md"))) claudeMdProjetos++;
            var dotClaude = Path.Combine(proj, ".claude");
            if (!Directory.Exists(dotClaude)) continue;
            itens.AddRange(Skills(Path.Combine(dotClaude, "skills"), $"projeto {nome}", null, false, null));
            itens.AddRange(Agentes(Path.Combine(dotClaude, "agents"), $"projeto {nome}", null, false, null));
        }

        // mesmo MCP em varios projetos vira varias linhas com origem diferente — ok; duplicata exata sai
        var unicos = itens.DistinctBy(i => (i.Tipo, i.Nome.ToLowerInvariant(), i.Origem)).ToList();
        return new SetupInventario(unicos, File.Exists(Path.Combine(ClaudeDir, "CLAUDE.md")), claudeMdProjetos);
    }

    // ================= pecas =================

    private static IEnumerable<ItemInventario> Skills(string dir, string origem, string? prefixo, bool desligado, string? dono)
    {
        foreach (var d in Subfolders(dir))
        {
            var md = Path.Combine(d, "SKILL.md");
            if (!File.Exists(md)) continue;
            var (nome, desc) = Frontmatter(md);
            var n = string.IsNullOrWhiteSpace(nome) ? Path.GetFileName(d) : nome;
            yield return new ItemInventario("skill", prefixo is null ? n : $"{prefixo}:{n}", desc, origem, d, desligado, PluginDono: dono);
        }
    }

    private static IEnumerable<ItemInventario> Agentes(string dir, string origem, string? prefixo, bool desligado, string? dono)
    {
        IEnumerable<string> arquivos;
        try { arquivos = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.md").ToList() : []; }
        catch (Exception) { yield break; }
        foreach (var f in arquivos)
        {
            var (nome, desc) = Frontmatter(f);
            var n = string.IsNullOrWhiteSpace(nome) ? Path.GetFileNameWithoutExtension(f) : nome;
            yield return new ItemInventario("agente", prefixo is null ? n : $"{prefixo}:{n}", desc, origem, f, desligado, PluginDono: dono);
        }
    }

    private static IEnumerable<ItemInventario> Hooks(JsonElement hooks, string settingsPath)
    {
        foreach (var ev in hooks.EnumerateObject())
        {
            if (ev.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var grupo in ev.Value.EnumerateArray())
            {
                var matcher = grupo.TryGetProperty("matcher", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                if (!grupo.TryGetProperty("hooks", out var hs) || hs.ValueKind != JsonValueKind.Array) continue;
                foreach (var h in hs.EnumerateArray())
                {
                    var cmd = h.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                    if (h.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
                        cmd += " " + string.Join(" ", a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                    var nome = string.IsNullOrEmpty(matcher) ? ev.Name : $"{ev.Name} · {matcher}";
                    yield return new ItemInventario("hook", nome, cmd.Trim(), "usuário", settingsPath, Medido: false);
                }
            }
        }
    }

    private sealed record Servidor(string Nome, string Desc);

    private static IEnumerable<Servidor> ServidoresMcp(string arquivo)
    {
        using var doc = AbrirJson(arquivo);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return [];
        // .mcp.json de plugin: { "mcpServers": {...} } ou direto { "nome": {...} }
        var lista = doc.RootElement.TryGetProperty("mcpServers", out _)
            ? ServidoresMcp(doc.RootElement).ToList()
            : doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object)
                .Select(p => new Servidor(p.Name, DescreverServidor(p.Value))).ToList();
        return lista;
    }

    /// <summary>Le "mcpServers" do objeto. So o tipo + executavel/host: env, headers e args podem ter
    /// chave de API e nunca vao pra tela.</summary>
    private static IEnumerable<Servidor> ServidoresMcp(JsonElement obj)
    {
        if (!obj.TryGetProperty("mcpServers", out var ms) || ms.ValueKind != JsonValueKind.Object) yield break;
        foreach (var s in ms.EnumerateObject())
            if (s.Value.ValueKind == JsonValueKind.Object)
                yield return new Servidor(s.Name, DescreverServidor(s.Value));
    }

    private static string DescreverServidor(JsonElement v)
    {
        if (v.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
            && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri))
            return $"http · {uri.Host}";
        if (v.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String)
            return $"stdio · {Path.GetFileName(c.GetString() ?? "")}";
        return "";
    }

    /// <summary>(raiz, nome da pasta sem sufixo ~g2) de cada plugin instalado ou sincronizado.</summary>
    private static IEnumerable<(string Raiz, string Nome)> RaizesDePlugin()
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var ip = AbrirJson(Path.Combine(ClaudeDir, "plugins", "installed_plugins.json")))
        {
            if (ip is not null && ip.RootElement.TryGetProperty("plugins", out var ps) && ps.ValueKind == JsonValueKind.Object)
                foreach (var p in ps.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var inst in p.Value.EnumerateArray())
                        if (inst.TryGetProperty("installPath", out var ipath) && ipath.GetString() is { } raiz
                            && Directory.Exists(raiz) && vistos.Add(raiz))
                            yield return (raiz, p.Name.Split('@')[0]);
                }
        }

        foreach (var bucket in Subfolders(Path.Combine(ClaudeDir, "plugins", "synced")))
            foreach (var raiz in Subfolders(bucket))
            {
                var nome = Path.GetFileName(raiz);
                if (nome.StartsWith('.') || !Directory.Exists(Path.Combine(raiz, ".claude-plugin")) || !vistos.Add(raiz)) continue;
                var i = nome.IndexOf('~');
                yield return (raiz, i > 0 ? nome[..i] : nome);
            }
    }

    private static string? NomeDoPlugin(string raiz)
    {
        using var doc = AbrirJson(Path.Combine(raiz, ".claude-plugin", "plugin.json"));
        return doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() : null;
    }

    private static string DescricaoDoPlugin(string raiz)
    {
        using var doc = AbrirJson(Path.Combine(raiz, ".claude-plugin", "plugin.json"));
        return doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString() ?? "" : "";
    }

    /// <summary>name/description do frontmatter YAML (aceita valor em bloco "&gt;"/"|" e aspas).</summary>
    internal static (string Nome, string Desc) Frontmatter(string arquivo)
    {
        string[] linhas;
        try { linhas = File.ReadLines(arquivo).Take(60).ToArray(); }
        catch (Exception) { return ("", ""); }
        if (linhas.Length == 0 || linhas[0].Trim() != "---") return ("", "");

        string nome = "", desc = "";
        for (var i = 1; i < linhas.Length; i++)
        {
            var l = linhas[i];
            if (l.Trim() == "---") break;
            if (l.StartsWith("name:")) nome = Limpar(l[5..]);
            else if (l.StartsWith("description:"))
            {
                var v = l[12..].Trim();
                if (v is "" or ">" or "|" or ">-" or "|-")
                {
                    var partes = new List<string>();
                    while (i + 1 < linhas.Length && (linhas[i + 1].StartsWith(' ') || linhas[i + 1].StartsWith('\t')))
                        partes.Add(linhas[++i].Trim());
                    desc = string.Join(" ", partes);
                }
                else desc = Limpar(v);
            }
        }
        return (nome, desc);
    }

    private static string Limpar(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0]) v = v[1..^1];
        return v.Replace("\\n", " ");
    }

    // ================= mapa do factory =================

    public (IReadOnlyList<MaoOdin> Maos, IReadOnlyList<ScriptFactory> Scripts) LerMapa()
    {
        var maos = new List<MaoOdin>();
        using (var doc = AbrirJson(Path.Combine(AppContext.BaseDirectory, "Assets", "hands.json")))
        {
            if (doc is not null && doc.RootElement.TryGetProperty("maos", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var m in arr.EnumerateArray())
                {
                    var nome = m.TryGetProperty("nome", out var n) ? n.GetString() ?? "" : "";
                    var tags = m.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                        ? string.Join(" · ", t.EnumerateArray().Select(x => x.GetString())) : "";
                    if (nome != "") maos.Add(new MaoOdin(nome, tags));
                }
        }

        var scripts = new List<ScriptFactory>();
        var tools = Path.Combine(global::Factory.Services.AppPaths.Repo, "tools");
        try
        {
            if (Directory.Exists(tools))
                foreach (var f in Directory.EnumerateFiles(tools).Where(f => f.EndsWith(".mjs") || f.EndsWith(".ps1")).Order())
                    scripts.Add(new ScriptFactory(Path.GetFileName(f),
                        PrimeiroComentario(f) is { Length: > 0 } r ? r : "(sem comentário no topo do arquivo)", f));
        }
        catch (Exception) { }
        return (maos, scripts);
    }

    private static string PrimeiroComentario(string arquivo)
    {
        try
        {
            var bloco = false; // dentro de <# ... #> (ps1): a 1a linha de texto ja e o resumo
            foreach (var bruto in File.ReadLines(arquivo).Take(20))
            {
                var l = bruto.Trim();
                if (l == "" || l.StartsWith("#!") || l is "//" or "#" || l.StartsWith(".SYNOPSIS")) continue;
                if (l == "<#") { bloco = true; continue; }
                if (l.StartsWith("<#")) return l[2..].Trim();
                if (l.StartsWith("//")) return l.TrimStart('/').Trim();
                if (l.StartsWith('#')) return l.TrimStart('#').Trim();
                return bloco ? l : "";
            }
        }
        catch (Exception) { }
        return "";
    }

    // ================= joias =================

    public HashSet<string> LerJoias()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = AbrirJson(JoiasFile);
        if (doc is not null && doc.RootElement.TryGetProperty("joias", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var j in arr.EnumerateArray())
                if (j.GetString() is { Length: > 0 } s) set.Add(s);
        return set;
    }

    public void GravarJoias(IEnumerable<string> chaves)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JoiasFile)!);
        var json = JsonSerializer.Serialize(new
        {
            schema = "setup-joias/v1",
            nota = "Joias do setup do Claude Code marcadas a mao na secao SETUP do Factory. Chave = tipo:nome.",
            atualizado = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            joias = chaves.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var tmp = JoiasFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, JoiasFile, overwrite: true);
    }

    // ================= util =================

    private static IEnumerable<string> Subfolders(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).ToList() : []; }
        catch (Exception) { return []; }
    }

    private static JsonDocument? AbrirJson(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return null;
            using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(fs, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception) { return null; }
    }
}
