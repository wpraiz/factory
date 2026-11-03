using CommunityToolkit.Mvvm.ComponentModel;

namespace Factory.Models;

/// <summary>
/// Uma linha da FROTA: uma pasta com sessao Claude Code (viva, recente ou nunca) — catalogada em
/// <raiz> ou nao (sessao "orfa": cwd que nao virou projeto). A instancia fica viva entre
/// atualizacoes — Live/Recent trocam in-place a cada 30 s, nunca recriando a linha, pra nao
/// piscar a tela nem perder o scroll.
/// </summary>
public sealed partial class FleetRow : ObservableObject
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool IsCliente { get; init; }
    public bool IsOrphan { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLive), nameof(HasRecentOnly), nameof(HasNone), nameof(HasSession), nameof(StatusText), nameof(ResumeTooltip), nameof(RowOpacity))]
    private SessionInfo? _live;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLive), nameof(HasRecentOnly), nameof(HasNone), nameof(HasSession), nameof(StatusText), nameof(ResumeTooltip), nameof(RowOpacity))]
    private SessionInfo? _recent;

    public bool HasLive => Live is not null;
    public bool HasRecentOnly => Live is null && Recent is not null;
    public bool HasNone => Live is null && Recent is null;
    /// <summary>!HasNone como propriedade propria — x:Bind com Converter={StaticResource Not}
    /// em cima de Visibility quebra (Not devolve bool, não Visibility; InvalidCastException em
    /// runtime, visto no crash.txt). x:Bind sem converter faz a conversão bool→Visibility sozinho.</summary>
    public bool HasSession => HasLive || HasRecentOnly;

    /// <summary>Linha "nunca rodou" fica visualmente mais fraca — sem isso ela e "recente"
    /// eram indistinguiveis a nao ser lendo a frase inteira (ux-review).</summary>
    public double RowOpacity => HasNone ? 0.55 : 1.0;

    public string ResumeTooltip => (Live ?? Recent)?.Title ?? "";

    public string StatusText
    {
        get
        {
            if (Live is not null)
            {
                var d = Live.Record.Duration;
                var when = d is null || d.Value.TotalMinutes < 1 ? "agora"
                    : d.Value.TotalHours >= 1 ? $"{(int)d.Value.TotalHours}h{d.Value.Minutes:00}"
                    : $"{(int)d.Value.TotalMinutes} min";
                return $"ao vivo há {when}";
            }
            if (Recent is not null)
                return $"última: {ProjectInfo.Humanize(Recent.Record.LastTs ?? Recent.Record.LastWrite)}";
            return "nunca rodou aqui — clique + NOVA";
        }
    }
}
