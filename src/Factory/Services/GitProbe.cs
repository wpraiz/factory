using System.Diagnostics;

namespace Factory.Services;

public interface IGitProbe
{
    /// <summary>Branch atual e numero de arquivos modificados; null se git nao esta disponivel.</summary>
    Task<(string Branch, int Dirty)?> ProbeAsync(string path, CancellationToken ct = default);

    /// <summary>Roda `git -C path args` e devolve stdout (null se falhar). 8 s de teto.</summary>
    Task<string?> RunAsync(string path, string args, CancellationToken ct = default);
}

public sealed class GitProbe : IGitProbe
{
    public async Task<(string Branch, int Dirty)?> ProbeAsync(string path, CancellationToken ct = default)
    {
        var branch = await RunAsync(path, "rev-parse --abbrev-ref HEAD", ct);
        if (branch is null) return null;

        var status = await RunAsync(path, "status --porcelain", ct) ?? "";
        var dirty = status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return (branch.Trim(), dirty);
    }

    public async Task<string?> RunAsync(string path, string args, CancellationToken ct = default)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("git", $"-C \"{path}\" {args}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            p.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var output = await p.StandardOutput.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            return p.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            // git ausente, timeout ou repo quebrado: sem info, sem drama.
            return null;
        }
    }
}
