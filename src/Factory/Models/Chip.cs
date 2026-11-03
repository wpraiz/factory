using System.Windows.Input;

namespace Factory.Models;

/// <summary>Um botaozinho clicavel: tag, nome de projeto, arquivo. Rotulo + acao.</summary>
public sealed class Chip
{
    public required string Label { get; init; }
    public int Count { get; init; }
    public required ICommand Command { get; init; }
    public string Tooltip { get; init; } = "";

    public string Text => Count > 0 ? $"{Label} {Count}" : Label;
}
