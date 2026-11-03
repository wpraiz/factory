using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Factory.Models;
using Factory.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Factory.ViewModels;

/// <summary>Um resultado da palette: rotulo, detalhe e a acao. Kind vira o cabecalho do grupo.</summary>
public sealed class PaletteItem
{
    public required string Kind { get; init; }     // projeto · tag · sessao · acao · secao
    public required string Label { get; init; }
    public string Detail { get; init; } = "";
    public string Glyph { get; init; } = "";
    public int Score { get; set; }
    public DateTimeOffset Recency { get; init; }
    public required Action Run { get; init; }

    public string KindText => Kind.ToUpperInvariant();
}

/// <summary>
/// Ctrl+K: tudo e comando. Prefixos mudam o modo: `#` tag, `@` sessao, `>` acao, `$` secao;
/// sem prefixo busca em tudo. Vazio mostra recentes. Fuzzy por subsequencia, com bonus de
/// prefixo / inicio de palavra / consecutivos e desempate por recencia.
/// </summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private readonly ProjectCatalog _catalog;
    private readonly ISessionIndex _index;
    private readonly ILauncher _launcher;
    private readonly NavigationService _nav;

    public ObservableCollection<PaletteItem> Results { get; } = [];

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _selectedIndex = -1;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _hint = "";

    public PaletteViewModel(ProjectCatalog catalog, ISessionIndex index, ILauncher launcher, NavigationService nav)
    {
        _catalog = catalog;
        _index = index;
        _launcher = launcher;
        _nav = nav;
    }

    // Resolvido tarde: o ProjectsViewModel nasce com a primeira pagina.
    private ProjectsViewModel Projects => App.Services.GetRequiredService<ProjectsViewModel>();

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen) { Query = ""; Search(); }
    }

    public void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Search();

    public void Move(int delta)
    {
        if (Results.Count == 0) return;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    public void Execute()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        var item = Results[SelectedIndex];
        Close();
        try { item.Run(); }
        catch (Exception) { /* acao falhou: a UI-alvo mostra a mensagem */ }
    }

    public void Execute(PaletteItem item)
    {
        Close();
        try { item.Run(); }
        catch (Exception) { }
    }

    // ---------- busca ----------

    private void Search()
    {
        var raw = Query.Trim();
        string? kind = null;
        if (raw.Length > 0 && "#@>$".Contains(raw[0]))
        {
            kind = raw[0] switch { '#' => "tag", '@' => "sessao", '>' => "acao", _ => "secao" };
            raw = raw[1..].Trim();
        }

        IEnumerable<PaletteItem> pool = kind switch
        {
            "tag" => Tags(),
            "sessao" => Sessions(),
            "acao" => Actions(),
            "secao" => Sections(),
            _ => raw.Length == 0 ? Recents() : Projects_().Concat(Tags()).Concat(Sessions()).Concat(Actions()).Concat(Sections()),
        };

        var scored = raw.Length == 0
            ? pool.Take(40).ToList()
            : pool.Select(i => { i.Score = PaletteMatcher.Score(raw, i.Label, i.Detail); return i; })
                  .Where(i => i.Score > 0)
                  .OrderByDescending(i => i.Score).ThenByDescending(i => i.Recency)
                  .GroupBy(i => i.Kind).SelectMany(g => g.Take(8))
                  .OrderByDescending(i => i.Score).ThenByDescending(i => i.Recency)
                  .Take(40).ToList();

        Results.Clear();
        foreach (var i in scored) Results.Add(i);
        SelectedIndex = Results.Count > 0 ? 0 : -1;
        Hint = raw.Length == 0 && kind is null
            ? "recentes · prefixos: # tag  @ sessao  > acao  $ secao"
            : Results.Count == 0 ? "nada encontrado" : $"{Results.Count} resultados · Enter abre · Esc fecha";
    }

    private IEnumerable<PaletteItem> Recents()
    {
        foreach (var path in Projects.RecentPaths)
        {
            var p = _catalog.All.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (p is not null) yield return ProjectItem(p);
        }
        foreach (var s in _index.All.Where(r => !r.IsSubagent).Take(5)) yield return SessionItem(s);
        foreach (var a in Actions().Take(4)) yield return a;
    }

    private IEnumerable<PaletteItem> Projects_() => _catalog.All.Select(ProjectItem);

    private PaletteItem ProjectItem(ProjectInfo p)
    {
        var path = p.Path;
        return new PaletteItem
        {
            Kind = "projeto", Label = p.Name, Detail = p.Badges, Glyph = "\uE8B7", Recency = p.LastTouch,
            Run = () => _nav.GoToProject(path),
        };
    }

    private IEnumerable<PaletteItem> Tags() =>
        _catalog.All.SelectMany(p => p.AllTags).GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                var tag = "#" + g.Key;
                return new PaletteItem
                {
                    Kind = "tag", Label = tag, Detail = $"{g.Count()} projetos", Glyph = "\uE8EC",
                    Run = () => { Projects.Filter = tag; _nav.GoToSection("projects"); },
                };
            });

    private IEnumerable<PaletteItem> Sessions() =>
        _index.All.Where(r => !r.IsSubagent).Take(200).Select(SessionItem);

    private PaletteItem SessionItem(SessionRecord s)
    {
        var dir = s.Cwd.Length > 0 && Directory.Exists(s.Cwd) ? s.Cwd : ProjectScanner.Root;
        var id = s.Id;
        return new PaletteItem
        {
            Kind = "sessao", Label = s.Title,
            Detail = $"{ProjectInfo.Humanize(s.LastWrite)} · {s.Cwd}", Glyph = "\uE8F2", Recency = s.LastWrite,
            Run = () => _launcher.OpenClaude(dir, id),
        };
    }

    private IEnumerable<PaletteItem> Actions()
    {
        var pv = Projects;
        var sel = pv.Selected;
        var selName = sel?.Name ?? "(nenhum selecionado)";
        yield return Act("Rescan dos projetos", "reler pastas, STATUS e manifestos", () => pv.RescanCommand.Execute(null));
        yield return Act($"Claude aqui — {selName}", "abre o Windows Terminal com claude na pasta", () => pv.OpenClaudeCommand.Execute(null));
        yield return Act($"Terminal — {selName}", "", () => pv.OpenTerminalCommand.Execute(null));
        yield return Act($"Explorer — {selName}", "", () => pv.OpenExplorerCommand.Execute(null));
        yield return Act($"Copiar caminho — {selName}", "", () => pv.CopyPathCommand.Execute(null));
        yield return Act($"Rastro de novo — {selName}", "re-explora pasta, sessoes, mencoes, sistema", () => pv.RetrailCommand.Execute(null));
        yield return Act("Juntar contextos", "gera o .md com todos os STATUS em .factory\\contextos",
            () => { _nav.GoToSection("contexts"); App.Services.GetRequiredService<ContextsViewModel>().BundleCommand.Execute(null); });
        yield return Act("Esconder/mostrar lista", "", () => pv.ToggleListCommand.Execute(null));
        yield return Act("Audio do STATUS (ESTUDIO)", "carrega o STATUS do projeto selecionado no estudio de voz",
            () => { _nav.GoToSection("studio"); App.Services.GetRequiredService<StudioViewModel>().FromStatusCommand.Execute(null); });
        yield return Act("Audio das proximas acoes (ESTUDIO)", "resumo falado do que fazer em cada projeto",
            () => { _nav.GoToSection("studio"); App.Services.GetRequiredService<StudioViewModel>().FromNextActionsCommand.Execute(null); });
    }

    private static PaletteItem Act(string label, string detail, Action run) =>
        new() { Kind = "acao", Label = label, Detail = detail, Glyph = "\uE945", Run = run };

    private IEnumerable<PaletteItem> Sections()
    {
        foreach (var s in Navigation.SectionRegistry.Sections)
        {
            var tag = s.Tag;
            yield return new PaletteItem { Kind = "secao", Label = s.Title, Detail = $"ir para {s.Title.ToUpperInvariant()}", Glyph = s.Glyph, Run = () => _nav.GoToSection(tag) };
        }
    }
}

