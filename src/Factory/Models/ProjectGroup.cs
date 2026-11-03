using System.Collections.ObjectModel;

namespace Factory.Models;

/// <summary>Faixa da lista de projetos (FIXADOS, ATIVO 7d, …). Fonte do CollectionViewSource agrupado.</summary>
public sealed class ProjectGroup
{
    public static readonly string[] Order = ["FIXADOS", "ATIVO 7d", "ULTIMOS 30d", "PARADO 30d+", "NUNCA TOCADO"];

    public required string Header { get; init; }
    public ObservableCollection<ProjectInfo> Items { get; } = [];
    public string HeaderText => $"// {Header} · {Items.Count}";
}
