using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Factory.Models;

namespace Factory.Services;

public interface IProjectForge
{
    /// <summary>Cria <raiz>\&lt;name&gt; com o esqueleto padrao (README, CLAUDE.md, STATUS.md, factory.json).</summary>
    Task<string> CreateAsync(string name, string kind, string description, bool gitInit, IProgress<string> log);
}

/// <summary>
/// A "fabrica" na versao 1: todo projeto novo nasce ja com o contrato que o cockpit
/// le (STATUS canonico + manifesto). Nada de template pesado — o conteudo vem depois.
/// </summary>
public sealed partial class ProjectForge : IProjectForge
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,60}$")]
    private static partial Regex NameRx();

    private readonly IManifestStore _manifests;

    public ProjectForge(IManifestStore manifests) => _manifests = manifests;

    public async Task<string> CreateAsync(string name, string kind, string description, bool gitInit, IProgress<string> log)
    {
        name = name.Trim();
        if (!NameRx().IsMatch(name))
            throw new ArgumentException("Nome invalido: minusculas, digitos, '-', '_' ou '.', comecando por letra/digito.");

        var dir = Path.Combine(ProjectScanner.Root, name);
        if (Directory.Exists(dir))
            throw new InvalidOperationException($"Ja existe: {dir}");

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var desc = string.IsNullOrWhiteSpace(description) ? "(descricao pendente)" : description.Trim();
        var utf8 = new UTF8Encoding(false);

        Directory.CreateDirectory(dir);
        log.Report($"pasta criada: {dir}");

        await File.WriteAllTextAsync(Path.Combine(dir, "README.md"),
            $"# {name}\n\n{desc}\n", utf8);
        log.Report("README.md");

        await File.WriteAllTextAsync(Path.Combine(dir, "CLAUDE.md"),
            $"""
            # {name}

            {desc}

            ## Regras deste projeto
            - `STATUS.md` na raiz e a fonte de "onde parei": ler ao retomar, gravar ao encerrar (skill `/status`).
            - `factory.json` e o manifesto lido pelo Factory (kind/stage/money/url) — atualizar quando o estado mudar.
            - Trabalhar sempre com a sessao aberta nesta pasta, nao na raiz de projetos.

            """, utf8);
        log.Report("CLAUDE.md");

        await File.WriteAllTextAsync(Path.Combine(dir, "STATUS.md"),
            $"""
            # STATUS — {name}
            Atualizado: {today}

            ## Onde paramos
            Projeto recem-criado pelo Factory. {desc}

            ## Pronto (com caminho)
            - esqueleto (README, CLAUDE.md, STATUS.md, factory.json) → `{dir}`

            ## Proxima acao
            Definir o primeiro entregavel concreto e abrir uma sessao do Claude nesta pasta.

            ## Bloqueios
            nenhum

            """, utf8);
        log.Report("STATUS.md");

        await _manifests.SaveAsync(dir, new ProjectManifest { Kind = kind, Stage = "ideia", Notes = desc });
        log.Report("factory.json");

        if (gitInit)
        {
            await File.WriteAllTextAsync(Path.Combine(dir, ".gitignore"),
                "node_modules/\nbin/\nobj/\n.env\n*.local.*\n__pycache__/\n", utf8);
            var ok = await RunGitAsync(dir, "init -q");
            log.Report(ok ? "git init" : "git init falhou (git no PATH?)");
        }

        return dir;
    }

    private static async Task<bool> RunGitAsync(string dir, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", $"-C \"{dir}\" {args}")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (p is null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Exception) { return false; }
    }
}
