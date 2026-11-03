using System.Collections.ObjectModel;

namespace Factory.Models;

/// <summary>Uma faixa do grid de APPS: nome (dado por ele), cor de acento e os tiles dela, na ordem do grid.</summary>
public sealed class AppGroup
{
    /// <summary>Sem verde: verde e so do LED "rodando", senao o estado some dentro da faixa verde.</summary>
    public static readonly string[] Palette = ["cyan", "magenta", "amber", "violet"];
    public const string Default = "FERRAMENTAS";

    public required string Name { get; init; }
    public required string Accent { get; init; }
    public ObservableCollection<AppTile> Tiles { get; } = [];

    public string CountText => Tiles.Count == 1 ? "1 app" : $"{Tiles.Count} apps";
}
