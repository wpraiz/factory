using System.Text;
using System.Text.RegularExpressions;
using Factory.Models;

namespace Factory.Services;

public sealed record Briefing(string Text, int DurationMs, string Digest, DateTimeOffset At)
{
    public string Where => Section("ONDE PAROU");
    public string Next => Section("PR[OÓ]XIMA A[CÇ][AÃ]O");
    public string Blockers => Section("BLOQUEIOS?");

    private string Section(string header)
    {
        var m = Regex.Match(Text, $@"(?is)(?:^|\n)\W*{header}\W*[:\-—]?\s*(.*?)(?=\n\W*(?:ONDE PAROU|PR[OÓ]XIMA A[CÇ][AÃ]O|BLOQUEIOS?)\b|\z)");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }
}

public interface IBriefingService
{
    /// <summary>Resumo de retomada gerado pelo Claude (headless) a partir do digest da sessao. 5–15 s.</summary>
    Task<Briefing> BuildAsync(SessionRecord session, ProjectInfo project, CancellationToken ct);
    Briefing? Cached(string sessionId);
    bool CliAvailable { get; }
}

/// <summary>
/// `claude -p` via <see cref="ClaudeCli"/> (assinatura, sem tools, MCP zerado).
/// Cache em %LOCALAPPDATA%\Factory\briefings.
/// </summary>
public sealed class BriefingService : IBriefingService
{
    private const string SystemPrompt =
        "Voce recebe o digest de uma sessao de trabalho do Claude Code em um projeto. Responda em pt-BR, no maximo 8 linhas, " +
        "exatamente nestas tres secoes, cada uma com o titulo em maiusculas seguido de dois-pontos: ONDE PAROU: / PROXIMA ACAO: / BLOQUEIOS:. " +
        "ONDE PAROU = o que estava sendo feito e por que (2-3 linhas). PROXIMA ACAO = UMA acao concreta e executavel (1-2 linhas). " +
        "BLOQUEIOS = o que trava, ou 'nenhum'. Nao invente: se o digest nao tem o dado, escreva 'sem sinal'. Sem preambulo, sem markdown.";

    private static readonly string CacheDir = Path.Combine(
        AppPaths.Cache, "briefings");

    private readonly SemaphoreSlim _gate = new(2);
    private readonly Dictionary<string, Briefing> _memory = new(StringComparer.OrdinalIgnoreCase);
    public bool CliAvailable => ClaudeCli.Available;

    public Briefing? Cached(string sessionId)
    {
        lock (_memory)
            if (_memory.TryGetValue(sessionId, out var b)) return b;
        try
        {
            var f = Path.Combine(CacheDir, sessionId + ".md");
            if (!File.Exists(f)) return null;
            var text = File.ReadAllText(f);
            var b = new Briefing(text, 0, "", File.GetLastWriteTime(f));
            lock (_memory) _memory[sessionId] = b;
            return b;
        }
        catch (Exception) { return null; }
    }

    public async Task<Briefing> BuildAsync(SessionRecord session, ProjectInfo project, CancellationToken ct)
    {
        var digest = BuildDigest(session, project);

        await _gate.WaitAsync(ct);
        try
        {
            var reply = await ClaudeCli.RunAsync(SystemPrompt, digest, resumeId: null, model: "sonnet", ct);
            var briefing = new Briefing(reply.Text, reply.DurationMs, digest, DateTimeOffset.Now);
            lock (_memory) _memory[session.Id] = briefing;
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(Path.Combine(CacheDir, session.Id + ".md"), briefing.Text, new UTF8Encoding(false));
            }
            catch (Exception) { /* cache e conveniencia */ }
            return briefing;
        }
        finally { _gate.Release(); }
    }

    /// <summary>≤ 40 linhas: o que uma pessoa leria pra lembrar da sessao.</summary>
    public static string BuildDigest(SessionRecord s, ProjectInfo p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PROJETO: {p.Name} ({p.Path})" + (p.GitBranch.Length > 0 ? $" · branch atual {p.GitBranch}" : ""));
        sb.AppendLine($"SESSAO: {s.Title} · {s.LastWrite:yyyy-MM-dd HH:mm} · {s.Turns} turnos" + (s.GitBranch.Length > 0 ? $" · branch na sessao {s.GitBranch}" : ""));
        sb.AppendLine();
        sb.AppendLine("PRIMEIRO PROMPT: " + Clip(s.FirstPrompt, 300));
        sb.AppendLine("ULTIMA MENSAGEM HUMANA: " + Clip(s.LastHuman, 600));
        sb.AppendLine("ULTIMA RESPOSTA DO ASSISTENTE: " + Clip(s.LastAssistant, 600));
        sb.AppendLine();
        var root = p.Path.TrimEnd('\\') + "\\";
        var edited = s.Touched.Where(t => t.IsEdit && t.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Select(t => Path.GetRelativePath(p.Path, t.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();
        sb.AppendLine("ARQUIVOS EDITADOS NA SESSAO: " + (edited.Count == 0 ? "nenhum registrado" : string.Join(", ", edited)));
        sb.AppendLine();
        var status = Path.Combine(p.Path, "STATUS.md");
        if (File.Exists(status))
        {
            sb.AppendLine("STATUS.md ATUAL (10 primeiras linhas):");
            try { foreach (var l in File.ReadLines(status).Take(10)) sb.AppendLine("  " + l); }
            catch (Exception) { sb.AppendLine("  (nao consegui ler)"); }
        }
        else sb.AppendLine("STATUS.md ATUAL: nao existe");
        return sb.ToString();
    }

    private static string Clip(string s, int n) => string.IsNullOrWhiteSpace(s) ? "(vazio)" : s.Length > n ? s[..n] + "…" : s;
}
