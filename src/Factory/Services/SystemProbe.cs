using System.Diagnostics;
using System.Management;
using System.Text;
using Factory.Models;
using Microsoft.Win32.TaskScheduler;
using TSTask = Microsoft.Win32.TaskScheduler.Task;
using Task = System.Threading.Tasks.Task;

namespace Factory.Services;

public interface ISystemProbe
{
    /// <summary>Processos vivos, atalhos .lnk e tasks agendadas que apontam pra pasta. Só leitura, sem UAC.</summary>
    Task<SystemReport> ProbeAsync(string projectPath, CancellationToken ct = default);

    /// <summary>Kills the process and its tree. false = sem permissao / ja morto.</summary>
    bool Kill(int pid);
}

/// <summary>
/// O que o Windows sabe sobre o projeto fora da pasta: quem esta rodando com ela na linha de
/// comando, que atalho aponta pra ela, que task agendada a executa. Tudo read-only; matar
/// processo e a unica acao e passa por confirmacao na UI.
/// </summary>
public sealed class SystemProbe : ISystemProbe
{
    private const int MaxLnk = 2000;

    public async Task<SystemReport> ProbeAsync(string projectPath, CancellationToken ct = default)
    {
        var report = new SystemReport();
        var path = projectPath.TrimEnd('\\');

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Processes(path, report);
            ct.ThrowIfCancellationRequested();
            Shortcuts(path, report);
            ct.ThrowIfCancellationRequested();
            ScheduledTasks(path, report);
        }, ct);

        return report;
    }

    public bool Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception) { return false; }
    }

    // ---------- processos (WMI: e o unico jeito de ler a linha de comando de outros processos) ----------

    private static void Processes(string path, SystemReport report)
    {
        var me = Environment.ProcessId;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process");
            using var rows = searcher.Get();
            foreach (var row in rows)
            {
                using (row)
                {
                    var pid = Convert.ToInt32(row["ProcessId"]);
                    if (pid == me) continue;
                    var cmd = row["CommandLine"] as string ?? "";
                    var exe = row["ExecutablePath"] as string ?? "";
                    // Processos elevados escondem a CommandLine (null) — sobra o executavel.
                    if (!cmd.Contains(path, StringComparison.OrdinalIgnoreCase)
                        && !exe.Contains(path, StringComparison.OrdinalIgnoreCase)) continue;
                    report.Processes.Add(new LiveProcess(pid, row["Name"] as string ?? "?", cmd.Length > 0 ? cmd : exe + "  (linha de comando sem acesso)"));
                }
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add("processos: " + ex.Message);
        }
    }

    // ---------- atalhos ----------

    private static IEnumerable<string> ShortcutRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }

    private static void Shortcuts(string path, SystemReport report)
    {
        var files = new List<string>();
        foreach (var root in ShortcutRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    files.Add(f);
                    if (files.Count >= MaxLnk) break;
                }
            }
            catch (Exception) { /* pasta sem acesso: segue */ }
            if (files.Count >= MaxLnk) break;
        }

        // Filtro barato primeiro: o .lnk guarda caminhos em ASCII e UTF-16; se a pasta nao
        // aparece nos bytes, nem vale abrir o COM.
        var candidates = files.Where(f => BytesContain(f, path)).ToList();
        if (candidates.Count == 0) return;

        try
        {
            RunSta(() =>
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                foreach (var f in candidates)
                {
                    try
                    {
                        dynamic lnk = shell.CreateShortcut(f);
                        string target = lnk.TargetPath ?? "";
                        string args = lnk.Arguments ?? "";
                        string wd = lnk.WorkingDirectory ?? "";
                        if ((target + " " + args + " " + wd).Contains(path, StringComparison.OrdinalIgnoreCase))
                            report.Shortcuts.Add(new ShortcutHit(f, target, args));
                    }
                    catch (Exception) { /* .lnk quebrado */ }
                }
                return 0;
            });
        }
        catch (Exception ex)
        {
            // COM indisponivel: os candidatos por bytes ja sao um bom sinal — lista sem detalhe.
            report.Errors.Add("atalhos (COM): " + ex.Message);
            foreach (var f in candidates) report.Shortcuts.Add(new ShortcutHit(f, "(alvo nao lido)", ""));
        }
    }

    private static bool BytesContain(string file, string needle)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length > 64 * 1024) return false;
            return IndexOf(bytes, Encoding.ASCII.GetBytes(needle)) >= 0
                   || IndexOf(bytes, Encoding.Unicode.GetBytes(needle)) >= 0
                   || IndexOf(bytes, Encoding.ASCII.GetBytes(needle.ToLowerInvariant())) >= 0
                   || IndexOf(bytes, Encoding.Unicode.GetBytes(needle.ToLowerInvariant())) >= 0;
        }
        catch (Exception) { return false; }
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        if (needle.Length == 0 || hay.Length < needle.Length) return -1;
        for (var i = 0; i <= hay.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                var a = hay[i + j];
                var b = needle[j];
                // case-insensitive so pra ASCII
                if (a != b && !(a is >= 65 and <= 90 && a + 32 == b) && !(b is >= 65 and <= 90 && b + 32 == a)) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }

    private static T RunSta<T>(Func<T> fn)
    {
        T result = default!;
        Exception? error = null;
        var th = new Thread(() =>
        {
            try { result = fn(); }
            catch (Exception ex) { error = ex; }
        });
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        th.Join();
        if (error is not null) throw error;
        return result;
    }

    // ---------- tasks agendadas ----------

    private static void ScheduledTasks(string path, SystemReport report)
    {
        try
        {
            using var ts = new TaskService();
            foreach (TSTask t in ts.AllTasks)
            {
                string actions;
                try { actions = string.Join("; ", t.Definition.Actions.Select(a => a.ToString())); }
                catch (Exception) { continue; }
                if (!actions.Contains(path, StringComparison.OrdinalIgnoreCase)) continue;
                report.Tasks.Add(new TaskHit(t.Path, t.State.ToString(), Sane(t.NextRunTime), actions));
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add("tasks: " + ex.Message);
        }
    }

    // O Task Scheduler usa 30/12/1899 pra "nunca".
    private static DateTime? Sane(DateTime v) => v.Year < 2000 ? null : v;
}
