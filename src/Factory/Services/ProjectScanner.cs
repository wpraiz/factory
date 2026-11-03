using Factory.Models;

namespace Factory.Services;

public interface IProjectScanner
{
    Task<List<ProjectInfo>> ScanAsync(CancellationToken ct = default);
}

/// <summary>
/// Varre as pastas de primeiro nivel de <see cref="Root"/> (<raiz> por padrao;
/// FACTORY_ROOT sobrescreve). Nao desce alem: o alvo e o inventario, nao o disco.
/// </summary>
public sealed class ProjectScanner : IProjectScanner
{
    public static string Root =>
        AppPaths.Root;

    private readonly IStatusParser _status;
    private readonly ISessionIndex _sessions;
    private readonly IManifestStore _manifests;

    public ProjectScanner(IStatusParser status, ISessionIndex sessions, IManifestStore manifests)
    {
        _status = status;
        _sessions = sessions;
        _manifests = manifests;
    }

    public Task<List<ProjectInfo>> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var result = new List<ProjectInfo>();
        if (!Directory.Exists(Root)) return result;

        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;

            var statusFile = Path.Combine(dir, "STATUS.md");
            var claudeFile = Path.Combine(dir, "CLAUDE.md");
            var readmeFile = Path.Combine(dir, "README.md");

            var info = new ProjectInfo
            {
                Name = name,
                Path = dir,
                HasStatus = File.Exists(statusFile),
                HasClaudeMd = File.Exists(claudeFile),
                HasReadme = File.Exists(readmeFile),
                HasGit = Directory.Exists(Path.Combine(dir, ".git")),
                AutoTags = InferTags(name, dir),
            };

            var touch = Directory.GetLastWriteTime(dir);
            foreach (var f in new[] { statusFile, claudeFile, readmeFile })
                if (File.Exists(f)) touch = Max(touch, File.GetLastWriteTime(f));

            if (info.HasStatus)
            {
                var doc = _status.Parse(SafeRead(statusFile));
                // STATUS fora do formato da skill nao declara "Atualizado:"; o mtime
                // do arquivo e a melhor aproximacao — o '~' marca que foi inferido.
                info.StatusUpdated = doc.Updated.Length > 0
                    ? doc.Updated
                    : File.GetLastWriteTime(statusFile).ToString("yyyy-MM-dd") + "~";
                info.NextAction = doc.NextAction;
                info.Blockers = doc.Blockers;
            }

            var sessions = _sessions.ForProject(dir);
            info.SessionCount = sessions.Count;
            if (sessions.Count > 0)
            {
                info.LastSession = sessions[0].LastWrite;
                touch = Max(touch, sessions[0].LastWrite.LocalDateTime);
            }

            info.Manifest = _manifests.Load(dir);
            info.LastTouch = touch;
            result.Add(info);
        }

        return result.OrderByDescending(p => p.LastTouch).ToList();
    }, ct);

    /// <summary>Tags baratas, so olhando nome e raiz da pasta. Nada de descer na arvore.</summary>
    private static List<string> InferTags(string name, string dir)
    {
        var tags = new List<string>();
        string[] files, dirs;
        try
        {
            files = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()!;
            dirs = Directory.GetDirectories(dir).Select(Path.GetFileName).ToArray()!;
        }
        catch (Exception) { return tags; }

        bool Any(Func<string, bool> pred) => files.Any(pred) || dirs.Any(pred);
        bool Ext(params string[] exts) => files.Any(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        bool Has(params string[] names) => Any(f => names.Contains(f, StringComparer.OrdinalIgnoreCase));

        if (Ext(".py") || Has("requirements.txt", "pyproject.toml")) tags.Add("python");
        if (Has("package.json")) tags.Add("node");
        if (Ext(".csproj", ".sln") || Has("Directory.Build.props")) tags.Add("dotnet");
        if (Ext(".php") || Has("wp-content", "wp-config.php", "wp")) tags.Add("wordpress");
        if (Ext(".ps1")) tags.Add("powershell");
        if (Has("Dockerfile", "docker-compose.yml")) tags.Add("docker");
        if (Ext(".db", ".sqlite", ".sqlite3")) tags.Add("sqlite");
        if (Has("apps", "packages")) tags.Add("monorepo");
        if (Has(".claude", "CLAUDE.md")) tags.Add("claude");
        if (Ext(".ipynb")) tags.Add("notebook");
        if (tags.Count == 0 && files.Length > 0 && files.All(f => Path.GetExtension(f) is ".md" or ".txt" or ".pdf" or ".docx"))
            tags.Add("docs");

        // manifestos da raiz (package.json, csproj, requirements…) dao tags mais precisas
        tags.AddRange(StackDetector.Detect(dir).Tags);

        var lower = name.ToLowerInvariant();
        if (lower.StartsWith('_')) tags.Add("infra");
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static string SafeRead(string file)
    {
        try { return File.ReadAllText(file); }
        catch (IOException) { return ""; }
    }
}
