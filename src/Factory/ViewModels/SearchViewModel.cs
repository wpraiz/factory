using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

/// <summary>Busca livre: qualquer termo, em todas as sessoes e docs — nao precisa ser um projeto.</summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly ITrailService _trail;
    private readonly ProjectCatalog _catalog;
    private readonly ILauncher _launcher;
    private readonly ISessionIndex _index;
    private readonly NavigationService _nav;
    private CancellationTokenSource? _cts;

    public ObservableCollection<Chip> ProjectMatches { get; } = [];
    public ObservableCollection<SessionInfo> Sessions { get; } = [];
    public ObservableCollection<MentionHit> Docs { get; } = [];

    /// <summary>Estado vazio util (BUSCA nunca abre em branco): buscas recentes e
    /// sugestoes tiradas do estado real (projetos tocados por ultimo, tags mais usadas).</summary>
    public ObservableCollection<Chip> Recentes { get; } = [];
    public ObservableCollection<Chip> SugestoesProjetos { get; } = [];
    public ObservableCollection<Chip> SugestoesTags { get; } = [];

    [ObservableProperty] private string _term = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _projectsText = "";
    [ObservableProperty] private string _sessionsText = "";
    [ObservableProperty] private string _docsText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NaoBuscou))]
    private bool _buscou;

    [ObservableProperty] private bool _temRecentes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SemSugestoes))]
    private bool _temSugestoes;

    public bool NaoBuscou => !Buscou;
    public bool SemSugestoes => !TemSugestoes;

    // recent searches are user data (a choice) -> kept with the data, not in %LOCALAPPDATA%
    private static readonly string RecentesFile = Path.Combine(
        AppPaths.Factory, "buscas-recentes.json");

    public SearchViewModel(ITrailService trail, ProjectCatalog catalog, ILauncher launcher, ISessionIndex index, NavigationService nav)
    {
        _trail = trail;
        _catalog = catalog;
        _launcher = launcher;
        _index = index;
        _nav = nav;
        _catalog.Changed += (_, _) => MontarSugestoes();
        MontarSugestoes();
        MontarRecentes(LerRecentes());
    }

    private void MontarSugestoes()
    {
        if (_catalog.IsScanning) return;
        SugestoesProjetos.Clear();
        SugestoesTags.Clear();
        // chip de projeto vai direto pro projeto (1 clique), nao faz busca full-text (ux-review)
        foreach (var p in _catalog.All.OrderByDescending(p => p.LastTouch).Take(6))
        {
            var path = p.Path;
            SugestoesProjetos.Add(new Chip { Label = p.Name, Tooltip = $"abrir no canvas · tocado {p.LastTouchText}", Command = new RelayCommand(() => _nav.GoToProject(path)) });
        }
        foreach (var (tag, n) in _catalog.All.SelectMany(p => p.AllTags)
                     .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                     .Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).Take(8))
            SugestoesTags.Add(Termo(tag, $"{n} projetos com essa tag"));
        TemSugestoes = SugestoesProjetos.Count > 0;
    }

    private void MontarRecentes(IEnumerable<string> termos)
    {
        Recentes.Clear();
        foreach (var t in termos) Recentes.Add(Termo(t, "buscar de novo"));
        TemRecentes = Recentes.Count > 0;
    }

    private Chip Termo(string termo, string dica) => new()
    {
        Label = termo,
        Tooltip = dica,
        Command = new RelayCommand(() => { Term = termo; SearchCommand.Execute(null); }),
    };

    private static List<string> LerRecentes()
    {
        try { return File.Exists(RecentesFile) ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentesFile)) ?? [] : []; }
        catch (Exception) { return []; }
    }

    private void GuardarRecente(string term)
    {
        var list = LerRecentes();
        list.RemoveAll(t => t.Equals(term, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, term);
        if (list.Count > 6) list.RemoveRange(6, list.Count - 6);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentesFile)!);
            File.WriteAllText(RecentesFile, System.Text.Json.JsonSerializer.Serialize(list));
        }
        catch (Exception) { /* sem gravar recente a busca continua valendo */ }
        MontarRecentes(list);
    }

    [RelayCommand]
    private void Limpar()
    {
        Term = "";
        Buscou = false;
        Status = "";
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var term = Term.Trim();
        if (term.Length < 2) { Status = "digite pelo menos 2 letras — ou clique numa sugestao abaixo"; return; }

        Buscou = true;
        GuardarRecente(term);
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsBusy = true;
        Sessions.Clear();
        Docs.Clear();
        ProjectMatches.Clear();
        Status = _index.IsReady ? "buscando…" : "buscando (indice de sessoes ainda construindo — sessoes podem faltar)…";

        var matches = _catalog.All.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                              || p.HasTag(term)).ToList();
        foreach (var p in matches)
        {
            var path = p.Path;
            ProjectMatches.Add(new Chip { Label = p.Name, Command = new RelayCommand(() => _nav.GoToProject(path)), Tooltip = path });
        }
        ProjectsText = matches.Count == 0 ? "nenhuma pasta na raiz com esse nome ou tag" : $"{matches.Count} pastas — clique pra abrir no canvas:";

        try
        {
            var r = await _trail.ExploreAsync(term, null, new Progress<string>(s => Status = s), cts.Token);
            if (cts.IsCancellationRequested) return;

            foreach (var (s, hits) in r.MentionSessions.Take(60))
            {
                var dir = s.Cwd.Length > 0 && Directory.Exists(s.Cwd) ? s.Cwd : ProjectScanner.Root;
                Sessions.Add(SessionInfo.From(s, hits, new RelayCommand(() => _launcher.OpenClaude(dir, s.Id)), GoFor(s.Cwd)));
            }
            SessionsText = r.MentionSessions.Count == 0 ? "nenhuma sessao cita" : $"{r.MentionSessions.Count} sessoes citam";

            foreach (var h in r.DocMentions.Take(60))
            {
                var path = h.Path;
                h.OpenCommand = new RelayCommand(() => _launcher.OpenFile(path));
                h.GoCommand = GoFor(path);
                Docs.Add(h);
            }
            DocsText = r.DocMentions.Count == 0 ? "nenhum arquivo cita" : $"{r.DocMentions.Count} arquivos citam";
            Status = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = $"a busca falhou ({ex.Message}) — tente de novo; se repetir, o indice de sessoes pode estar reconstruindo"; }
        finally { IsBusy = false; }
    }

    private RelayCommand? GoFor(string? anyPath)
    {
        if (string.IsNullOrEmpty(anyPath)) return null;
        var root = ProjectScanner.Root.TrimEnd('\\') + "\\";
        if (!anyPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        var name = anyPath[root.Length..].Split('\\', '/')[0];
        var target = Path.Combine(ProjectScanner.Root, name);
        return _catalog.All.Any(p => p.Path.Equals(target, StringComparison.OrdinalIgnoreCase))
            ? new RelayCommand(() => _nav.GoToProject(target))
            : null;
    }
}
