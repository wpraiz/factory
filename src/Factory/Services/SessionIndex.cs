using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Factory.Services;

/// <summary>Um arquivo que uma tool tocou numa sessao (Read, Write, Edit, Glob…).</summary>
public sealed record TouchedFile(string Tool, string Path)
{
    public bool IsEdit => Tool is "Write" or "Edit" or "MultiEdit" or "NotebookEdit";
}

/// <summary>Uma sessao do Claude Code ja digerida: titulos, prompts, texto e arquivos tocados.</summary>
public sealed class SessionRecord
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string? CustomTitle { get; set; }
    public string? AiTitle { get; set; }
    public string FirstPrompt { get; set; } = "";
    /// <summary>Prompts humanos + texto do assistente + args de tools, concatenados (com teto).</summary>
    public string Text { get; set; } = "";
    /// <summary>Arquivos que apareceram em tool_use, com o nome da tool (Write/Edit = mudou algo).</summary>
    public List<TouchedFile> Touched { get; set; } = [];
    public int Turns { get; set; }
    public DateTimeOffset LastWrite { get; set; }
    public long SizeBytes { get; set; }
    public long MtimeTicks { get; set; }

    // ---- v2 ----
    /// <summary>Branch registrado pelo Claude Code na sessao (campo gitBranch do jsonl).</summary>
    public string GitBranch { get; set; } = "";
    /// <summary>Branches citados em comandos git (checkout/switch/push).</summary>
    public List<string> Branches { get; set; } = [];
    public bool IsSubagent { get; set; }
    /// <summary>Id da sessao-mae quando IsSubagent.</summary>
    public string? ParentId { get; set; }
    /// <summary>Ultima mensagem humana e ultima resposta (ate 600 chars) — insumo do briefing.</summary>
    public string LastHuman { get; set; } = "";
    public string LastAssistant { get; set; } = "";
    public DateTimeOffset? FirstTs { get; set; }
    public DateTimeOffset? LastTs { get; set; }

    [JsonIgnore]
    public string Title => CustomTitle ?? AiTitle ?? (FirstPrompt.Length > 0 ? FirstPrompt : ShortId);
    [JsonIgnore] public string ShortId => Id.Length > 8 ? Id[..8] : Id;
    /// <summary>Compatibilidade: caminhos sem o nome da tool.</summary>
    [JsonIgnore] public IEnumerable<string> Paths => Touched.Select(t => t.Path);
    /// <summary>Sessao com o arquivo mexido ha menos de 2 min = provavelmente aberta agora. Um stat, sem re-parse.</summary>
    [JsonIgnore]
    public bool IsLive
    {
        get
        {
            try { return DateTime.Now - File.GetLastWriteTime(FilePath) < TimeSpan.FromMinutes(2); }
            catch (Exception) { return false; }
        }
    }
    [JsonIgnore]
    public TimeSpan? Duration => FirstTs is { } f && LastTs is { } l && l > f ? l - f : null;
}

public interface ISessionIndex
{
    string Root { get; }
    bool IsReady { get; }
    string Progress { get; }
    event EventHandler? Updated;

    /// <summary>Constroi/atualiza o indice (incremental por mtime). Idempotente; seguro chamar varias vezes.</summary>
    Task EnsureBuiltAsync(CancellationToken ct = default);

    IReadOnlyList<SessionRecord> All { get; }

    /// <summary>Sessoes abertas na pasta (slug igual ou subpasta). Funciona antes do indice ficar pronto (so metadados).</summary>
    IReadOnlyList<SessionRecord> ForProject(string projectPath, bool includeSubagents = false);

    /// <summary>Sessoes de OUTRAS pastas que citam o termo ou tocaram arquivos do projeto. Exige indice pronto.</summary>
    IReadOnlyList<(SessionRecord Session, int Hits)> Mentions(Regex alias, string? projectPath);
}