/// <summary>Fuzzy simples e previsivel: subsequencia obrigatoria; bonus por prefixo, inicio de palavra e consecutivos.</summary>
public static class PaletteMatcher
{
    public static int Score(string query, string label, string detail)
    {
        var s = ScoreOne(query, label);
        if (s > 0) return s + 50; // rotulo vale mais que detalhe
        return ScoreOne(query, detail);
    }

    private static int ScoreOne(string query, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var q = query.ToLowerInvariant();
        var t = text.ToLowerInvariant();

        if (t.StartsWith(q)) return 200 - Math.Min(50, t.Length - q.Length);
        var idx = t.IndexOf(q, StringComparison.Ordinal);
        if (idx >= 0) return 150 - Math.Min(50, idx) + (idx > 0 && IsSep(t[idx - 1]) ? 20 : 0);

        // subsequencia
        var score = 0;
        var ti = 0;
        var prev = -2;
        foreach (var c in q)
        {
            var found = t.IndexOf(c, ti);
            if (found < 0) return 0;
            score += 10;
            if (found == prev + 1) score += 3;
            if (found == 0 || IsSep(t[found - 1])) score += 6;
            score -= Math.Min(5, found - ti);
            prev = found;
            ti = found + 1;
        }
        return Math.Max(1, score);
    }

    private static bool IsSep(char c) => c is ' ' or '-' or '_' or '/' or '\\' or '.' or ':';
}
