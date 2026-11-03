using CommunityToolkit.Mvvm.ComponentModel;

namespace Factory.Models;

/// <summary>Uma pasta de primeiro nivel em <raiz>, com tudo que o scanner conseguiu ler dela.</summary>
public sealed partial class ProjectInfo : ObservableObject
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool HasStatus { get; init; }
    public bool HasClaudeMd { get; init; }
    public bool HasReadme { get; init; }
    public bool HasGit { get; init; }

    /// <summary>Tags inferidas do conteudo da raiz (python, node, dotnet, wordpress…). Nao vao pro manifesto.</summary>
    public List<string> AutoTags { get; init; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastTouchText))]
    private DateTimeOffset _lastTouch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badges))]
    private string _statusUpdated = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextAction))]
    private string _nextAction = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockers))]
    private string _blockers = "";

    /// <summary>Sessoes abertas na propria pasta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badges))]
    private int _sessionCount;

    /// <summary>Sessoes de outras pastas que citam o projeto (so depois do indice pronto).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badges))]
    private int _mentionCount;

    [ObservableProperty] private DateTimeOffset? _lastSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badges))]
    private string _gitBranch = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badges))]
    private int _gitDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badges), nameof(StageText), nameof(MoneyText), nameof(KindText), nameof(HasUrl),
        nameof(AllTags), nameof(TagsText), nameof(HasTags), nameof(Score), nameof(Rice), nameof(PotentialText),
        nameof(HasPotential), nameof(IsPinned), nameof(FirstMoneySoon))]
    private ProjectManifest _manifest = new();

    public bool HasNextAction => !string.IsNullOrWhiteSpace(NextAction);
    public bool HasBlockers => !string.IsNullOrWhiteSpace(Blockers)
                               && !Blockers.Trim().Equals("nenhum", StringComparison.OrdinalIgnoreCase);
    public bool HasUrl => !string.IsNullOrWhiteSpace(Manifest.Url);
    public bool IsPinned => Manifest.Pinned;

    public string KindText => string.IsNullOrWhiteSpace(Manifest.Kind) ? "—" : Manifest.Kind;

    public string StageText =>
        string.IsNullOrWhiteSpace(Manifest.Kind) && string.IsNullOrWhiteSpace(Manifest.Stage)
            ? "—"
            : $"{Manifest.Kind}/{Manifest.Stage}".Trim('/');

    public string MoneyText => string.IsNullOrWhiteSpace(Manifest.Money) ? "—" : Manifest.Money;

    // ---- tags ----
    public IReadOnlyList<string> AllTags =>
        Manifest.Tags.Select(t => t.Trim().TrimStart('#')).Where(t => t.Length > 0)
            .Concat(AutoTags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public bool HasTags => AllTags.Count > 0;
    public string TagsText => string.Join("  ", AllTags.Select(t => "#" + t));
    public bool HasTag(string tag) => AllTags.Contains(tag.TrimStart('#'), StringComparer.OrdinalIgnoreCase);

    // ---- grana ----
    public double Score => Manifest.Ice;
    public double Rice => Manifest.Rice;
    public bool FirstMoneySoon => Manifest.FirstMoneySoon;
    public bool HasPotential => Manifest.IsRated || Manifest.RevenueMonthly > 0;
    public string PotentialText
    {
        get
        {
            if (!HasPotential) return "";
            var parts = new List<string>(4);
            if (Manifest.IsRated)
            {
                parts.Add($"pot {ProjectManifest.Stars(Manifest.Potential)}  esf {ProjectManifest.Stars(Manifest.Effort)}"
                          + (Manifest.Confidence > 0 ? $"  conf {ProjectManifest.Stars(Manifest.Confidence)}" : "")
                          + $"  ICE {Manifest.Ice:0.0}");
            }
            if (Manifest.RevenueMonthly > 0) parts.Add($"R$ {Manifest.RevenueMonthly:N0}/mes");
            if (Manifest.RevenueTarget > 0) parts.Add($"meta R$ {Manifest.RevenueTarget:N0}");
            if (Manifest.FirstMoneyDays > 0) parts.Add($"1º real em {Manifest.FirstMoneyDays} d");
            if (!string.IsNullOrWhiteSpace(Manifest.Model)) parts.Add(Manifest.Model);
            return string.Join(" · ", parts);
        }
    }

    public string Badges
    {
        get
        {
            var parts = new List<string>(6);
            if (HasStatus) parts.Add(string.IsNullOrEmpty(StatusUpdated) ? "STATUS" : $"STATUS {StatusUpdated}");
            if (HasClaudeMd) parts.Add("CLAUDE");
            if (HasGit) parts.Add(string.IsNullOrEmpty(GitBranch) ? "git" : GitDirty > 0 ? $"{GitBranch}*{GitDirty}" : GitBranch);
            if (SessionCount > 0 || MentionCount > 0)
                parts.Add(MentionCount > 0 ? $"{SessionCount} sess +{MentionCount}" : $"{SessionCount} sess");
            if (!Manifest.IsEmpty && StageText != "—") parts.Add(StageText);
            return parts.Count == 0 ? "sem metadados" : string.Join(" · ", parts);
        }
    }

    public string LastTouchText => Humanize(LastTouch);

    /// <summary>Faixa da lista agrupada.</summary>
    public string Bucket
    {
        get
        {
            if (IsPinned) return "FIXADOS";
            var age = DateTimeOffset.Now - LastTouch;
            if (SessionCount == 0 && !HasStatus && age > TimeSpan.FromDays(30)) return "NUNCA TOCADO";
            if (age <= TimeSpan.FromDays(7)) return "ATIVO 7d";
            if (age <= TimeSpan.FromDays(30)) return "ULTIMOS 30d";
            return "PARADO 30d+";
        }
    }

    public static string Humanize(DateTimeOffset t)
    {
        var now = DateTimeOffset.Now;
        var d = now - t;
        if (d < TimeSpan.FromMinutes(1)) return "agora";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min";
        if (t.Date == now.Date) return $"hoje {t:HH:mm}";
        if (t.Date == now.Date.AddDays(-1)) return $"ontem {t:HH:mm}";
        if (d < TimeSpan.FromDays(14)) return $"{(int)d.TotalDays} d";
        return t.ToString("yyyy-MM-dd");
    }

    public static string HumanBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{b / 1024.0 / 1024.0:F1} MB",
        _ => $"{b / 1024.0 / 1024.0 / 1024.0:F2} GB",
    };
}
