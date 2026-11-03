using System.Text.RegularExpressions;
using Factory.ViewModels;

namespace Factory.Services;

/// <summary>
/// Acha o que abrir a partir do nome falado (a spoken game name failed because abrir_app so conhecia a lista fixa + nomes que o Windows resolve). Fontes, nesta ordem:
/// atalhos do Menu Iniciar (usuario + todos) e da Area de Trabalho (*.lnk, *.url) e jogos da Steam
/// (appmanifest_*.acf de todas as bibliotecas → steam://rungameid/ID). Casa por nome dobrado (sem acento/caixa):
/// igual > comeca com > contem > parecido (PaletteMatcher > 60, pega "Mecca" por "Mecha").
/// </summary>
public static class AppResolver
{
    public sealed record Alvo(string Caminho, string Nome, string Fonte);

    public static Alvo? Resolver(string nome)
    {
        var q = CommandLine.Fold((nome ?? "").Trim());
        if (q.Length < 2) return null;
        var candidatos = Atalhos().Concat(JogosSteam()).ToList();
        if (candidatos.Count == 0) return null;
        Alvo? melhor = null; var nota = 0;
        foreach (var c in candidatos)
        {
            var n = CommandLine.Fold(c.Nome);
            var s = n == q ? 100 : n.StartsWith(q) ? 90 : n.Contains(q) ? 80 : q.Contains(n) && n.Length >= 4 ? 70 : PaletteMatcher.Score(q, c.Nome, "");
            if (s > nota) { nota = s; melhor = c; }
        }
        return nota > 60 ? melhor : null;
    }

    private static IEnumerable<Alvo> Atalhos()
    {
        var pastas = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        foreach (var pasta in pastas.Distinct())
        {
            if (!Directory.Exists(pasta)) continue;
            IEnumerable<string> arquivos;
            try { arquivos = Directory.EnumerateFiles(pasta, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase)); }
            catch (Exception) { continue; }
            foreach (var f in arquivos)
            {
                var n = Path.GetFileNameWithoutExtension(f);
                if (n.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Desinstalar", StringComparison.OrdinalIgnoreCase)) continue;
                yield return new Alvo(f, n, "atalho");
            }
        }
    }

    private static IEnumerable<Alvo> JogosSteam()
    {
        var raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        var libs = new List<string> { Path.Combine(raiz, "steamapps") };
        var vdf = Path.Combine(raiz, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            string txt; try { txt = File.ReadAllText(vdf); } catch (Exception) { txt = ""; }
            foreach (Match m in Regex.Matches(txt, "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps"));
        }
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(lib)) continue;
            IEnumerable<string> acfs;
            try { acfs = Directory.EnumerateFiles(lib, "appmanifest_*.acf"); } catch (Exception) { continue; }
            foreach (var acf in acfs)
            {
                string txt; try { txt = File.ReadAllText(acf); } catch (Exception) { continue; }
                var id = Regex.Match(txt, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                var nome = Regex.Match(txt, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                if (id.Length == 0 || nome.Length == 0 || nome.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) || nome.Contains("Steamworks", StringComparison.OrdinalIgnoreCase)) continue;
                yield return new Alvo($"steam://rungameid/{id}", nome, "steam");
            }
        }
    }
}
