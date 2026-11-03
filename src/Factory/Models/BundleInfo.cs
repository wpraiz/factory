using System.Windows.Input;

namespace Factory.Models;

/// <summary>Um arquivo de contextos ja gerado em .factory\contextos.</summary>
public sealed class BundleInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required ICommand OpenCommand { get; init; }
}
