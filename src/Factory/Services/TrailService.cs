using System.Text.RegularExpressions;
using Factory.Models;
using Microsoft.Data.Sqlite;

namespace Factory.Services;

public interface ITrailService
{
    /// <summary>
    /// "Tudo associado" a um termo: perfil da pasta (se houver), bancos SQLite, sessoes
    /// proprias e que citam, mencoes em memoria / wiki / .factory / outros projetos.
    /// </summary>
    Task<TrailReport> ExploreAsync(string term, string? projectPath, IProgress<string>? progress, CancellationToken ct);
}

public sealed class TrailService : ITrailService
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "__pycache__", ".venv", "venv", ".next",
        "dist", "build", ".obsidian", "vendor", ".cache", "packages", ".idea", ".vs",
    };
    private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".txt", ".ps1", ".py", ".json", ".js", ".ts", ".yml", ".yaml", ".csv", ".html", ".toml", ".cfg", ".ini", ".sh" };
    private static readonly HashSet<string> DbExts = new(StringComparer.OrdinalIgnoreCase) { ".db", ".sqlite", ".sqlite3" };
    private static readonly string[] EntryNames =
        { "README.md", "STATUS.md", "CLAUDE.md", "package.json", "requirements.txt", "pyproject.toml", "docker-compose.yml", "Makefile", "index.html", "main.py", "app.py", "main.js", "index.js" };

    private const int MaxFilesWalk = 25_000;
    private const long MaxDocBytes = 3_000_000;

    private readonly ISessionIndex _index;
    private readonly IGitProbe _git;
    private readonly ISystemProbe _system;

    public TrailService(ISessionIndex index, IGitProbe git, ISystemProbe system)
    {
        _index = index;
        _git = git;
        _system = system;
    }

    /// <summary>"wifi-radar" → casa wifi-radar, wifi_radar, wifi radar, wifiradar; nao casa "xwifi-radarx".</summary>
    public static Regex AliasRegex(string term)
    {
        var parts = Regex.Split(term.Trim(), "[-_ ]+").Where(p => p.Length > 0).Select(Regex.Escape);
        var core = string.Join("[-_ ]?", parts);
        return new Regex($"(?<![A-Za-z0-9]){core}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public async Task<TrailReport> ExploreAsync(string term, string? projectPath, IProgress<string>? progress, CancellationToken ct)
    {
        var report = new TrailReport { IndexWasReady = _index.IsReady };
        var alias = AliasRegex(term);

        if (projectPath is not null && Directory.Exists(projectPath))
        {
            progress?.Report("pasta…");
            report.Folder = await Task.Run(() => Profile(projectPath, ct), ct);
            var remote = await _git.RunAsync(projectPath, "remote -v", ct);
            var log = await _git.RunAsync(projectPath, "log --oneline -6", ct);
            report.Folder = report.Folder with
            {
                GitRemote = FirstLine(remote ?? ""),
                GitLog = (log ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList(),
                Stack = StackDetector.Detect(projectPath),
            };

            progress?.Report("bancos…");
            foreach (var db in FindDatabases(projectPath))
            {
                ct.ThrowIfCancellationRequested();
                report.Databases.Add(await Task.Run(() => InspectDb(db), ct));
            }

            report.OwnSessions = _index.ForProject(projectPath).ToList();

            progress?.Report("sistema…");
            try { report.System = await _system.ProbeAsync(projectPath, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { report.System = new SystemReport(); report.System.Errors.Add(ex.Message); }
        }

        progress?.Report("sessoes que citam…");
        report.MentionSessions = await Task.Run(() => _index.Mentions(alias, projectPath).ToList(), ct);

        progress?.Report("memoria, wiki, .factory, projetos…");
        await Task.Run(() =>
        {
            foreach (var (source, files) in DocSources(projectPath))
            {
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var hit = Grep(source, f, alias);
                    if (hit is not null) report.DocMentions.Add(hit);
                    if (report.DocMentions.Count >= 200) return;
                }
            }
        }, ct);
        report.DocMentions.Sort((a, b) => b.Hits.CompareTo(a.Hits));

        progress?.Report("");
        return report;
    }

    // ---------- pasta ----------

    private static FolderProfile Profile(string root, CancellationToken ct)
    {
        int files = 0, dirs = 0;
        long bytes = 0;
        var exts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var recent = new List<RecentFile>();
        var entries = new List<string>();
        DateTime? oldest = null, newest = null;
        var truncated = false;

        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();
            IEnumerable<string> subdirs, subfiles;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
                subfiles = Directory.EnumerateFiles(dir);
            }
            catch (Exception) { continue; }

            foreach (var d in subdirs)
            {
                var name = Path.GetFileName(d);
                if (SkipDirs.Contains(name) || name.StartsWith('.')) continue;
                dirs++;
                if (depth < 6) stack.Push((d, depth + 1));
            }
            foreach (var f in subfiles)
            {
                if (++files > MaxFilesWalk) { truncated = true; break; }
                FileInfo fi;
                try { fi = new FileInfo(f); } catch (Exception) { continue; }
                bytes += fi.Length;
                var ext = fi.Extension.Length == 0 ? "(sem ext)" : fi.Extension.TrimStart('.').ToLowerInvariant();
                exts[ext] = exts.GetValueOrDefault(ext) + 1;
                var m = fi.LastWriteTime;
                if (oldest is null || m < oldest) oldest = m;
                if (newest is null || m > newest) newest = m;

                var rel = Path.GetRelativePath(root, f);
                if (recent.Count < 8 || m > recent[^1].Modified)
                {
                    recent.Add(new RecentFile(rel, m, fi.Length));
                    recent.Sort((a, b) => b.Modified.CompareTo(a.Modified));
                    if (recent.Count > 8) recent.RemoveAt(8);
                }
                if (depth == 0 && (EntryNames.Contains(fi.Name, StringComparer.OrdinalIgnoreCase)
                                   || fi.Extension is ".py" or ".ps1" or ".sln" or ".csproj"))
                    entries.Add(fi.Name);
                else if (depth <= 2 && fi.Extension is ".csproj" or ".sln")
                    entries.Add(rel);
            }
            if (truncated) break;
        }

        return new FolderProfile(
            files, dirs, bytes,
            exts.OrderByDescending(kv => kv.Value).Take(8).Select(kv => (kv.Key, kv.Value)).ToList(),
            recent, entries, "", [], oldest, newest, truncated);
    }

    private static IEnumerable<string> FindDatabases(string root)
    {
        var found = new List<string>();
        void Walk(string dir, int depth)
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); } catch (Exception) { return; }
            foreach (var e in entries)
            {
                if (Directory.Exists(e))
                {
                    var name = Path.GetFileName(e);
                    if (depth < 2 && !SkipDirs.Contains(name) && !name.StartsWith('.')) Walk(e, depth + 1);
                }
                else if (DbExts.Contains(Path.GetExtension(e)) && found.Count < 6)
                    found.Add(e);
            }
        }
        Walk(root, 0);
        return found;
    }

    private static DbInfo InspectDb(string path)
    {
        long size = 0;
        try { size = new FileInfo(path).Length; } catch (Exception) { }
        var tables = new List<DbTable>();
        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
            con.Open();
            var names = new List<string>();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "select name from sqlite_master where type='table' and name not like 'sqlite_%' order by name";
                using var r = cmd.ExecuteReader();
                while (r.Read()) names.Add(r.GetString(0));
            }
            foreach (var n in names.Take(30))
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"select count(*) from \"{n.Replace("\"", "\"\"")}\"";
                tables.Add(new DbTable(n, (long)(cmd.ExecuteScalar() ?? 0L)));
            }
            return new DbInfo(path, size, tables, null);
        }
        catch (Exception ex)
        {
            return new DbInfo(path, size, tables, ex.Message);
        }
    }

    // ---------- docs ----------

    private IEnumerable<(string Source, IEnumerable<string> Files)> DocSources(string? projectPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claude = Path.Combine(home, ".claude");

        yield return ("memoria", MemoryFiles(claude));
        yield return ("wiki", Walk(AppPaths.Wiki, 8, f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)));
        yield return (".factory", Walk(AppPaths.Dados, 6, f => DocExts.Contains(Path.GetExtension(f))));
        yield return ("projetos", OtherProjectRoots(projectPath));
    }

    private IEnumerable<string> MemoryFiles(string claude)
    {
        var global = Path.Combine(claude, "CLAUDE.md");
        if (File.Exists(global)) yield return global;
        var projects = Path.Combine(claude, "projects");
        if (!Directory.Exists(projects)) yield break;
        foreach (var d in Directory.EnumerateDirectories(projects))
        {
            var mem = Path.Combine(d, "memory");
            if (!Directory.Exists(mem)) continue;
            foreach (var f in Directory.EnumerateFiles(mem, "*.md")) yield return f;
        }
    }

    private static IEnumerable<string> OtherProjectRoots(string? projectPath)
    {
        if (!Directory.Exists(ProjectScanner.Root)) yield break;
        foreach (var d in Directory.EnumerateDirectories(ProjectScanner.Root))
        {
            if (projectPath is not null && d.Equals(projectPath, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileName(d);
            if (name is "wiki" or ".factory") continue; // ja cobertos
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(d); } catch (Exception) { continue; }
            foreach (var f in files)
                if (DocExts.Contains(Path.GetExtension(f))) yield return f;
        }
    }

    private static IEnumerable<string> Walk(string root, int maxDepth, Func<string, bool> accept)
    {
        if (!Directory.Exists(root)) yield break;
        var stack = new Stack<(string, int)>();
        stack.Push((root, 0));
        var count = 0;
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            IEnumerable<string> subdirs, files;
            try { subdirs = Directory.EnumerateDirectories(dir); files = Directory.EnumerateFiles(dir); }
            catch (Exception) { continue; }
            foreach (var d in subdirs)
            {
                var name = Path.GetFileName(d);
                if (depth < maxDepth && !SkipDirs.Contains(name) && !name.StartsWith('.')) stack.Push((d, depth + 1));
            }
            foreach (var f in files)
            {
                if (!accept(f)) continue;
                if (++count > 6000) yield break;
                yield return f;
            }
        }
    }

    private static MentionHit? Grep(string source, string file, Regex alias)
    {
        FileInfo fi;
        try { fi = new FileInfo(file); if (fi.Length > MaxDocBytes) return null; } catch (Exception) { return null; }
        string text;
        try { text = File.ReadAllText(file); } catch (Exception) { return null; }

        var m = alias.Match(text);
        if (!m.Success) return null;
        var hits = alias.Count(text);

        var start = Math.Max(0, m.Index - 70);
        var len = Math.Min(text.Length - start, 170);
        var snippet = Regex.Replace(text.Substring(start, len), @"\s+", " ").Trim();

        return new MentionHit
        {
            Source = source, Path = file, Hits = hits,
            Snippet = (start > 0 ? "…" : "") + snippet + (start + len < text.Length ? "…" : ""),
            Modified = fi.LastWriteTime,
        };
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return (i < 0 ? s : s[..i]).Trim();
    }
}
