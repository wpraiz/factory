using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Factory.Services;

/// <summary>Resposta de uma rodada headless: texto e o id da sessao (pra continuar com --resume).</summary>
public sealed record ClaudeReply(string Text, string? SessionId, int DurationMs);

/// <summary>
/// `claude -p` headless, padrao unico do app (assinatura, sem --bare, MCP zerado, sem tool
/// nenhuma, cwd na pasta do app pra nao carregar hooks/MCP de projeto). Usado pelo briefing
/// e pelo chat. Nada aqui gasta credito de API: e a assinatura do Claude Code.
/// </summary>
public static class ClaudeCli
{
    private static readonly Lazy<(string Exe, string? Prefix)?> Cli = new(Resolve);

    public static bool Available => Cli.Value is not null;

    /// <summary>Roda uma rodada. <paramref name="resumeId"/> continua a conversa anterior.</summary>
    public static async Task<ClaudeReply> RunAsync(string systemPrompt, string input, string? resumeId, string model, CancellationToken ct)
    {
        var cli = Cli.Value ?? throw new InvalidOperationException("claude CLI nao encontrado (nem em ~\\.local\\bin nem no PATH)");
        var psi = new ProcessStartInfo
        {
            FileName = cli.Exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        if (cli.Prefix is not null) { psi.ArgumentList.Add(cli.Prefix); psi.ArgumentList.Add("claude"); }
        foreach (var a in new[]
                 {
                     "-p", "--model", model,
                     "--permission-mode", "dontAsk",
                     "--disallowedTools", "Bash,Edit,Write,Read,Glob,Grep,WebFetch,WebSearch,Task,Agent,NotebookEdit",
                     "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
                     "--append-system-prompt", systemPrompt,
                     "--output-format", "json",
                 })
            psi.ArgumentList.Add(a);
        if (!string.IsNullOrWhiteSpace(resumeId)) { psi.ArgumentList.Add("--resume"); psi.ArgumentList.Add(resumeId); }

        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("nao consegui iniciar o claude");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        using var reg = timeout.Token.Register(() => { try { p.Kill(entireProcessTree: true); } catch (Exception) { } });

        await p.StandardInput.WriteAsync(input);
        p.StandardInput.Close();
        var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = await p.StandardError.ReadToEndAsync(timeout.Token);
        await p.WaitForExitAsync(timeout.Token);
        sw.Stop();

        var (text, sessionId) = Parse(stdout);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("sem resposta" + (stderr.Length > 0 ? ": " + stderr[^Math.Min(300, stderr.Length)..].Trim() : ""));
        return new ClaudeReply(text.Trim(), sessionId, (int)sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// AGENT mode (design rule: Odin is free to act): claude -p COM
    /// ferramentas (Bash, Read, Write, Edit, Glob, Grep) e o windows-mcp, permissoes liberadas, cwd em
    /// <raiz>. Usado so pela mao `fazer` do Odin, que so roda depois de ele pedir em voz alta. Ate 10 min.
    /// </summary>
    public static async Task<ClaudeReply> RunAgentAsync(string systemPrompt, string task, string cwd, CancellationToken ct, string model = "sonnet", bool readOnly = false, int maxMinutes = 10, bool semMouse = false, bool semMcp = false)
    {
        var cli = Cli.Value ?? throw new InvalidOperationException("claude CLI nao encontrado");
        var psi = new ProcessStartInfo
        {
            FileName = cli.Exe, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = cwd, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        };
        if (cli.Prefix is not null) { psi.ArgumentList.Add(cli.Prefix); psi.ArgumentList.Add("claude"); }
        // windows-mcp with its PowerShell/Registry tools excluded.
        // Fiscal (readOnly): so Screenshot/Snapshot/Scrape/DisplayInventory; sem clique, teclado, arquivo ou processo.
        var excl = readOnly ? "PowerShell,Registry,Click,Type,Move,Scroll,Shortcut,Clipboard,MultiEdit,MultiSelect,FileSystem,Process,App,Notification,Wait,WaitFor"
            : semMouse ? "PowerShell,Registry,Click,Type,Move,Scroll,Shortcut,MultiSelect" : "PowerShell,Registry";
        // semMcp: quem so le uma imagem que o Factory ja capturou nao precisa subir o uvx windows-mcp (segundos)
        var mcp = semMcp ? "{\"mcpServers\":{}}"
            : "{\"mcpServers\":{\"windows-mcp\":{\"type\":\"stdio\",\"command\":\"uvx\",\"args\":[\"windows-mcp\",\"serve\",\"--exclude-tools\",\"" + excl + "\"]}}}";
        foreach (var a in new[]
                 {
                     "-p", "--model", model,
                     "--permission-mode", "bypassPermissions",
                     "--max-turns", readOnly ? "8" : model == "haiku" ? "12" : "60",
                     "--strict-mcp-config", "--mcp-config", mcp,
                     "--append-system-prompt", systemPrompt,
                     "--output-format", "json",
                 })
            psi.ArgumentList.Add(a);
        if (readOnly) { psi.ArgumentList.Add("--disallowedTools"); psi.ArgumentList.Add("Bash,Write,Edit,MultiEdit,NotebookEdit,WebFetch,WebSearch,Agent"); }

        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("nao consegui iniciar o claude");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(maxMinutes));
        using var reg = timeout.Token.Register(() => { try { p.Kill(entireProcessTree: true); } catch (Exception) { } });
        await p.StandardInput.WriteAsync(task);
        p.StandardInput.Close();
        var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = await p.StandardError.ReadToEndAsync(timeout.Token);
        await p.WaitForExitAsync(timeout.Token);
        sw.Stop();
        var (text, sid) = Parse(stdout);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("sem resposta" + (stderr.Length > 0 ? ": " + stderr[^Math.Min(300, stderr.Length)..].Trim() : ""));
        return new ClaudeReply(text.Trim(), sid, (int)sw.ElapsedMilliseconds);
    }

    private static (string Text, string? SessionId) Parse(string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var text = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            var sid = root.TryGetProperty("session_id", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            return (text, sid);
        }
        catch (JsonException) { return (stdout, null); }
    }

    private static (string Exe, string? Prefix)? Resolve()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
                 {
                     Path.Combine(home, ".local", "bin", "claude.exe"),
                     Path.Combine(home, ".local", "bin", "claude.cmd"),
                     Path.Combine(home, "AppData", "Roaming", "npm", "claude.cmd"),
                 })
            if (File.Exists(candidate)) return (candidate, null);

        // PATH: cmd resolve .cmd/.exe; o "claude" sem extensao de ~\.local\bin tambem passa por aqui.
        try
        {
            using var w = Process.Start(new ProcessStartInfo("where.exe", "claude")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            var first = w?.StandardOutput.ReadLine();
            w?.WaitForExit(3000);
            if (!string.IsNullOrWhiteSpace(first) && File.Exists(first))
                return first.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? (first, null) : ("cmd.exe", "/c");
        }
        catch (Exception) { }
        return null;
    }
}
