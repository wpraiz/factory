using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Factory.Services;

public interface ISetupUsage
{
    /// <summary>Ultimo resultado gravado em %LOCALAPPDATA%\Factory\setup-uso.json (cache regeneravel).</summary>
    UsoResumo? Cache();

    /// <summary>Varre ~/.claude/projects/**/*.jsonl em thread de fundo; so rele arquivo cujo mtime/tamanho
    /// mudou. Contagem: Skill-&gt;input.skill, Agent/Task-&gt;input.subagent_type,
    /// mcp__servidor__* -&gt; servidor.</summary>
    Task<UsoResumo> AtualizarAsync(IProgress<string>? progresso, CancellationToken ct);
}

/// <summary>Uso somado de uma chave "tipo:nome" (tipo = skill | agente | mcp).</summary>
public sealed record UsoItem(int Vezes, string Ultimo, int Projetos);

public sealed record UsoResumo(IReadOnlyDictionary<string, UsoItem> Itens, DateTimeOffset Atualizado,
    int Sessoes, string DesdeData);

public sealed class SetupUsage : ISetupUsage
{
    private static readonly string Raiz = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    private static readonly string CacheFile = Path.Combine(
        AppPaths.Cache, "setup-uso.json");

    private readonly SemaphoreSlim _gate = new(1, 1);

    // ---------- formato do cache (1 entrada por .jsonl) ----------
    private sealed class CacheDoc
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "setup-uso-cache/v1";
        [JsonPropertyName("atualizado")] public DateTimeOffset Atualizado { get; set; }
        [JsonPropertyName("arquivos")] public Dictionary<string, Arquivo> Arquivos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Arquivo
    {
        [JsonPropertyName("mtime")] public long Mtime { get; set; }
        [JsonPropertyName("tam")] public long Tamanho { get; set; }
        [JsonPropertyName("proj")] public string Projeto { get; set; } = "";
        [JsonPropertyName("primeiro")] public string Primeiro { get; set; } = "";
        [JsonPropertyName("hits")] public List<Hit> Hits { get; set; } = [];
    }

    private sealed class Hit
    {
        [JsonPropertyName("k")] public string Chave { get; set; } = "";
        [JsonPropertyName("c")] public int Vezes { get; set; }
        [JsonPropertyName("u")] public string Ultimo { get; set; } = "";
    }

    public UsoResumo? Cache()
    {
        var doc = LerCache();
        return doc is null || doc.Arquivos.Count == 0 ? null : Resumir(doc);
    }

    public Task<UsoResumo> AtualizarAsync(IProgress<string>? progresso, CancellationToken ct) =>
        Task.Run(async () =>
        {
            await _gate.WaitAsync(ct);
            try { return Atualizar(progresso, ct); }
            finally { _gate.Release(); }
        }, ct);

    private UsoResumo Atualizar(IProgress<string>? progresso, CancellationToken ct)
    {
        var doc = LerCache() ?? new CacheDoc();
        var atuais = new Dictionary<string, (string Proj, FileInfo Fi)>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(Raiz))
            foreach (var projDir in Directory.EnumerateDirectories(Raiz))
            {
                var proj = Path.GetFileName(projDir);
                IEnumerable<string> arquivos;
                try { arquivos = Directory.EnumerateFiles(projDir, "*.jsonl", SearchOption.AllDirectories).ToList(); }
                catch (Exception) { continue; }
                foreach (var f in arquivos) atuais[f] = (proj, new FileInfo(f));
            }

        // sessao apagada pelo Claude Code (cleanupPeriodDays) sai do cache junto
        foreach (var velho in doc.Arquivos.Keys.Where(k => !atuais.ContainsKey(k)).ToList())
            doc.Arquivos.Remove(velho);

        var mudados = atuais.Where(kv =>
            !doc.Arquivos.TryGetValue(kv.Key, out var a)
            || a.Mtime != kv.Value.Fi.LastWriteTimeUtc.Ticks || a.Tamanho != kv.Value.Fi.Length).ToList();

        var feitos = 0;
        var novos = new ConcurrentDictionary<string, Arquivo>(StringComparer.OrdinalIgnoreCase);
        progresso?.Report(mudados.Count == 0 ? "nada novo nas sessões" : $"lendo {mudados.Count} sessão(ões) nova(s) ou alterada(s)…");

        Parallel.ForEach(mudados, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, kv =>
        {
            var a = LerArquivo(kv.Key, kv.Value.Proj, kv.Value.Fi, ct);
            if (a is not null) novos[kv.Key] = a;
            var n = Interlocked.Increment(ref feitos);
            if (n % 25 == 0) progresso?.Report($"lendo sessões… {n}/{mudados.Count}");
        });

