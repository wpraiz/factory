using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;
using Microsoft.UI.Dispatching;

namespace Factory.ViewModels;

public sealed partial class ProjectsViewModel : ObservableObject
{
    private readonly ProjectCatalog _catalog;
    private readonly ISessionIndex _index;
    private readonly ITrailService _trail;
    private readonly ILauncher _launcher;
    private readonly IManifestStore _manifests;
    private readonly NavigationService _nav;
    private readonly ISystemProbe _system;
    private readonly IBriefingService _brief;
    private readonly IStatusParser _statusParser;
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _briefCts;
    private Briefing? _lastBriefing;
    private SessionRecord? _briefSession;
    private readonly DispatcherQueueTimer _liveTimer;
    private CancellationTokenSource? _trailCts;
    private bool _indexWasReady;

    // historico de navegacao entre projetos (← →)
    private readonly List<string> _history = [];
    private int _histPos = -1;
    private bool _navigatingHistory;

    /// <summary>Lista agrupada (FIXADOS / ATIVO 7d / …) ja filtrada.</summary>
    public ObservableCollection<ProjectGroup> Groups { get; } = [];
    public ObservableCollection<Chip> TagCloud { get; } = [];
    public ObservableCollection<SessionInfo> OwnSessions { get; } = [];
    public ObservableCollection<SessionInfo> MentionSessions { get; } = [];
    public ObservableCollection<MentionHit> DocMentions { get; } = [];
    public ObservableCollection<Chip> RecentFiles { get; } = [];
    public ObservableCollection<Chip> EntryPoints { get; } = [];
    public ObservableCollection<LiveProcess> Processes { get; } = [];
    public ObservableCollection<ShortcutHit> Shortcuts { get; } = [];
    public ObservableCollection<TaskHit> Tasks { get; } = [];
    public IReadOnlyList<string> Kinds => ProjectManifest.Kinds;
    public IReadOnlyList<string> Stages => ProjectManifest.Stages;
    public IReadOnlyList<string> Models => ProjectManifest.Models;

