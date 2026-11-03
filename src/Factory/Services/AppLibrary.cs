using System.Diagnostics;
using System.Management;
using System.Text.Encodings.Web;
using System.Text.Json;
using Factory.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Factory.Services;

public interface IAppLibrary
{
    /// <summary>Grid salvo (ordem = ordem do grid). Vazio na 1ª vez → <see cref="Seed"/>.</summary>
    List<AppTile> Load();
    Task SaveAsync(IEnumerable<AppTile> tiles);
    /// <summary>First run: the user's tools that exist on disk right now.</summary>
    List<AppTile> Seed();
    /// <summary>Tudo que da pra virar tile: seeds + atalhos do Desktop/Menu Iniciar + projetos com START.</summary>
    Task<List<AppTile>> CandidatesAsync(IReadOnlyList<ProjectInfo> projects, CancellationToken ct = default);
    AppTile FromFile(string path);
    void Launch(AppTile tile);
    /// <summary>Uma passada de WMI; marca IsRunning em cada tile (chamar na thread de UI: so seta propriedades).</summary>
    Task RefreshRunningAsync(IReadOnlyList<AppTile> tiles, CancellationToken ct = default);
    /// <summary>Icone real (thumbnail do shell) como stream; null se nao houver.</summary>
    Task<IRandomAccessStream?> IconStreamAsync(AppTile tile);
    string ConfigFile { get; }
}

