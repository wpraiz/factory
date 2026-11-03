using System.Text;
using Factory.Models;

namespace Factory.Services;

public interface IContextBundler
{
    string OutputDir { get; }
    /// <summary>Junta todos os STATUS.md num unico .md em <see cref="OutputDir"/>; devolve o caminho.</summary>
    Task<string> BundleAsync(IReadOnlyList<ProjectInfo> projects);
    IReadOnlyList<string> ListBundles();
}

/// <summary>
/// "Juntar contextos": um arquivo so com o presente de todos os projetos, pra colar
/// numa sessao nova ou ler de manha. Vai pra .factory\contextos (espaco de operacao
/// do usuario, nao do app).
/// </summary>
public sealed class ContextBundler : IContextBundler
{
    public string OutputDir => Path.Combine(AppPaths.Dados, "contextos");

    public async Task<string> BundleAsync(IReadOnlyList<ProjectInfo> projects)
    {
        Directory.CreateDirectory(OutputDir);
        var now = DateTimeOffset.Now;
        var file = Path.Combine(OutputDir, $"factory-contextos-{now:yyyy-MM-dd-HHmm}.md");

        var withStatus = projects.Where(p => p.HasStatus).OrderByDescending(p => p.LastTouch).ToList();
        var without = projects.Where(p => !p.HasStatus).OrderBy(p => p.Name).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Contextos — {now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Gerado pelo Factory a partir dos STATUS.md de `{ProjectScanner.Root}`.");
        sb.AppendLine($"{withStatus.Count} projetos com STATUS · {without.Count} sem.");
        sb.AppendLine();
        sb.AppendLine("## Indice (proxima acao de cada um)");
        sb.AppendLine();
        foreach (var p in withStatus)
        {
            var next = string.IsNullOrWhiteSpace(p.NextAction) ? "(sem proxima acao declarada)" : FirstLine(p.NextAction);
            sb.AppendLine($"- **{p.Name}** ({p.StatusUpdated}) — {next}");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var p in withStatus)
        {
            sb.AppendLine($"## {p.Name}");
            sb.AppendLine();
            sb.Append('`').Append(p.Path).Append('`');
            if (!p.Manifest.IsEmpty) sb.Append(" · manifesto: ").Append(p.StageText).Append(" · ").Append(p.MoneyText);
            sb.AppendLine();
            sb.AppendLine();
            string raw;
            try { raw = await File.ReadAllTextAsync(Path.Combine(p.Path, "STATUS.md")); }
            catch (IOException) { raw = "(nao consegui ler o STATUS.md)"; }
            // Rebaixa headings do STATUS pra ficarem abaixo do "## projeto".
            foreach (var line in raw.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                sb.AppendLine(l.StartsWith('#') ? "#" + l : l);
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        if (without.Count > 0)
        {
            sb.AppendLine("## Sem STATUS.md");
            sb.AppendLine();
            foreach (var p in without)
                sb.AppendLine($"- {p.Name} — ultimo toque {p.LastTouchText}, {p.SessionCount} sessoes");
        }

        await File.WriteAllTextAsync(file, sb.ToString(), new UTF8Encoding(false));
        return file;
    }

    public IReadOnlyList<string> ListBundles()
    {
        if (!Directory.Exists(OutputDir)) return [];
        return Directory.EnumerateFiles(OutputDir, "factory-contextos-*.md")
            .OrderByDescending(f => f)
            .ToList();
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return i < 0 ? s : s[..i];
    }
}