    /// <summary>Caminhos dos ultimos projetos abertos (mais recente primeiro) — alimenta a palette.</summary>
    public IReadOnlyList<string> RecentPaths => _history.AsEnumerable().Reverse().Distinct().Take(8).ToList();

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _isListOpen = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(NoSelection))]
    private ProjectInfo? _selected;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;

    // ---- cards do canvas ----
    [ObservableProperty] private string _trailStatus = "";
    [ObservableProperty] private string _folderText = "";
    [ObservableProperty] private string _gitText = "";
    [ObservableProperty] private string _dbText = "";
    [ObservableProperty] private string _ownSessionsText = "";
    [ObservableProperty] private string _mentionSessionsText = "";
    [ObservableProperty] private string _docMentionsText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _systemText = "";
    [ObservableProperty] private string _stackText = "";

    // ---- briefing ----
    [ObservableProperty] private string _briefingText = "";
    [ObservableProperty] private string _briefingStatus = "";
    [ObservableProperty] private string _briefingTitle = "";
    [ObservableProperty] private bool _isBriefing;
    [ObservableProperty] private bool _hasBriefing;
    [ObservableProperty] private string _startCmdText = "";
    [ObservableProperty] private bool _hasStart;

    // ---- manifesto (edicao) ----
    [ObservableProperty] private string? _editKind;
    [ObservableProperty] private string? _editStage;
    [ObservableProperty] private string? _editModel;
    [ObservableProperty] private string _editMoney = "";
    [ObservableProperty] private string _editUrl = "";
    [ObservableProperty] private string _editNotes = "";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private double _editPotential;
    [ObservableProperty] private double _editEffort;
    [ObservableProperty] private double _editConfidence;
    [ObservableProperty] private double _editReach;
    [ObservableProperty] private string _editFirstMoneyDays = "";
    [ObservableProperty] private string _editRevenue = "";
    [ObservableProperty] private string _editRevenueTarget = "";
    [ObservableProperty] private bool _editPinned;

    public bool HasSelection => Selected is not null;
    public bool NoSelection => Selected is null;

    public ProjectsViewModel(ProjectCatalog catalog, ISessionIndex index, ITrailService trail, ILauncher launcher,
        IManifestStore manifests, NavigationService nav, ISystemProbe system, IBriefingService brief, IStatusParser statusParser)
    {
        _catalog = catalog;
        _system = system;
        _brief = brief;
        _statusParser = statusParser;
        _index = index;
        _trail = trail;
        _launcher = launcher;
        _manifests = manifests;
        _nav = nav;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _catalog.Changed += (_, _) => Refresh();
        _nav.ProjectRequested += (_, path) => SelectByPath(path);
        _index.Updated += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            // Quando o indice fica pronto, o rastro do projeto aberto ganha as sessoes que citam.
            if (_index.IsReady && !_indexWasReady)
            {
                _indexWasReady = true;
                if (Selected is { } p) _ = ExploreAsync(p);
            }
        });
        _indexWasReady = _index.IsReady;

        // LED "ao vivo" das sessoes: um stat por linha visivel a cada 30 s.
        _liveTimer = _dispatcher.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(30);
        _liveTimer.Tick += (_, _) =>
        {
            foreach (var s in OwnSessions) s.RefreshLive();
            foreach (var s in MentionSessions) s.RefreshLive();
        };
        _liveTimer.Start();

        Refresh();
    }

    // ---------- lista / filtro / tags ----------

    private void Refresh()
    {
        IsScanning = _catalog.IsScanning;
        RebuildTagCloud();
        ApplyFilter();
    }

    private void RebuildTagCloud()
    {
        TagCloud.Clear();
        var counts = _catalog.All.SelectMany(p => p.AllTags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(24);
        foreach (var g in counts)
        {
            var tag = "#" + g.Key;
            TagCloud.Add(new Chip
            {
                Label = tag, Count = g.Count(),
                Command = new RelayCommand(() => Filter = Filter.Equals(tag, StringComparison.OrdinalIgnoreCase) ? "" : tag),
            });
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private bool Matches(ProjectInfo p, string q) =>
        q.Length == 0
        || (q.StartsWith('#')
            ? p.HasTag(q)
            : p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
              || p.Badges.Contains(q, StringComparison.OrdinalIgnoreCase)
              || p.TagsText.Contains(q, StringComparison.OrdinalIgnoreCase)
              || p.NextAction.Contains(q, StringComparison.OrdinalIgnoreCase));

    private void ApplyFilter()
    {
        var q = Filter.Trim();
        var keepPath = Selected?.Path;

        var visible = _catalog.All.Where(p => Matches(p, q)).ToList();
        var buckets = visible.GroupBy(p => p.Bucket).ToDictionary(g => g.Key, g => g.ToList());

        Groups.Clear();
        foreach (var header in ProjectGroup.Order)
        {
            if (!buckets.TryGetValue(header, out var items) || items.Count == 0) continue;
            var g = new ProjectGroup { Header = header };
            foreach (var p in items.OrderByDescending(p => p.LastTouch)) g.Items.Add(p);
            Groups.Add(g);
        }
        CountText = $"{visible.Count} / {_catalog.All.Count}";

        // Clear() derruba o SelectedItem do ListView (TwoWay); restaura pelo caminho.
        if (keepPath is not null)
        {
            _navigatingHistory = true;
            Selected = visible.FirstOrDefault(p => p.Path == keepPath);
            _navigatingHistory = false;
        }
    }

    private bool IsVisible(ProjectInfo p) => Groups.Any(g => g.Items.Contains(p));

    // ---------- navegacao ----------

    public void SelectByPath(string path)
    {
        var p = _catalog.All.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (p is null) { Message = $"nao e um projeto da raiz: {path}"; return; }
        if (!IsVisible(p)) Filter = "";
        Selected = p;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_histPos <= 0) return;
        _histPos--;
        _navigatingHistory = true;
        SelectByPath(_history[_histPos]);
        _navigatingHistory = false;
        UpdateHistoryFlags();
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_histPos >= _history.Count - 1) return;
        _histPos++;
        _navigatingHistory = true;
        SelectByPath(_history[_histPos]);
        _navigatingHistory = false;
        UpdateHistoryFlags();
    }

    private void PushHistory(string path)
    {
        if (_navigatingHistory) return;
        if (_histPos >= 0 && _histPos < _history.Count && _history[_histPos] == path) return;
        if (_histPos < _history.Count - 1) _history.RemoveRange(_histPos + 1, _history.Count - _histPos - 1);
        _history.Add(path);
        if (_history.Count > 60) _history.RemoveAt(0);
        _histPos = _history.Count - 1;
        UpdateHistoryFlags();
    }

    private void UpdateHistoryFlags()
    {
        CanGoBack = _histPos > 0;
        CanGoForward = _histPos < _history.Count - 1;
    }

    private ICommand? GoCommandFor(string? anyPath)
    {
        if (string.IsNullOrEmpty(anyPath)) return null;
        var root = ProjectScanner.Root.TrimEnd('\\') + "\\";
        if (!anyPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = anyPath[root.Length..];
        var name = rest.Split('\\', '/')[0];
        var target = Path.Combine(ProjectScanner.Root, name);
        if (Selected is { } cur && target.Equals(cur.Path, StringComparison.OrdinalIgnoreCase)) return null;
        if (!_catalog.All.Any(p => p.Path.Equals(target, StringComparison.OrdinalIgnoreCase))) return null;
        return new RelayCommand(() => _nav.GoToProject(target));
    }

    // ---------- selecao / rastro ----------

    partial void OnSelectedChanged(ProjectInfo? value)
    {
        _trailCts?.Cancel();
        Message = "";
        ClearCards();
        if (value is null) return;

        PushHistory(value.Path);

        StatusText = File.Exists(Path.Combine(value.Path, "STATUS.md"))
            ? SafeRead(Path.Combine(value.Path, "STATUS.md"))
            : "— sem STATUS.md —\nAbra uma sessao do Claude aqui e feche com \"/status salvar\".";

        LoadManifestFields(value.Manifest);
        _ = ExploreAsync(value);
    }

    private void LoadManifestFields(ProjectManifest m)
    {
        EditKind = string.IsNullOrWhiteSpace(m.Kind) ? null : m.Kind;
        EditStage = string.IsNullOrWhiteSpace(m.Stage) ? null : m.Stage;
        EditModel = string.IsNullOrWhiteSpace(m.Model) ? null : m.Model;
        EditMoney = m.Money;
        EditUrl = m.Url;
        EditNotes = m.Notes;
        EditTags = string.Join(", ", m.Tags);
        EditPotential = m.Potential;
        EditEffort = m.Effort;
        EditConfidence = m.Confidence;
        EditReach = m.Reach;
        EditFirstMoneyDays = m.FirstMoneyDays > 0 ? m.FirstMoneyDays.ToString() : "";
        EditRevenue = m.RevenueMonthly > 0 ? m.RevenueMonthly.ToString("0.##", CultureInfo.InvariantCulture) : "";
        EditRevenueTarget = m.RevenueTarget > 0 ? m.RevenueTarget.ToString("0.##", CultureInfo.InvariantCulture) : "";
        EditPinned = m.Pinned;
    }

    private void ClearCards()
    {
        OwnSessions.Clear();
        MentionSessions.Clear();
        DocMentions.Clear();
        RecentFiles.Clear();
        EntryPoints.Clear();
        Processes.Clear();
        Shortcuts.Clear();
        Tasks.Clear();
        FolderText = GitText = DbText = SystemText = StackText = StartCmdText = "";
        HasStart = false;
        _briefCts?.Cancel();
        BriefingText = BriefingStatus = BriefingTitle = "";
        HasBriefing = false;
        IsBriefing = false;
        _lastBriefing = null;
        _briefSession = null;
        OwnSessionsText = MentionSessionsText = DocMentionsText = "";
        StatusText = "";
        TrailStatus = "";
    }

    private async Task ExploreAsync(ProjectInfo p)
    {
        _trailCts?.Cancel();
        var cts = _trailCts = new CancellationTokenSource();
        var progress = new Progress<string>(s => TrailStatus = s.Length == 0 ? "" : $"rastreando: {s}");
        TrailStatus = "rastreando…";

        TrailReport report;
        try { report = await _trail.ExploreAsync(p.Name, p.Path, progress, cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { TrailStatus = $"erro no rastro: {ex.Message}"; return; }
        if (cts.IsCancellationRequested || Selected != p) return;

        Apply(p, report);
    }

    private void Apply(ProjectInfo p, TrailReport r)
    {
        // ---- pasta ----
        if (r.Folder is { } f)
        {
            var sb = new StringBuilder();
            sb.Append($"{f.Files} arquivos · {f.Dirs} pastas · {ProjectInfo.HumanBytes(f.Bytes)}");
            if (f.Truncated) sb.Append(" (parcial)");
            sb.AppendLine();
            if (f.Exts.Count > 0) sb.AppendLine(string.Join(" · ", f.Exts.Select(e => $"{e.Ext} {e.Count}")));
            if (f.Oldest is { } o && f.Newest is { } n) sb.Append($"de {o:yyyy-MM-dd} a {n:yyyy-MM-dd HH:mm}");
            FolderText = sb.ToString().TrimEnd();

            RecentFiles.Clear();
            foreach (var x in f.Recent)
            {
                var full = Path.Combine(p.Path, x.RelPath);
                RecentFiles.Add(new Chip
                {
                    Label = $"{x.Modified:MM-dd HH:mm}  {x.RelPath}  ({ProjectInfo.HumanBytes(x.Size)})",
                    Command = new RelayCommand(() => _launcher.OpenFile(full)),
                    Tooltip = full,
                });
            }
            EntryPoints.Clear();
            foreach (var e in f.EntryPoints.Take(12))
            {
                var full = Path.Combine(p.Path, e);
                EntryPoints.Add(new Chip { Label = e, Command = new RelayCommand(() => _launcher.OpenFile(full)), Tooltip = full });
            }

            GitText = f.GitRemote.Length == 0 && f.GitLog.Count == 0
                ? (p.HasGit ? "git: sem remote" : "sem git")
                : (f.GitRemote.Length > 0 ? f.GitRemote + "\n" : "") + string.Join("\n", f.GitLog);

            StackText = f.Stack.Summary.Length == 0 ? "stack: nao reconhecida pelos manifestos da raiz" : f.Stack.Summary;
            StartCmdText = f.Stack.StartCommand ?? "";
            HasStart = f.Stack.HasStart;
        }

        // contexto pras linhas de sessao: o que mudou + fresh/stale/drifted
        var ctx = new SessionContext(p.Path, r.Folder?.Newest, p.GitBranch, full => new RelayCommand(() => _launcher.OpenFile(full)));

        // ---- bancos ----
        DbText = r.Databases.Count == 0 ? "nenhum .db/.sqlite ate 2 niveis" : string.Join("\n\n", r.Databases.Select(d =>
        {
            var head = $"{Path.GetRelativePath(p.Path, d.Path)}  ({ProjectInfo.HumanBytes(d.SizeBytes)})";
            if (d.Error is not null) return head + "\n  erro: " + d.Error;
            return head + "\n  " + string.Join(" · ", d.Tables.Select(t => $"{t.Name} {t.Rows}"));
        }));

        // ---- sessoes ----
        OwnSessions.Clear();
        foreach (var s in r.OwnSessions.Take(30))
            OwnSessions.Add(WithBrief(SessionInfo.From(s, 0, new RelayCommand(() => _launcher.OpenClaude(p.Path, s.Id)), null, ctx)));
        OwnSessionsText = r.OwnSessions.Count == 0
            ? (r.IndexWasReady ? "nenhuma sessao aberta nesta pasta" : "nenhuma na pasta (indice ainda construindo)")
            : $"{r.OwnSessions.Count} sessoes abertas nesta pasta";

        MentionSessions.Clear();
        foreach (var (s, hits) in r.MentionSessions.Take(30))
        {
            var dir = s.Cwd.Length > 0 && Directory.Exists(s.Cwd) ? s.Cwd : ProjectScanner.Root;
            MentionSessions.Add(WithBrief(SessionInfo.From(s, hits,
                new RelayCommand(() => _launcher.OpenClaude(dir, s.Id)),
                GoCommandFor(s.Cwd), ctx)));
        }
        MentionSessionsText = !r.IndexWasReady
            ? "indice de sessoes ainda construindo — volta sozinho quando pronto"
            : r.MentionSessions.Count == 0
                ? "nenhuma sessao de outra pasta cita este projeto"
                : $"{r.MentionSessions.Count} sessoes de OUTRAS pastas citam ou tocaram este projeto";

        // ---- sistema ----
        Processes.Clear();
        Shortcuts.Clear();
        Tasks.Clear();
        if (r.System is { } sys)
        {
            foreach (var x in sys.Processes) Processes.Add(x);
            foreach (var x in sys.Shortcuts) Shortcuts.Add(x);
            foreach (var x in sys.Tasks) Tasks.Add(x);
            var parts = new List<string>(4)
            {
                $"{sys.Processes.Count} processos vivos", $"{sys.Shortcuts.Count} atalhos", $"{sys.Tasks.Count} tasks agendadas",
            };
            if (sys.Errors.Count > 0) parts.Add("avisos: " + string.Join(" | ", sys.Errors));
            SystemText = string.Join(" · ", parts);
        }
        else SystemText = "";

        // ---- docs ----
        DocMentions.Clear();
        foreach (var h in r.DocMentions.Take(40))
        {
            var path = h.Path;
            h.OpenCommand = new RelayCommand(() => _launcher.OpenFile(path));
            h.GoCommand = GoCommandFor(path);
            DocMentions.Add(h);
        }
        DocMentionsText = r.DocMentions.Count == 0
            ? "nada em memoria, wiki, .factory ou raiz de outros projetos"
            : $"{r.DocMentions.Count} arquivos citam: " + string.Join(" · ",
                r.DocMentions.GroupBy(h => h.Source).Select(g => $"{g.Key} {g.Count()}"));

        TrailStatus = "";
    }

    // ---------- briefing ----------

    private SessionInfo WithBrief(SessionInfo si)
    {
        si.BriefCommand = new AsyncRelayCommand(() => BriefAsync(si));
        return si;
    }

    private async Task BriefAsync(SessionInfo si)
    {
        if (Selected is not { } p || IsBriefing) return;
        _briefCts?.Cancel();
        var cts = _briefCts = new CancellationTokenSource();
        IsBriefing = true;
        BriefingTitle = si.Title;
        _briefSession = si.Record;

        if (_brief.Cached(si.Id) is { } cached)
        {
            _lastBriefing = cached;
            BriefingText = cached.Text;
            HasBriefing = true;
            BriefingStatus = $"cache de {ProjectInfo.Humanize(cached.At)} · gerando de novo…";
        }
        else
        {
            BriefingStatus = _brief.CliAvailable ? "gerando com claude -p (5–15 s)…" : "claude CLI nao encontrado";
        }

        try
        {
            var b = await _brief.BuildAsync(si.Record, p, cts.Token);
            if (cts.IsCancellationRequested) return;
            _lastBriefing = b;
            BriefingText = b.Text;
            HasBriefing = true;
            BriefingStatus = $"gerado em {b.DurationMs / 1000.0:F1} s";
        }
        catch (OperationCanceledException) { BriefingStatus = "cancelado / tempo esgotado (120 s)"; }
        catch (Exception ex) { BriefingStatus = "erro: " + ex.Message; }
        finally { if (!cts.IsCancellationRequested) IsBriefing = false; }
    }

    public bool StatusFileExists(out string updated)
    {
        updated = "";
        if (Selected is not { } p) return false;
        var f = Path.Combine(p.Path, "STATUS.md");
        if (!File.Exists(f)) return false;
        updated = File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm");
        return true;
    }

    /// <summary>STATUS.md no formato da skill /status, montado do briefing. Vazio se nao ha briefing.</summary>
    public string BuildStatusPreview()
    {
        if (Selected is not { } p || _lastBriefing is not { } b) return "";
        var where = b.Where.Length > 0 ? b.Where : b.Text;
        var next = b.Next.Length > 0 ? b.Next : "(definir)";
        var blockers = b.Blockers.Length > 0 ? b.Blockers : "nenhum";
        var edited = _briefSession is { } s
            ? s.Touched.Where(t => t.IsEdit && t.Path.StartsWith(p.Path, StringComparison.OrdinalIgnoreCase))
                .Select(t => Path.GetRelativePath(p.Path, t.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList()
            : [];
        var sb = new StringBuilder();
        sb.AppendLine($"# STATUS — {p.Name}");
        sb.AppendLine($"Atualizado: {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## Onde paramos");
        sb.AppendLine(where);
        sb.AppendLine();
        sb.AppendLine("## Pronto (com caminho)");
        if (edited.Count == 0) sb.AppendLine("- (sem arquivos editados registrados na sessao)");
        foreach (var e in edited) sb.AppendLine($"- `{e}`");
        sb.AppendLine();
        sb.AppendLine("## Próxima ação");
        sb.AppendLine(next);
        sb.AppendLine();
        sb.AppendLine("## Bloqueios");
        sb.AppendLine(blockers);
        sb.AppendLine();
        sb.AppendLine($"_gerado pelo Factory a partir da sessao {(_briefSession?.ShortId ?? "?")} — revise antes de confiar_");
        return sb.ToString();
    }

    /// <summary>Chamado pela pagina depois da confirmacao. Faz .bak se ja existia.</summary>
    public void SaveStatus(string text)
    {
        if (Selected is not { } p) return;
        var f = Path.Combine(p.Path, "STATUS.md");
        try
        {
            if (File.Exists(f)) File.Copy(f, f + ".bak", overwrite: true);
            File.WriteAllText(f, text, new UTF8Encoding(false));
            var doc = _statusParser.Parse(text);
            p.StatusUpdated = doc.Updated;
            p.NextAction = doc.NextAction;
            p.Blockers = doc.Blockers;
            StatusText = text;
            Message = File.Exists(f + ".bak") ? "STATUS.md gravado (anterior em STATUS.md.bak)" : "STATUS.md criado";
            _ = _catalog.ScanAsync(); // HasStatus e init-only: o rescan atualiza a lista
        }
        catch (Exception ex)
        {
            Message = "erro ao gravar STATUS.md: " + ex.Message;
        }
    }

    // ---------- comandos ----------

    [RelayCommand]
    private Task RescanAsync() => _catalog.ScanAsync();

    [RelayCommand]
    private void ToggleList() => IsListOpen = !IsListOpen;

    [RelayCommand]
    private void OpenClaude() { if (Selected is { } p) _launcher.OpenClaude(p.Path); }

    [RelayCommand]
    private void OpenTerminal() { if (Selected is { } p) _launcher.OpenTerminal(p.Path); }

    [RelayCommand]
    private void OpenExplorer() { if (Selected is { } p) _launcher.OpenExplorer(p.Path); }

    [RelayCommand]
    private void CopyPath()
    {
        if (Selected is not { } p) return;
        _launcher.CopyToClipboard(p.Path);
        Message = "caminho copiado";
    }

    [RelayCommand]
    private void Retrail() { if (Selected is { } p) _ = ExploreAsync(p); }

    [RelayCommand]
    private void Start()
    {
        if (Selected is { } p && HasStart) _launcher.RunInTerminal(p.Path, StartCmdText);
    }

    /// <summary>Chamado pela pagina DEPOIS da confirmacao (ContentDialog precisa do XamlRoot).</summary>
    public void KillProcess(LiveProcess proc)
    {
        var ok = _system.Kill(proc.Pid);
        Message = ok ? $"PID {proc.Pid} ({proc.Name}) encerrado" : $"nao consegui encerrar PID {proc.Pid} (permissao? ja morreu?)";
        if (ok) Processes.Remove(proc);
    }

    [RelayCommand]
    private async Task SaveManifestAsync()
    {
        if (Selected is not { } p) return;
        var m = new ProjectManifest
        {
            Kind = EditKind ?? "",
            Stage = EditStage ?? "",
            Model = EditModel ?? "",
            Money = EditMoney.Trim(),
            Url = EditUrl.Trim(),
            Notes = EditNotes.Trim(),
            Tags = EditTags.Split(',', ';', ' ').Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
                .Where(t => t.Length > 0).Distinct().ToList(),
            Pinned = EditPinned,
            Potential = (int)Math.Round(EditPotential),
            Effort = (int)Math.Round(EditEffort),
            Confidence = (int)Math.Round(EditConfidence),
            Reach = (int)Math.Round(EditReach),
            FirstMoneyDays = ParseInt(EditFirstMoneyDays),
            RevenueMonthly = ParseMoney(EditRevenue),
            RevenueTarget = ParseMoney(EditRevenueTarget),
        };
        try
        {
            await _manifests.SaveAsync(p.Path, m);
            var wasPinned = p.Manifest.Pinned;
            p.Manifest = m;
            EditTags = string.Join(", ", m.Tags);
            RebuildTagCloud();
            if (wasPinned != m.Pinned) ApplyFilter(); // muda de faixa
            Message = $"factory.json salvo em {p.Name}";
        }
        catch (Exception ex)
        {
            Message = $"erro ao salvar: {ex.Message}";
        }
    }

    private static int ParseInt(string s) => int.TryParse(s.Trim(), out var n) ? Math.Max(0, n) : 0;

    private static double ParseMoney(string s)
    {
        var clean = s.Replace("R$", "").Replace(".", "").Replace(',', '.').Trim();
        return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0;
    }

    private static string SafeRead(string file)
    {
        try { return File.ReadAllText(file); }
        catch (IOException ex) { return $"(nao consegui ler: {ex.Message})"; }
    }
}