public sealed class AppLibrary : IAppLibrary
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] SkipLnk = ["uninstall", "desinstal", "help", "ajuda", "readme", "website", "web site", "documentation", "release notes", "license"];

    private readonly ILauncher _launcher;
    private readonly Dictionary<string, (string Target, string Args, string Dir)> _lnkCache = new(StringComparer.OrdinalIgnoreCase);

    public string ConfigFile { get; } = Path.Combine(
        AppPaths.Cache, "apps.json");

    public AppLibrary(ILauncher launcher) => _launcher = launcher;

    private sealed class ConfigDoc { public List<AppTile> Apps { get; set; } = []; }

    // ---------- persistencia ----------

    public List<AppTile> Load()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return [];
            var doc = JsonSerializer.Deserialize<ConfigDoc>(File.ReadAllText(ConfigFile), Json);
            var list = doc?.Apps ?? [];
            foreach (var t in list) Resolve(t);
            // the grid is for PROJECTS; o que for programa do Windows (Chrome, Signal…) sai sozinho.
            // Excecao: o que ele adicionou a mao com + ARQUIVO (Pinned) fica.
            list.RemoveAll(t => !t.Pinned && !IsOurs(t));
            return list;
        }
        catch (Exception) { return []; }
    }

    public Task SaveAsync(IEnumerable<AppTile> tiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        var doc = new ConfigDoc { Apps = tiles.ToList() };
        return File.WriteAllTextAsync(ConfigFile, JsonSerializer.Serialize(doc, Json) + Environment.NewLine);
    }

    // ---------- seeds: o que ele citou e o que existe ----------

    public List<AppTile> Seed()
    {
        var root = ProjectScanner.Root;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var list = new List<AppTile>();

        void Add(AppTile t, string mustExist)
        {
            if (mustExist.Length > 0 && !File.Exists(mustExist) && !Directory.Exists(mustExist)) return;
            Resolve(t);
            list.Add(t);
        }

        // seeds pessoais tirados da versao publica: o usuario adiciona com + ARQUIVO

        return list;
    }

    /// <summary>So o que e nosso: alvo dentro de <raiz> (o .lnk e resolvido antes), URL local, ou pasta de projeto.</summary>
    private static bool IsOurs(AppTile t)
    {
        var root = ProjectScanner.Root.TrimEnd('\\') + "\\";
        static bool Under(string p, string root) => p.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        return t.Kind switch
        {
            AppTile.KindUrl => t.Target.Contains("localhost", StringComparison.OrdinalIgnoreCase) || t.Target.Contains("127.0.0.1"),
            AppTile.KindExe => Under(t.ResolvedTarget.Length > 0 ? t.ResolvedTarget : t.Target, root),
            _ => Under(t.WorkingDir.Length > 0 ? t.WorkingDir : t.Target, root),
        };
    }

    // ---------- candidatos ----------

    public async Task<List<AppTile>> CandidatesAsync(IReadOnlyList<ProjectInfo> projects, CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<AppTile>();
        void Add(AppTile t) { if (seen.Add(t.Key)) list.Add(t); }

        foreach (var s in Seed()) Add(s);

        await Task.Run(() =>
        {
            // atalhos: Desktop (raiz) e Menu Iniciar (ate 2 niveis) — so os que apontam pra dentro de <raiz>
            var roots = new[]
            {
                (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 0),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), 0),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"), 2),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"), 2),
            };
            var files = new List<string>();
            foreach (var (dir, depth) in roots)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    files.AddRange(Directory.EnumerateFiles(dir, "*.lnk", new System.IO.EnumerationOptions { RecurseSubdirectories = depth > 0, MaxRecursionDepth = depth, IgnoreInaccessible = true }));
                }
                catch (Exception) { }
                if (files.Count > 400) break;
            }
            foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(f);
                if (SkipLnk.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
                if (name.Equals("Factory", StringComparison.OrdinalIgnoreCase)) continue;
                var t = new AppTile { Name = name, Subtitle = Path.GetFileName(Path.GetDirectoryName(f) ?? ""), Kind = AppTile.KindExe, Target = f, Glyph = "\uE768", Accent = "cyan" };
                Resolve(t);
                if (t.ResolvedTarget.Length > 0 && !File.Exists(t.ResolvedTarget) && !Directory.Exists(t.ResolvedTarget)) continue; // atalho morto
                if (!IsOurs(t)) continue; // programa do Windows: nao e projeto nosso
                t.Subtitle = Path.GetRelativePath(ProjectScanner.Root, t.ResolvedTarget);
                lock (list) Add(t);
            }

            // projetos com comando de start detectado
            foreach (var p in projects)
            {
                ct.ThrowIfCancellationRequested();
                var stack = StackDetector.Detect(p.Path);
                if (!stack.HasStart) continue;
                var t = new AppTile { Name = p.Name, Subtitle = stack.StartCommand!, Kind = AppTile.KindTerminal, WorkingDir = p.Path, Target = p.Path, Args = stack.StartCommand!, Glyph = "\uE768", Accent = "green" };
                lock (list) Add(t);
                var c = new AppTile { Name = $"Claude · {p.Name}", Subtitle = "terminal com claude na pasta", Kind = AppTile.KindClaude, WorkingDir = p.Path, Target = p.Path, Glyph = "\uE756", Accent = "green" };
                lock (list) Add(c);
            }
        }, ct);

        return list;
    }

    public AppTile FromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        var t = ext switch
        {
            ".py" => new AppTile { Name = name, Subtitle = $"python {Path.GetFileName(path)}", Kind = AppTile.KindTerminal, WorkingDir = dir, Target = path, Args = $"python \"{Path.GetFileName(path)}\"", Glyph = "\uE768", Accent = "green" },
            ".ps1" => new AppTile { Name = name, Subtitle = Path.GetFileName(path), Kind = AppTile.KindTerminal, WorkingDir = dir, Target = path, Args = $"& '.\\{Path.GetFileName(path)}'", Glyph = "\uE768", Accent = "green" },
            ".url" => new AppTile { Name = name, Subtitle = "atalho web", Kind = AppTile.KindExe, Target = path, Glyph = "\uE774", Accent = "cyan" },
            _ => new AppTile { Name = name, Subtitle = Path.GetFileName(dir), Kind = AppTile.KindExe, Target = path, Glyph = "\uE768", Accent = "cyan" },
        };
        t.Pinned = true;
        Resolve(t);
        return t;
    }

    // ---------- abrir ----------

    public void Launch(AppTile tile)
    {
        switch (tile.Kind)
        {
            case AppTile.KindUrl:
                Process.Start(new ProcessStartInfo(tile.Target) { UseShellExecute = true });
                break;
            case AppTile.KindTerminal:
                if (string.IsNullOrWhiteSpace(tile.Args)) _launcher.OpenTerminal(tile.WorkingDir);
                else _launcher.RunInTerminal(tile.WorkingDir, tile.Args);
                break;
            case AppTile.KindClaude:
                _launcher.OpenClaude(tile.WorkingDir);
                break;
            case AppTile.KindFolder:
                _launcher.OpenExplorer(tile.Target);
                break;
            default:
                var wd = tile.WorkingDir.Length > 0 ? tile.WorkingDir : Path.GetDirectoryName(tile.ResolvedTarget.Length > 0 ? tile.ResolvedTarget : tile.Target) ?? "";
                Process.Start(new ProcessStartInfo(tile.Target)
                {
                    UseShellExecute = true,
                    Arguments = tile.Args,
                    WorkingDirectory = Directory.Exists(wd) ? wd : "",
                });
                break;
        }
    }

    // ---------- estado vivo ----------

    public async Task RefreshRunningAsync(IReadOnlyList<AppTile> tiles, CancellationToken ct = default)
    {
        if (tiles.Count == 0) return;
        var rows = await Task.Run(() =>
        {
            var r = new List<(string Name, string Exe, string Cmd)>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, ExecutablePath, CommandLine FROM Win32_Process");
                using var res = searcher.Get();
                foreach (var row in res)
                    using (row)
                        r.Add((row["Name"] as string ?? "", row["ExecutablePath"] as string ?? "", row["CommandLine"] as string ?? ""));
            }
            catch (Exception) { }
            return r;
        }, ct);
        if (rows.Count == 0) return;

        foreach (var t in tiles)
        {
            var running = t.Kind switch
            {
                AppTile.KindExe => ExeRunning(t, rows),
                AppTile.KindTerminal when t.Args.Length > 0 => rows.Any(r => r.Cmd.Length > 0
                    && r.Cmd.Contains(ScriptToken(t.Args), StringComparison.OrdinalIgnoreCase)
                    && (t.WorkingDir.Length == 0 || r.Cmd.Contains(Path.GetFileName(t.WorkingDir), StringComparison.OrdinalIgnoreCase) || r.Cmd.Contains(t.WorkingDir, StringComparison.OrdinalIgnoreCase))),
                AppTile.KindUrl => false,
                _ => false,
            };
            if (t.IsRunning != running) t.IsRunning = running;
        }
    }

    private static bool ExeRunning(AppTile t, List<(string Name, string Exe, string Cmd)> rows)
    {
        var target = t.ResolvedTarget.Length > 0 ? t.ResolvedTarget : t.Target;
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var exeName = Path.GetFileName(target);
        return rows.Any(r => r.Exe.Equals(target, StringComparison.OrdinalIgnoreCase)
                             || (r.Exe.Length == 0 && r.Name.Equals(exeName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>"python wifi_radar.py" → "wifi_radar.py"; ".\vigiar.cmd" → "vigiar.cmd".</summary>
    private static string ScriptToken(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tok = parts.LastOrDefault(p => p.Contains('.')) ?? parts.LastOrDefault() ?? args;
        return tok.Trim('"', '\'', '&', ' ').Replace(".\\", "");
    }

    // ---------- icone ----------

    public async Task<IRandomAccessStream?> IconStreamAsync(AppTile tile)
    {
        // .lnk nao devolve thumbnail pelo shell: usa o exe resolvido (icone real do programa)
        var path = tile.Kind switch
        {
            AppTile.KindExe => tile.ResolvedTarget.Length > 0 && File.Exists(tile.ResolvedTarget) ? tile.ResolvedTarget : tile.Target,
            AppTile.KindFolder => tile.Target,
            _ => "",
        };
        if (path.Length == 0 || !(File.Exists(path) || Directory.Exists(path))) return null;
        try
        {
            IStorageItemProperties item = Directory.Exists(path)
                ? await StorageFolder.GetFolderFromPathAsync(path)
                : await StorageFile.GetFileFromPathAsync(path);
            var thumb = await item.GetThumbnailAsync(ThumbnailMode.SingleItem, 96, ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || thumb.Size == 0) return null;
            return thumb;
        }
        catch (Exception) { return null; }
    }

    // ---------- default group (the user can rename it on the tile) ----------

    private static string DefaultGroup(AppTile t)
    {
        // the name counts too, for tiles without a path (a dashboard URL)
        var p = (t.Name + "|" + t.WorkingDir + "|" + t.Target + "|" + t.ResolvedTarget).ToLowerInvariant();
        if (p.Contains("wifi") || p.Contains("security") || p.Contains("vigia")) return "VIGIA";
        if (t.Kind == AppTile.KindClaude) return "CLAUDE";
        return AppGroup.Default;
    }

    // ---------- .lnk ----------

    private void Resolve(AppTile t)
    {
        ResolveLnk(t);
        if (string.IsNullOrWhiteSpace(t.Group)) t.Group = DefaultGroup(t);
    }

    private void ResolveLnk(AppTile t)
    {
        if (t.Kind != AppTile.KindExe || !t.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) { t.ResolvedTarget = t.Target; return; }
        lock (_lnkCache)
        {
            if (_lnkCache.TryGetValue(t.Target, out var hit)) { Apply(t, hit); return; }
        }
        (string Target, string Args, string Dir) r = ("", "", "");
        try
        {
            r = RunSta(() =>
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic lnk = shell.CreateShortcut(t.Target);
                return ((string)(lnk.TargetPath ?? ""), (string)(lnk.Arguments ?? ""), (string)(lnk.WorkingDirectory ?? ""));
            });
        }
        catch (Exception) { }
        lock (_lnkCache) _lnkCache[t.Target] = r;
        Apply(t, r);

        static void Apply(AppTile t, (string Target, string Args, string Dir) r)
        {
            t.ResolvedTarget = r.Target;
            if (t.WorkingDir.Length == 0 && r.Dir.Length > 0) t.WorkingDir = r.Dir;
        }
    }

    private static T RunSta<T>(Func<T> fn)
    {
        T result = default!;
        Exception? error = null;
        var th = new Thread(() => { try { result = fn(); } catch (Exception ex) { error = ex; } });
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        th.Join();
        if (error is not null) throw error;
        return result;
    }
}