/// <summary>
/// Indexa ~/.claude/projects/**/*.jsonl uma vez (cache em %LOCALAPPDATA%\Factory) e
/// depois so reprocessa o que mudou. E o que permite achar um projeto que nasceu numa
/// sessao aberta em <raiz> (caso real: wifi-radar tinha 0 sessoes na pasta propria).
/// v2: guarda tool por arquivo, branch, timestamps, ultimas mensagens e inclui sub-agentes.
/// </summary>
public sealed partial class SessionIndex : ISessionIndex
{
    private const int CacheVersion = 2;
    private const int TextCap = 160_000;
    private const int TouchedCap = 400;
    private const int TailCap = 600;

    private sealed class CacheEnvelope
    {
        public int Version { get; set; }
        public List<SessionRecord> Items { get; set; } = [];
    }

    public string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    private static readonly string CacheFile = Path.Combine(
        AppPaths.Cache, "sessions-index.json");

    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private List<SessionRecord> _all = [];

    public bool IsReady { get; private set; }
    public string Progress { get; private set; } = "";
    public event EventHandler? Updated;
    public IReadOnlyList<SessionRecord> All => _all;

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"git\s+(?:checkout|switch)\s+(?:-b\s+|-c\s+)?([A-Za-z0-9._/\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BranchRx();

    public static string Slug(string path) => NonAlnum().Replace(path.TrimEnd('\\', '/'), "-");

