using CommunityToolkit.Mvvm.ComponentModel;

namespace Factory.Models;

/// <summary>
/// A planning item read from a source (STATUS.md, docs\plano-*.md) for the user to decide GOOD / BAD / LATER
/// inside Factory. As fontes so sao lidas; a decisao vai pra
/// <dados>\factory\decisoes.jsonl (append-only, decisao/v1).
/// </summary>
public sealed partial class BoardCard : ObservableObject
{
    /// <summary>"&lt;arquivo da fonte&gt;#&lt;hash estavel do titulo&gt;" — chave em decisoes.jsonl.</summary>
    public required string Id { get; init; }
    /// <summary>rumo | status | planos (filter).</summary>
    public required string Fonte { get; init; }
    /// <summary>Short badge: RUMO, STATUS, PLANO.</summary>
    public required string Selo { get; init; }
    /// <summary>Caminho absoluto do arquivo de onde o card saiu.</summary>
    public required string Caminho { get; init; }
    /// <summary>De onde, dentro do arquivo: "Próxima ação", "Bloqueios", "plano-odin"...</summary>
    public required string Secao { get; init; }
    public required string Titulo { get; init; }
    /// <summary>Markdown recortado (MarkdownLite).</summary>
    public string Resumo { get; init; } = "";
    public int Linha { get; init; }

    public string ArquivoNome => System.IO.Path.GetFileName(Caminho);
    public string FonteTooltip => Linha > 0 ? $"abrir {Caminho} (linha {Linha})" : $"abrir {Caminho}";
    public bool TemResumo => Resumo.Length > 0;

    [ObservableProperty] private string _nota = "";

    /// <summary>Caixa de nota aberta no tile ("+ nota"); fechada por padrao pra o card ficar limpo.</summary>
    [ObservableProperty] private bool _notaAberta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DecisaoRotulo), nameof(DecisaoDetalhe), nameof(Decidido))]
    private string? _decisao;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DecisaoDetalhe))]
    private DateTimeOffset? _decididoEm;

    /// <summary>Nota gravada junto da decisao (a caixa pode ter sido editada depois).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DecisaoDetalhe))]
    private string _notaGravada = "";

    public bool Decidido => !string.IsNullOrEmpty(Decisao);
    public string DecisaoRotulo => Decisao?.ToUpperInvariant() ?? "";

    public string DecisaoDetalhe
    {
        get
        {
            var quando = DecididoEm is { } d ? d.ToLocalTime().ToString("dd/MM HH:mm") : "";
            return string.IsNullOrWhiteSpace(NotaGravada) ? quando : $"{quando} · {NotaGravada}";
        }
    }
}

/// <summary>Uma linha de decisoes.jsonl (schema decisao/v1).</summary>
public sealed record DecisaoPlano(string Id, string Fonte, string Titulo, string Decisao, string Nota, DateTimeOffset Em);

/// <summary>Resultado de uma leitura das fontes: cards + o que faltou (a UI diz, nao inventa).</summary>
public sealed record PlanoLeitura(IReadOnlyList<BoardCard> Cards, IReadOnlyList<string> Faltas);