        foreach (var kv in novos) doc.Arquivos[kv.Key] = kv.Value;
        doc.Atualizado = DateTimeOffset.Now;
        GravarCache(doc);
        return Resumir(doc);
    }

    /// <summary>Stream linha a linha; so faz parse de linha com "tool_use" (tool_result tem
    /// "tool_use_id", que nao casa com as aspas).</summary>
    private static Arquivo? LerArquivo(string caminho, string proj, FileInfo fi, CancellationToken ct)
    {
        var hits = new Dictionary<string, Hit>(StringComparer.Ordinal);
        var primeiro = "";
        try
        {
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            using var sr = new StreamReader(fs);
            string? linha;
            while ((linha = sr.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (primeiro == "") primeiro = Timestamp(linha);
                if (!linha.Contains("\"tool_use\"", StringComparison.Ordinal)) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(linha); }
                catch (JsonException) { continue; }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    var t = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                        ? (ts.GetString() ?? "") : "";
                    if (t.Length > 10) t = t[..10];
                    if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object
                        || !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;

                    foreach (var c in content.EnumerateArray())
                    {
                        if (c.ValueKind != JsonValueKind.Object || Str(c, "type") != "tool_use") continue;
                        var nome = Str(c, "name");
                        var input = c.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Object ? inp : default;
                        string? chave = null;
                        if (nome == "Skill" && input.ValueKind == JsonValueKind.Object && Str(input, "skill") is { Length: > 0 } sk)
                            chave = "skill:" + sk;
                        else if (nome is "Agent" or "Task" && input.ValueKind == JsonValueKind.Object && Str(input, "subagent_type") is { Length: > 0 } ag)
                            chave = "agente:" + ag;
                        else if (nome.StartsWith("mcp__", StringComparison.Ordinal))
                        {
                            var partes = nome.Split("__");
                            if (partes.Length > 1 && partes[1] != "") chave = "mcp:" + partes[1];
                        }
                        if (chave is null) continue;
                        if (!hits.TryGetValue(chave, out var h)) hits[chave] = h = new Hit { Chave = chave };
                        h.Vezes++;
                        if (string.CompareOrdinal(t, h.Ultimo) > 0) h.Ultimo = t;
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; } // arquivo sendo escrito/travado: tenta de novo no proximo refresh

        return new Arquivo
        {
            Mtime = fi.LastWriteTimeUtc.Ticks,
            Tamanho = fi.Length,
            Projeto = proj,
            Primeiro = primeiro,
            Hits = hits.Values.ToList(),
        };
    }

    /// <summary>Data (yyyy-MM-dd) do 1o "timestamp" da linha, sem parse de JSON.</summary>
    private static string Timestamp(string linha)
    {
        const string k = "\"timestamp\":\"";
        var i = linha.IndexOf(k, StringComparison.Ordinal);
        return i >= 0 && i + k.Length + 10 <= linha.Length ? linha.Substring(i + k.Length, 10) : "";
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static UsoResumo Resumir(CacheDoc doc)
    {
        var soma = new Dictionary<string, (int Vezes, string Ultimo, HashSet<string> Projs)>(StringComparer.OrdinalIgnoreCase);
        var desde = "";
        foreach (var a in doc.Arquivos.Values)
        {
            if (a.Primeiro.Length == 10 && (desde == "" || string.CompareOrdinal(a.Primeiro, desde) < 0)) desde = a.Primeiro;
            foreach (var h in a.Hits)
            {
                if (!soma.TryGetValue(h.Chave, out var s)) s = (0, "", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                s.Vezes += h.Vezes;
                if (string.CompareOrdinal(h.Ultimo, s.Ultimo) > 0) s.Ultimo = h.Ultimo;
                s.Projs.Add(a.Projeto);
                soma[h.Chave] = s;
            }
        }

        var itens = soma.ToDictionary(kv => kv.Key, kv => new UsoItem(kv.Value.Vezes, kv.Value.Ultimo, kv.Value.Projs.Count),
            StringComparer.OrdinalIgnoreCase);
        return new UsoResumo(itens, doc.Atualizado, doc.Arquivos.Count, desde);
    }

    private static CacheDoc? LerCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            var doc = JsonSerializer.Deserialize<CacheDoc>(File.ReadAllText(CacheFile));
            if (doc is null || doc.Schema != "setup-uso-cache/v1") return null;
            // o deserializador cria o dicionario sem o comparer: recria case-insensitive
            doc.Arquivos = new Dictionary<string, Arquivo>(doc.Arquivos, StringComparer.OrdinalIgnoreCase);
            return doc;
        }
        catch (Exception) { return null; } // cache corrompido: regenera
    }

    private static void GravarCache(CacheDoc doc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            var tmp = CacheFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc));
            File.Move(tmp, CacheFile, overwrite: true);
        }
        catch (Exception) { }
    }
}
