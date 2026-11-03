namespace Factory.Models;

/// <summary>Um lugar pra onde uma entrada pode ir: favorito do destinos.json, projeto do catalogo
/// ou subpasta de projeto (projeto aninhado, ex.: site\blog).</summary>
public sealed record Destino(string Nome, string Caminho, string Origem)
{
    /// <summary>Texto da sugestao no AutoSuggestBox.</summary>
    public override string ToString() => $"{Nome}  ·  {Caminho}";
}

/// <summary>O que o `claude -p` devolve no SUGERIR. Tudo editavel depois.</summary>
public sealed record Sugestao(string? Destino, string Tipo, string Prioridade, string Titulo, int Quantidade);

/// <summary>Um .md ja gravado em &lt;destino&gt;\inbox\ (frontmatter entrada/v1).</summary>
public sealed record InboxItem(string Titulo, string Projeto, string Tipo, string Prioridade, string Status,
    DateTimeOffset Criado, string Arquivo, string Pasta)
{
    public string Meta => $"{Projeto} · {Tipo} · {ProjectInfo.Humanize(Criado)} · {Status}";
    public string NomeArquivo => System.IO.Path.GetFileName(Arquivo);
}
