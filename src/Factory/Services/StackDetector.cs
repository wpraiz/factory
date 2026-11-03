using System.Text.Json;
using System.Text.RegularExpressions;

namespace Factory.Services;

/// <summary>Stack real do projeto: tags precisas + como rodar (sugestao, nunca executado sozinho).</summary>
public sealed record StackInfo(IReadOnlyList<string> Tags, string? StartCommand, string Summary)
{
    public static readonly StackInfo Empty = new([], null, "");
    public bool HasStart => !string.IsNullOrWhiteSpace(StartCommand);
}

/// <summary>
/// Le so os manifestos da raiz (package.json, *.csproj, requirements/pyproject, composer.json,
/// wp-config.php). Barato o bastante pra rodar no scan de todos os projetos.
/// </summary>
public static partial class StackDetector
{
    [GeneratedRegex(@"<UseWinUI>\s*true", RegexOptions.IgnoreCase)]
    private static partial Regex UseWinUiRx();

    [GeneratedRegex(@"Sdk=""Microsoft\.NET\.Sdk\.Web""", RegexOptions.IgnoreCase)]
    private static partial Regex WebSdkRx();

    private static readonly (string Dep, string Tag)[] NodeDeps =
    {
        ("next", "next"), ("react", "react"), ("vue", "vue"), ("svelte", "svelte"), ("vite", "vite"),
        ("electron", "electron"), ("express", "express"), ("fastify", "fastify"), ("tailwindcss", "tailwind"),
        ("typescript", "typescript"), ("playwright", "playwright"), ("puppeteer", "puppeteer"),
        ("@anthropic-ai/sdk", "claude-api"), ("@modelcontextprotocol/sdk", "mcp"),
    };

    private static readonly (string Pkg, string Tag)[] PyDeps =
    {
        ("fastapi", "fastapi"), ("flask", "flask"), ("django", "django"), ("streamlit", "streamlit"),
        ("playwright", "playwright"), ("selenium", "selenium"), ("pandas", "pandas"), ("torch", "pytorch"),
        ("anthropic", "claude-api"), ("openai", "openai"), ("requests", "requests"), ("uvicorn", "uvicorn"),
    };

    public static StackInfo Detect(string dir)
    {
        var tags = new List<string>();
        var summary = new List<string>();
        string? start = null;

        try
        {
            // ---- node ----
            var pkg = Path.Combine(dir, "package.json");
            if (File.Exists(pkg))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
                var root = doc.RootElement;
                var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var section in new[] { "dependencies", "devDependencies" })
                    if (root.TryGetProperty(section, out var d) && d.ValueKind == JsonValueKind.Object)
                        foreach (var kv in d.EnumerateObject()) deps.Add(kv.Name);
                foreach (var (dep, tag) in NodeDeps) if (deps.Contains(dep)) tags.Add(tag);

                if (root.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in new[] { "dev", "start", "serve" })
                        if (scripts.TryGetProperty(name, out var sc) && sc.ValueKind == JsonValueKind.String)
                        {
                            start ??= $"npm run {name}";
                            summary.Add($"npm run {name} → {sc.GetString()}");
                            break;
                        }
                }
                summary.Insert(0, $"node · {deps.Count} deps");
            }

            // ---- dotnet ----
            var csprojs = Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateDirectories(dir).Take(20).SelectMany(d => SafeFiles(d, "*.csproj")))
                .Take(6).ToList();
            foreach (var cs in csprojs)
            {
                var xml = File.ReadAllText(cs);
                if (UseWinUiRx().IsMatch(xml)) tags.Add("winui3");
                if (WebSdkRx().IsMatch(xml)) tags.Add("aspnet");
                var tfm = Regex.Match(xml, @"<TargetFramework>([^<]+)</TargetFramework>").Groups[1].Value;
                summary.Add($"dotnet · {Path.GetFileName(cs)}" + (tfm.Length > 0 ? $" ({tfm})" : ""));
                start ??= UseWinUiRx().IsMatch(xml)
                    ? $"dotnet build \"{Path.GetRelativePath(dir, cs)}\""
                    : $"dotnet run --project \"{Path.GetRelativePath(dir, cs)}\"";
            }

            // ---- python ----
            var req = Path.Combine(dir, "requirements.txt");
            var pyproj = Path.Combine(dir, "pyproject.toml");
            if (File.Exists(req) || File.Exists(pyproj))
            {
                var text = (File.Exists(req) ? File.ReadAllText(req) : "") + "\n" + (File.Exists(pyproj) ? File.ReadAllText(pyproj) : "");
                foreach (var (p, tag) in PyDeps)
                    if (Regex.IsMatch(text, $@"(?im)^\s*{Regex.Escape(p)}\b|""{Regex.Escape(p)}[>=<\[""]")) tags.Add(tag);
                summary.Add("python · " + (File.Exists(req) ? "requirements.txt" : "pyproject.toml"));
            }
            foreach (var entry in new[] { "main.py", "app.py", "server.py", "servidor.py" })
            {
                var f = Path.Combine(dir, entry);
                if (!File.Exists(f)) continue;
                var head = ReadHead(f, 4000);
                if (head.Contains("streamlit", StringComparison.OrdinalIgnoreCase)) { start ??= $"streamlit run {entry}"; tags.Add("streamlit"); }
                else if (head.Contains("fastapi", StringComparison.OrdinalIgnoreCase)) { start ??= $"uvicorn {Path.GetFileNameWithoutExtension(entry)}:app --reload"; }
                else start ??= $"python {entry}";
                if (head.Contains("tkinter", StringComparison.OrdinalIgnoreCase)) tags.Add("tkinter");
                break;
            }
            if (start is null)
            {
                var onlyPy = SafeFiles(dir, "*.py").Take(2).ToList();
                if (onlyPy.Count == 1) start = $"python {Path.GetFileName(onlyPy[0])}";
            }

            // ---- php / wordpress ----
            if (File.Exists(Path.Combine(dir, "composer.json"))) { tags.Add("php"); summary.Add("php · composer.json"); }
            if (File.Exists(Path.Combine(dir, "wp-config.php")) || Directory.Exists(Path.Combine(dir, "wp-content")))
            { tags.Add("wordpress"); summary.Add("wordpress"); }

            // ---- docker ----
            if (File.Exists(Path.Combine(dir, "docker-compose.yml")) || File.Exists(Path.Combine(dir, "compose.yml")))
                start ??= "docker compose up";
        }
        catch (Exception)
        {
            // manifesto quebrado: fica com o que deu
        }

        return new StackInfo(tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), start, string.Join("\n", summary));
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern); }
        catch (Exception) { return []; }
    }

    private static string ReadHead(string file, int chars)
    {
        try
        {
            using var r = new StreamReader(file);
            var buf = new char[chars];
            var n = r.Read(buf, 0, chars);
            return new string(buf, 0, n);
        }
        catch (Exception) { return ""; }
    }
}
