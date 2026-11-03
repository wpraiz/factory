using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

public sealed class StageGroup
{
    public required string Stage { get; init; }
    public required string Header { get; init; }
    public ObservableCollection<ProjectInfo> Items { get; } = [];
}

/// <summary>Um ponto no mapa potencial × esforco. Coordenadas ja em pixels do canvas.</summary>
public sealed class MoneyDot
{
    public required ProjectInfo Project { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    /// <summary>Tamanho = confianca (o que ja e real fica grande).</summary>
    public required double Size { get; init; }
    /// <summary>Primeiro real em ate 14 dias: anel verde.</summary>
    public required bool Highlight { get; init; }
    public required string Tooltip { get; init; }
    public required ICommand GoCommand { get; init; }
}

/// <summary>Linha do ranking "melhor R$ por esforco".</summary>
public sealed class MoneyRow
{
    public required ProjectInfo Project { get; init; }
    public required int Rank { get; init; }
    public required double Score { get; init; }
    public required ICommand GoCommand { get; init; }
    public string Name => Project.Name;
    public string Stars => $"{ProjectManifest.Stars(Project.Manifest.Potential)} / {ProjectManifest.Stars(Project.Manifest.Effort)} / {ProjectManifest.Stars(Project.Manifest.Confidence)}";
    public string ScoreText => Score.ToString("0.0");
    public string Revenue => Project.Manifest.RevenueMonthly > 0 ? $"R$ {Project.Manifest.RevenueMonthly:N0}/mes" : "R$ 0";
    public string Model => string.IsNullOrWhiteSpace(Project.Manifest.Model) ? "—" : Project.Manifest.Model;
    public string Stage => string.IsNullOrWhiteSpace(Project.Manifest.Stage) ? "—" : Project.Manifest.Stage;
    public string Soon => Project.FirstMoneySoon ? $"≤{Project.Manifest.FirstMoneyDays}d" : "";
}

/// <summary>
/// Le so o que esta declarado nos factory.json. Sem manifesto, o projeto nao aparece
/// aqui — o painel nao inventa faturamento.
/// </summary>
public sealed partial class MoneyViewModel : ObservableObject
{
    public const double PlotW = 720, PlotH = 460, Pad = 48;

    private readonly ProjectCatalog _catalog;
    private readonly ILauncher _launcher;
    private readonly NavigationService _nav;

    public ObservableCollection<StageGroup> Groups { get; } = [];
    public ObservableCollection<MoneyDot> Dots { get; } = [];
    public ObservableCollection<MoneyRow> Ranking { get; } = [];

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _totalsText = "";
    [ObservableProperty] private string _rankingText = "";
    /// <summary>false = ICE (potencial×confianca÷esforco); true = RICE (× alcance).</summary>
    [ObservableProperty] private bool _useRice;

    /// <summary>Avisa a pagina que os pontos mudaram (ela desenha no canvas).</summary>
    public event EventHandler? DotsChanged;

