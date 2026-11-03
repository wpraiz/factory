using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace Factory.Models;

/// <summary>
/// Um tile do grid de APPS: o que o usuario abre todo dia, escolhido por ele. Persistido em
/// %LOCALAPPDATA%\Factory\apps.json (ordem = ordem do grid). Icone e estado vivo nao
/// vao pro JSON — sao recalculados a cada abertura.
/// </summary>
public sealed partial class AppTile : ObservableObject
{
    public const string KindExe = "exe";           // .exe / .lnk / .cmd / .bat — abre pelo shell
    public const string KindUrl = "url";           // http(s):// — browser padrao
    public const string KindTerminal = "terminal"; // Windows Terminal na pasta, rodando Args (ou so abre se vazio)
    public const string KindClaude = "claude";     // Windows Terminal com `claude` na pasta
    public const string KindFolder = "folder";     // Explorer

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Kind { get; set; } = KindExe;
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
    public string WorkingDir { get; set; } = "";
    /// <summary>Segoe Fluent Icons; usado quando nao ha icone real do executavel.</summary>
    public string Glyph { get; set; } = "";
    /// <summary>cyan | green | magenta | amber — em memoria vem do grupo (AppsViewModel.RebuildGroups).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccentName))]
    private string _accent = "cyan";

    /// <summary>Faixa do grid (nome dado por ele). Vazio = FERRAMENTAS.</summary>
    [ObservableProperty]
    private string _group = "";

    /// <summary>Adicionado a mao (+ ARQUIVO): nunca e removido pela limpeza automatica.</summary>
    public bool Pinned { get; set; }

    /// <summary>Nomes de grupo existentes, pro seletor do tile em modo ESCOLHER (setado pelo ViewModel).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private IReadOnlyList<string> _groupOptions = [];

    [JsonIgnore] public string AccentName => Accent;

    /// <summary>1..9 = Ctrl+N abre este tile (posicao no grid); 0 = sem atalho. Setado pelo ViewModel.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(HotkeyText), nameof(HasHotkey))]
    private int _hotkey;

    [JsonIgnore] public string HotkeyText => Hotkey > 0 ? $"^{Hotkey}" : "";
    [JsonIgnore] public bool HasHotkey => Hotkey > 0;

    /// <summary>Alvo real (o .lnk resolvido) — so em memoria.</summary>
    [JsonIgnore] public string ResolvedTarget { get; set; } = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isRunning;

    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(NoIcon))]
    private ImageSource? _icon;

    /// <summary>No painel ESCOLHER: marcado = esta no grid.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _enabled = true;

    /// <summary>Grid em modo de edicao: mostra ↑ ↓ ✕ no tile.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(NotEditing))]
    private bool _editing;

    [JsonIgnore] public bool NotEditing => !Editing;
    [JsonIgnore] public bool HasIcon => Icon is not null;
    [JsonIgnore] public bool NoIcon => Icon is null;

    /// <summary>Chave de identidade pra casar candidato com tile salvo.</summary>
    [JsonIgnore] public string Key => $"{Kind}|{Target}|{Args}".ToLowerInvariant();

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        KindUrl => "web",
        KindTerminal => string.IsNullOrWhiteSpace(Args) ? "terminal" : "script",
        KindClaude => "claude",
        KindFolder => "pasta",
        _ => "app",
    };

    [JsonIgnore]
    public string CandidateText => string.IsNullOrWhiteSpace(Subtitle) ? Name : $"{Name}  ·  {Subtitle}";
}