    private static bool SlugBelongs(string slug, string projectSlug) =>
        slug.Equals(projectSlug, StringComparison.OrdinalIgnoreCase)
        || slug.StartsWith(projectSlug + "-", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<SessionRecord> ForProject(string projectPath, bool includeSubagents = false)
    {
        var slug = Slug(projectPath);
        if (IsReady)
            return _all.Where(r => SlugBelongs(r.Slug, slug) && (includeSubagents || !r.IsSubagent))
                .OrderByDescending(r => r.LastWrite).ToList();

        // Antes do indice: so o que da pra saber olhando o disco (sem sub-agentes).
        if (!Directory.Exists(Root)) return [];
        var list = new List<SessionRecord>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(dir);
            if (!SlugBelongs(name, slug)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl"))
            {
                var fi = new FileInfo(f);
                list.Add(new SessionRecord
                {
                    Id = Path.GetFileNameWithoutExtension(f), Slug = name, FilePath = f,
                    LastWrite = fi.LastWriteTime, SizeBytes = fi.Length,
                });
            }
        }
        return list.OrderByDescending(r => r.LastWrite).ToList();
    }

    public IReadOnlyList<(SessionRecord Session, int Hits)> Mentions(Regex alias, string? projectPath)
    {
        if (!IsReady) return [];
        var slug = projectPath is null ? null : Slug(projectPath);
        var result = new List<(SessionRecord, int)>();
        foreach (var r in _all)
        {
            if (slug is not null && SlugBelongs(r.Slug, slug)) continue;
            var hits = alias.Count(r.Text);
            if (projectPath is not null)
                hits += r.Touched.Count(t => t.Path.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase)) * 5;
            if (r.IsSubagent) hits /= 2; // sub-agente repete o contexto da mae: pesa metade
            if (hits > 0) result.Add((r, hits));
        }
        return result.OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item1.LastWrite).ToList();
    }

    public async Task EnsureBuiltAsync(CancellationToken ct = default)
    {
        if (!await _buildGate.WaitAsync(0, ct)) return; // ja tem build rodando
        try
        {
            await Task.Run(() => Build(ct), ct);
        }
        finally { _buildGate.Release(); }
    }

    private void Build(CancellationToken ct)
    {
        if (!Directory.Exists(Root)) { IsReady = true; Report("sem ~/.claude/projects"); return; }

        var cache = LoadCache().ToDictionary(r => r.FilePath, StringComparer.OrdinalIgnoreCase);
        // Sessoes de topo (<slug>\<id>.jsonl) + sub-agentes (<slug>\<id>\subagents\agent-*.jsonl).
        // Fora disso ha transcricoes de workflows/tool-results que nao sao sessoes — ficam de fora.
        var files = Directory.EnumerateDirectories(Root)
            .SelectMany(slugDir =>
                Directory.EnumerateFiles(slugDir, "*.jsonl").Concat(
                    Directory.EnumerateDirectories(slugDir).SelectMany(sessionDir =>
                    {
                        var sub = Path.Combine(sessionDir, "subagents");
                        return Directory.Exists(sub) ? Directory.EnumerateFiles(sub, "*.jsonl") : [];
                    })))
            .Select(f => new FileInfo(f))
            .ToList();

        var fresh = new List<SessionRecord>();
        var todo = new List<FileInfo>();
        foreach (var fi in files)
        {
            if (cache.TryGetValue(fi.FullName, out var rec) && rec.MtimeTicks == fi.LastWriteTimeUtc.Ticks && rec.SizeBytes == fi.Length)
                fresh.Add(rec);
            else
                todo.Add(fi);
        }

        var done = 0;
        var parsed = new List<SessionRecord>();
        var lockObj = new object();
        Report(todo.Count == 0 ? "" : $"indexando sessoes 0/{todo.Count}");

        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, fi =>
        {
            var rec = Parse(fi);
            lock (lockObj)
            {
                parsed.Add(rec);
                done++;
                if (done % 3 == 0 || done == todo.Count) Report($"indexando sessoes {done}/{todo.Count}");
            }
        });

        var all = fresh.Concat(parsed).ToList();

        // Sub-agente nao traz cwd: herda da mae.
        var byId = all.Where(r => !r.IsSubagent).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var s in all.Where(r => r.IsSubagent && r.Cwd.Length == 0 && r.ParentId is not null))
            if (byId.TryGetValue(s.ParentId!, out var parent)) s.Cwd = parent.Cwd;

        _all = all.OrderByDescending(r => r.LastWrite).ToList();
        IsReady = true;
        Report("");
        if (todo.Count > 0) SaveCache(_all);
    }

    private void Report(string p)
    {
        Progress = p;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static SessionRecord Parse(FileInfo fi)
    {
        var dirName = Path.GetFileName(fi.DirectoryName) ?? "";
        var isSub = dirName.Equals("subagents", StringComparison.OrdinalIgnoreCase);
        var sessionDir = isSub ? Directory.GetParent(fi.DirectoryName!) : null;

        var rec = new SessionRecord
        {
            Id = Path.GetFileNameWithoutExtension(fi.Name),
            // slug = pasta de primeiro nivel abaixo de Root
            Slug = isSub ? Path.GetFileName(sessionDir!.Parent!.FullName) : dirName,
            FilePath = fi.FullName,
            LastWrite = fi.LastWriteTime,
            SizeBytes = fi.Length,
            MtimeTicks = fi.LastWriteTimeUtc.Ticks,
            IsSubagent = isSub,
            ParentId = isSub ? Path.GetFileName(sessionDir!.FullName) : null,
        };
        var text = new StringBuilder();
        var touched = new List<TouchedFile>();
        var branches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var reader = new StreamReader(fi.FullName);
            while (reader.ReadLine() is { } line)
            {
                if (line.Contains("\"type\":\"custom-title\"", StringComparison.Ordinal))
                    rec.CustomTitle = Prop(line, "customTitle") ?? rec.CustomTitle;
                else if (line.Contains("\"type\":\"ai-title\"", StringComparison.Ordinal))
                    rec.AiTitle = Prop(line, "aiTitle") ?? rec.AiTitle;
                else if (line.Contains("\"type\":\"user\"", StringComparison.Ordinal))
                    Ingest(line, rec, text, touched, branches, isUser: true);
                else if (line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal))
                    Ingest(line, rec, text, touched, branches, isUser: false);
            }
        }
        catch (IOException) { /* sessao viva com lock: fica com o que deu pra ler */ }

        rec.Text = text.ToString();
        rec.Touched = touched.DistinctBy(t => (t.Tool, t.Path.ToLowerInvariant())).Take(TouchedCap).ToList();
        rec.Branches = branches.Take(20).ToList();
        return rec;
    }

    private static void Ingest(string line, SessionRecord rec, StringBuilder text, List<TouchedFile> touched,
        HashSet<string> branches, bool isUser)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (rec.Cwd.Length == 0 && root.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
                rec.Cwd = cwd.GetString() ?? "";
            if (rec.GitBranch.Length == 0 && root.TryGetProperty("gitBranch", out var gb) && gb.ValueKind == JsonValueKind.String)
                rec.GitBranch = gb.GetString() ?? "";
            if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(ts.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var when))
            {
                rec.FirstTs ??= when;
                rec.LastTs = when;
            }

            if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content)) return;

            if (content.ValueKind == JsonValueKind.String)
            {
                AddText(content.GetString(), rec, text, isUser);
                return;
            }
            if (content.ValueKind != JsonValueKind.Array) return;

            foreach (var part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out var t)) continue;
                switch (t.GetString())
                {
                    case "text":
                        if (part.TryGetProperty("text", out var tx)) AddText(tx.GetString(), rec, text, isUser);
                        break;
                    case "tool_use":
                        var tool = part.TryGetProperty("name", out var nm) ? nm.GetString() ?? "?" : "?";
                        if (part.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
                            foreach (var kv in input.EnumerateObject())
                            {
                                if (kv.Value.ValueKind != JsonValueKind.String) continue;
                                var v = kv.Value.GetString() ?? "";
                                if (kv.Name is "file_path" or "path" or "notebook_path")
                                {
                                    if (touched.Count < TouchedCap * 3) touched.Add(new TouchedFile(tool, v));
                                    Append(text, v);
                                }
                                else if (kv.Name is "command" or "pattern" or "query" or "description")
                                {
                                    Append(text, v.Length > 400 ? v[..400] : v);
                                    if (kv.Name == "command")
                                        foreach (Match m in BranchRx().Matches(v)) branches.Add(m.Groups[1].Value);
                                }
                            }
                        break;
                }
            }
        }
        catch (JsonException) { }
    }

    private static void AddText(string? s, SessionRecord rec, StringBuilder text, bool isUser)
    {
        if (string.IsNullOrWhiteSpace(s)) return;
        var clean = Regex.Replace(s, @"\s+", " ").Trim();
        if (clean.StartsWith('<')) return; // system-reminder, command-name etc.
        var tail = clean.Length > TailCap ? clean[..TailCap] + "…" : clean;
        if (isUser)
        {
            rec.Turns++;
            if (rec.FirstPrompt.Length == 0) rec.FirstPrompt = clean.Length > 160 ? clean[..160] + "…" : clean;
            rec.LastHuman = tail;
        }
        else
        {
            rec.LastAssistant = tail;
        }
        Append(text, clean.Length > 4000 ? clean[..4000] : clean);
    }

    private static void Append(StringBuilder sb, string s)
    {
        if (sb.Length > TextCap) return;
        sb.Append(s).Append('\n');
    }

    private static string? Prop(string line, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static List<SessionRecord> LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return [];
            // v1 era um array puro: falha ao desserializar o envelope → rebuild completo, uma vez.
            var env = JsonSerializer.Deserialize<CacheEnvelope>(File.ReadAllText(CacheFile));
            return env is { Version: CacheVersion } ? env.Items : [];
        }
        catch (Exception) { return []; }
    }

    private static void SaveCache(List<SessionRecord> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(new CacheEnvelope { Version = CacheVersion, Items = all }));
        }
        catch (Exception) { /* cache e conveniencia; sem ele so fica mais lento */ }
    }
}
