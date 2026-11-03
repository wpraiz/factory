using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;
using Microsoft.UI.Dispatching;

namespace Factory.ViewModels;

/// <summary>
/// Visao cruzada de TODAS as sessoes Claude Code (catalogadas em <raiz> ou nao — cruza
/// ISessionIndex.All, nao so ProjectCatalog): quem tem sessao viva agora, quem teve recente,
/// quem nunca teve. Lista unica (nunca duas colunas vazias); linhas com Manifest.Kind == "cliente"
/// ganham uma tag; sessao sem projeto catalogado ganha tag "nao catalogado".
/// </summary>
public sealed partial class FleetViewModel : ObservableObject
{
    private readonly ProjectCatalog _catalog;
    private readonly ISessionIndex _index;
    private readonly ILauncher _launcher;
    private readonly NavigationService _nav;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _liveTimer;
    private readonly Dictionary<string, FleetRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Faixas de tempo (redesign B/A): lista plana de dezenas virou 4 faixas;
    /// ANTES/NUNCA fica recolhida. Membership recalcula no Rebuild estrutural, nao no timer.</summary>
    public ObservableCollection<FleetRow> Vivos { get; } = [];
    public ObservableCollection<FleetRow> Hoje { get; } = [];
    public ObservableCollection<FleetRow> Semana { get; } = [];
    public ObservableCollection<FleetRow> Antes { get; } = [];

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _vivosTitulo = "";
    [ObservableProperty] private string _hojeTitulo = "";
    [ObservableProperty] private string _semanaTitulo = "";
    [ObservableProperty] private string _antesTitulo = "";
    [ObservableProperty] private bool _temVivos;
    [ObservableProperty] private bool _temHoje;
    [ObservableProperty] private bool _temSemana;
    [ObservableProperty] private bool _temAntes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AntesChevron))]
    private bool _antesAberto;

    /// <summary>E70D = chevron pra baixo (abrir), E70E = pra cima (fechar).</summary>
    public string AntesChevron => AntesAberto ? "" : "";

    [RelayCommand]
    private void AlternarAntes() => AntesAberto = !AntesAberto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoRows))]
    private bool _hasRows;

    /// <summary>!HasRows como propriedade propria — Converter={StaticResource Not} em cima de
    /// Visibility quebra em runtime (devolve bool, nao Visibility; achado no crash.txt).</summary>
    public bool NoRows => !HasRows;

    public FleetViewModel(ProjectCatalog catalog, ISessionIndex index, ILauncher launcher, NavigationService nav)
    {
        _catalog = catalog;
        _index = index;
        _launcher = launcher;
        _nav = nav;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _catalog.Changed += (_, _) => Rebuild();
        _index.Updated += (_, _) => _dispatcher.TryEnqueue(Rebuild);

        // So atualiza Live/Recent das linhas que ja existem — nunca recria a lista (ux-review:
        // clear+add a cada 30s piscava a tela e resetava o scroll). Projeto/sessao nova/removida
        // so aparece/some no proximo Rebuild() estrutural (catalogo ou indice mudou).
        _liveTimer = _dispatcher.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(30);
        _liveTimer.Tick += (_, _) => RefreshLiveOnly();
        _liveTimer.Start();

        Rebuild();
    }

    /// <summary>Recalcula quem existe e a ordem. So em mudanca estrutural real — nunca no timer.</summary>
    public void Rebuild()
    {
        Vivos.Clear();
        Hoje.Clear();
        Semana.Clear();
        Antes.Clear();
        _rows.Clear();

        var built = new List<FleetRow>(_catalog.All.Count + 8);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in _catalog.All)
        {
            known.Add(p.Path);
            built.Add(BuildRow(p.Name, p.Path, IsClienteKind(p), isOrphan: false));
        }

        // sessoes cujo cwd nao bate com nenhum projeto catalogado: cruza TUDO que o indice sabe,
        // nao so o que o ProjectCatalog ja catalogou (o problema real do usuario e 45 projetos, um
        // cerebro — nao vale ter sessao invisivel so por nao ter virado card ainda).
        if (_index.IsReady)
        {
            foreach (var cwd in _index.All
                         .Where(s => !s.IsSubagent && !string.IsNullOrWhiteSpace(s.Cwd) && !known.Contains(s.Cwd) && !PastaDeBuild(s.Cwd))
                         .Select(s => s.Cwd)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                known.Add(cwd);
                var name = Path.GetFileName(cwd.TrimEnd('\\', '/'));
                built.Add(BuildRow(string.IsNullOrEmpty(name) ? cwd : name, cwd, isCliente: false, isOrphan: true));
            }
        }

        var hoje = DateTimeOffset.Now.Date;
        foreach (var row in built.OrderByDescending(RankKey))
        {
            _rows[row.Path] = row;
            var quando = row.Recent is { } r ? SessionKey(r.Record) : (DateTimeOffset?)null;
            if (row.HasLive) Vivos.Add(row);
            else if (quando is { } q && q.LocalDateTime.Date == hoje) Hoje.Add(row);
            else if (quando is { } q2 && DateTimeOffset.Now - q2 <= TimeSpan.FromDays(7)) Semana.Add(row);
            else Antes.Add(row);
        }

        TemVivos = Vivos.Count > 0;
        TemHoje = Hoje.Count > 0;
        TemSemana = Semana.Count > 0;
        TemAntes = Antes.Count > 0;
        VivosTitulo = $"AO VIVO · {Vivos.Count}";
        HojeTitulo = $"HOJE · {Hoje.Count}";
        SemanaTitulo = $"ESTA SEMANA · {Semana.Count}";
        AntesTitulo = $"ANTES E NUNCA · {Antes.Count}";
        HasRows = _rows.Count > 0;
        UpdateSummary();
    }

    /// <summary>Atualiza sessao viva/recente das linhas existentes, sem mexer em ordem ou
    /// membership — e o que roda a cada 30s.</summary>
    private void RefreshLiveOnly()
    {
        foreach (var row in _rows.Values)
        {
            var sessions = _index.ForProject(row.Path);
            row.Live = LiveSessionInfo(row.Path, sessions);
            row.Recent = RecentSessionInfo(row.Path, sessions, row.Live);
        }

        UpdateSummary();
    }

    private FleetRow BuildRow(string name, string path, bool isCliente, bool isOrphan)
    {
        var sessions = _index.ForProject(path);
        var live = LiveSessionInfo(path, sessions);
        var recent = RecentSessionInfo(path, sessions, live);
        return new FleetRow { Name = name, Path = path, IsCliente = isCliente, IsOrphan = isOrphan, Live = live, Recent = recent };
    }

    private SessionInfo? LiveSessionInfo(string path, IReadOnlyList<SessionRecord> sessions)
    {
        var r = sessions.Where(s => s.IsLive).OrderByDescending(SessionKey).FirstOrDefault();
        return r is null ? null : SessionInfo.From(r, 0, ResumeCommand(path, r.Id), GoCommand(path));
    }

    private SessionInfo? RecentSessionInfo(string path, IReadOnlyList<SessionRecord> sessions, SessionInfo? live)
    {
        var r = sessions.OrderByDescending(SessionKey).FirstOrDefault();
        if (r is null || (live is not null && r.Id == live.Id)) return null;
        return SessionInfo.From(r, 0, ResumeCommand(path, r.Id), GoCommand(path));
    }

    private static DateTimeOffset SessionKey(SessionRecord s) => s.LastTs ?? s.LastWrite;

    /// <summary>
    /// cwd dentro de bin\ ou obj\ nao e trabalho do usuario: e o proprio Factory rodando `claude -p`
    /// (chat, briefing, SUGERIR) com cwd na pasta do exe (ClaudeCli). Apareciam como "publish" e
    /// "publish-homolog" na FROTA.
    /// </summary>
    private static bool PastaDeBuild(string cwd) =>
        cwd.Split('\\', '/').Any(seg => seg.Equals("bin", StringComparison.OrdinalIgnoreCase) || seg.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static bool IsClienteKind(ProjectInfo p) =>
        string.Equals(p.Manifest.Kind?.Trim(), "cliente", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ao vivo > recente > nunca; dentro de cada faixa, pela data da SESSAO — nao do
    /// mtime da pasta (ux-review: LastTouch deixava projeto so-editado furar fila na frente de
    /// projeto com sessao Claude recente). Linha orfa sem sessao nao existe (so aparece com sessao).</summary>
    private static (int Rank, DateTimeOffset When) RankKey(FleetRow row) => row switch
    {
        { HasLive: true } => (2, SessionKey(row.Live!.Record)),
        { HasRecentOnly: true } => (1, SessionKey(row.Recent!.Record)),
        _ => (0, DateTimeOffset.MinValue),
    };

    private void UpdateSummary()
    {
        var liveCount = _rows.Values.Count(r => r.HasLive);
        Summary = liveCount == 0 ? "nada rodando agora" : $"{liveCount} sessão(ões) viva(s) agora";
    }

    [RelayCommand]
    private Task RescanAsync() => _catalog.ScanAsync();

    public void Resume(FleetRow row) => _launcher.OpenClaude(row.Path, (row.Live ?? row.Recent)?.Id);
    public void Launch(FleetRow row) => _launcher.OpenClaude(row.Path);
    public void Terminal(FleetRow row) => _launcher.OpenTerminal(row.Path);
    public void Folder(FleetRow row) => _launcher.OpenExplorer(row.Path);
    public void Go(FleetRow row) => _nav.GoToProject(row.Path);

    private RelayCommand ResumeCommand(string path, string sessionId) => new(() => _launcher.OpenClaude(path, sessionId));

    private RelayCommand GoCommand(string path) => new(() => _nav.GoToProject(path));
}
