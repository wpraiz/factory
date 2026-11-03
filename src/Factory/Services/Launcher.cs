using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;

namespace Factory.Services;

public interface ILauncher
{
    void OpenTerminal(string dir);
    /// <summary>Abre o Windows Terminal na pasta com `claude` (ou `claude --resume id`).</summary>
    void OpenClaude(string dir, string? resumeId = null);
    /// <summary>Abre o Windows Terminal na pasta com `claude "prompt"` (sessao nova, interativa).</summary>
    void OpenClaudeWithPrompt(string dir, string prompt, string extraArgs = "");
    void OpenExplorer(string dir);
    /// <summary>Abre o terminal na pasta e roda o comando (fica aberto pra ver a saida).</summary>
    void RunInTerminal(string dir, string command);
    void OpenFile(string path);
    void CopyToClipboard(string text);
}

public sealed class Launcher : ILauncher
{
    public void OpenTerminal(string dir)
    {
        if (!TryStart("wt.exe", $"-d \"{dir}\""))
            Start("pwsh.exe", "-NoExit", dir);
    }

    public void OpenClaude(string dir, string? resumeId = null)
    {
        var claude = resumeId is null ? "claude" : $"claude --resume {resumeId}";
        if (!TryStart("wt.exe", $"-d \"{dir}\" pwsh -NoExit -Command \"{claude}\""))
            Start("pwsh.exe", $"-NoExit -Command \"{claude}\"", dir);
    }

    public void OpenClaudeWithPrompt(string dir, string prompt, string extraArgs = "")
    {
        // aspas simples do pwsh: literal, so ' precisa dobrar; aspas duplas somem pro wt/pwsh; ";" o wt le como "nova aba" (a task was once split in 2 tabs)
        var safe = prompt.Replace(";", ",").Replace("\"", "").Replace("'", "''").Replace("\r", " ").Replace("\n", " ");
        // prompt ANTES das flags: --allowedTools e variadico e engolia a tarefa como se fosse mais uma ferramenta
        var claude = extraArgs.Length > 0 ? $"claude '{safe}' {extraArgs}" : $"claude '{safe}'";
        if (!TryStart("wt.exe", $"-d \"{dir}\" pwsh -NoExit -Command \"{claude}\""))
            Start("pwsh.exe", $"-NoExit -Command \"{claude}\"", dir);
    }

    public void OpenExplorer(string dir) => TryStart("explorer.exe", $"\"{dir}\"");

    public void RunInTerminal(string dir, string command)
    {
        var escaped = command.Replace("\"", "\\\"");
        if (!TryStart("wt.exe", $"-d \"{dir}\" pwsh -NoExit -Command \"{escaped}\""))
            Start("pwsh.exe", $"-NoExit -Command \"{escaped}\"", dir);
    }

    public void OpenFile(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void CopyToClipboard(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
    }

    private static bool TryStart(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void Start(string exe, string args, string workDir)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = workDir });
        }
        catch (Exception) { /* sem pwsh nem wt: nada a fazer, o botao so nao responde */ }
    }
}