    public MoneyViewModel(ProjectCatalog catalog, ILauncher launcher, NavigationService nav)
    {
        _catalog = catalog;
        _launcher = launcher;
        _nav = nav;
        _catalog.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    partial void OnUseRiceChanged(bool value) => Rebuild();

    private double ScoreOf(ProjectInfo p) => UseRice ? p.Manifest.Rice : p.Manifest.Ice;

    public void Rebuild()
    {
        var declared = _catalog.All.Where(p => !p.Manifest.IsEmpty).ToList();
        var rated = declared.Where(p => p.Manifest.IsRated && p.Manifest.Stage != "morto").ToList();

        // ---- mapa ----
        Dots.Clear();
        var slots = new Dictionary<(int, int), int>();
        foreach (var p in rated.OrderByDescending(p => p.Manifest.ConfidenceOr3))
        {
            var m = p.Manifest;
            var key = (m.Effort, m.Potential);
            var n = slots.GetValueOrDefault(key);
            slots[key] = n + 1;
            // mesma celula: espalha em circulo pra nao empilhar
            var jitterX = n == 0 ? 0 : 22 * Math.Cos(n * 1.9);
            var jitterY = n == 0 ? 0 : 22 * Math.Sin(n * 1.9);
            var x = Pad + (m.Effort - 0.5) / 5.0 * PlotW + jitterX;
            var y = Pad + PlotH - (m.Potential - 0.5) / 5.0 * PlotH + jitterY;
            var size = 10 + m.ConfidenceOr3 * 5.0;
            var path = p.Path;
            Dots.Add(new MoneyDot
            {
                Project = p, X = x, Y = y, Size = size, Highlight = p.FirstMoneySoon,
                Tooltip = $"{p.Name}\npotencial {ProjectManifest.Stars(m.Potential)} · esforco {ProjectManifest.Stars(m.Effort)} · confianca {ProjectManifest.Stars(m.Confidence)}\n"
                          + $"ICE {m.Ice:0.0} · RICE {m.Rice:0.0}\n"
                          + (m.RevenueMonthly > 0 ? $"R$ {m.RevenueMonthly:N0}/mes · " : "R$ 0 · ")
                          + (string.IsNullOrWhiteSpace(m.Model) ? "sem modelo" : m.Model)
                          + $" · {p.StageText}"
                          + (m.FirstMoneyDays > 0 ? $"\n1º real em {m.FirstMoneyDays} d" : ""),
                GoCommand = new RelayCommand(() => _nav.GoToProject(path)),
            });
        }

        // ---- ranking ----
        Ranking.Clear();
        var rank = 0;
        foreach (var p in rated.OrderByDescending(ScoreOf).ThenByDescending(p => p.Manifest.RevenueMonthly).Take(12))
        {
            var path = p.Path;
            Ranking.Add(new MoneyRow { Project = p, Rank = ++rank, Score = ScoreOf(p), GoCommand = new RelayCommand(() => _nav.GoToProject(path)) });
        }
        RankingText = rated.Count == 0
            ? "avalie potencial e esforco no manifesto (canvas do projeto) pra este ranking existir"
            : $"{rated.Count} avaliados · ordenado por {(UseRice ? "RICE = alcance × potencial × confianca ÷ esforco" : "ICE = potencial × confianca ÷ esforco")} · estrelas: pot / esf / conf";

        // ---- totais ----
        var revenue = declared.Sum(p => p.Manifest.RevenueMonthly);
        var target = declared.Sum(p => p.Manifest.RevenueTarget);
        var earning = declared.Count(p => p.Manifest.RevenueMonthly > 0);
        TotalsText = $"R$ {revenue:N0}/mes declarados em {earning} projetos"
                     + (target > 0 ? $" · meta 90 d: R$ {target:N0}/mes" : "")
                     + $" · {rated.Count} avaliados · {_catalog.All.Count - declared.Count} sem manifesto";

        // ---- grupos por estagio ----
        Groups.Clear();
        foreach (var stage in ProjectManifest.Stages)
        {
            var items = declared
                .Where(p => string.Equals(p.Manifest.Stage, stage, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Manifest.RevenueMonthly).ThenBy(p => p.Name)
                .ToList();
            if (items.Count == 0) continue;
            var g = new StageGroup { Stage = stage, Header = $"{stage.ToUpperInvariant()} · {items.Count}" };
            foreach (var p in items) g.Items.Add(p);
            Groups.Add(g);
        }
        var noStage = declared.Where(p => string.IsNullOrWhiteSpace(p.Manifest.Stage)).OrderBy(p => p.Name).ToList();
        if (noStage.Count > 0)
        {
            var g = new StageGroup { Stage = "", Header = $"SEM STAGE · {noStage.Count}" };
            foreach (var p in noStage) g.Items.Add(p);
            Groups.Add(g);
        }

        var live = declared.Count(p => p.Manifest.Stage is "no-ar" or "vendendo");
        Summary = _catalog.IsScanning
            ? "varrendo…"
            : $"{declared.Count} com manifesto · {live} no ar/vendendo";

        DotsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenUrl(ProjectInfo p)
    {
        if (p.HasUrl) _launcher.OpenFile(p.Manifest.Url);
    }

    public void GoTo(ProjectInfo p) => _nav.GoToProject(p.Path);
}
