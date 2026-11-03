using CommunityToolkit.Mvvm.ComponentModel;

namespace Factory.Models;

/// <summary>
/// Uma peca do setup do Claude Code (SETUP): skill, agente, plugin, servidor MCP ou hook.
/// Inventario vem do disco (~/.claude, plugins, <raiz>\*\.claude); uso vem do scan das sessoes.
/// So a marca de joia muda depois de criada (clique do usuario).
/// </summary>
public sealed partial class SetupItem : ObservableObject
{
    /// <summary>skill | agente | plugin | mcp | hook</summary>
    public required string Tipo { get; init; }
    public required string Nome { get; init; }
    public string Descricao { get; init; } = "";
    /// <summary>"usuário", "plugin X", "projeto X", "sem arquivo" (embutido/conector: usado mas nao instalado).</summary>
    public required string Origem { get; init; }
    public string Caminho { get; init; } = "";
    /// <summary>Plugin desligado em enabledPlugins: nothing from it loads (skill/agente/MCP do plugin tambem).</summary>
    public bool Desligado { get; init; }
    /// <summary>Hook nao aparece como tool_use nas sessoes: uso nao e medido, entao nunca vira "morto".</summary>
    public bool Medido { get; init; } = true;

    /// <summary>Chave de uso/joia: "skill:status", "agente:ux-reviewer", "mcp:playwright", "plugin:pdf-viewer".</summary>
    public string Chave => $"{Tipo}:{Nome}";

    public int Vezes { get; set; }
    public string Ultimo { get; set; } = "";
    public int Projetos { get; set; }
    public bool IsNucleo { get; set; }
    public bool IsMorto { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selo), nameof(JoiaOpacity), nameof(JoiaTooltip))]
    private bool _isJoia;

    /// <summary>Selo na frente do nome: 💎 (marcada a mao) vem antes de 🔥/🪦.</summary>
    public string Selo => (IsJoia ? "💎" : "") + (IsNucleo ? "🔥" : IsMorto ? "🪦" : "");

    public double JoiaOpacity => IsJoia ? 1.0 : 0.3;
    public string JoiaTooltip => IsJoia ? "é jóia — clique pra desmarcar" : "marcar como jóia (guardado em .factory\\factory\\setup-joias.json)";

    public string TipoRotulo => Tipo switch
    {
        "skill" => "SKILL",
        "agente" => "AGENTE",
        "plugin" => "PLUGIN",
        "mcp" => "MCP",
        "hook" => "HOOK",
        _ => Tipo.ToUpperInvariant(),
    };

    public string OrigemTexto => Origem + (Desligado ? " · desligado" : "");

    public bool TemDescricao => !string.IsNullOrWhiteSpace(Descricao);

    public string UsoTexto => !Medido ? "—" : Vezes == 0 ? "0 usos" : Vezes == 1 ? "1 uso" : $"{Vezes} usos";

    public string UsoDetalhe => !Medido
        ? "uso não medido"
        : Vezes == 0 ? "nunca no histórico" : $"últ. {DataCurta(Ultimo)} · {Projetos} proj.";

    /// <summary>Linha morta/desligada fica mais fraca: o olho vai primeiro no que esta vivo.</summary>
    public double RowOpacity => Desligado || IsMorto ? 0.6 : 1.0;

    public bool TemCaminho => !string.IsNullOrEmpty(Caminho);

    private static string DataCurta(string iso) =>
        iso.Length >= 10 ? $"{iso[8..10]}/{iso[5..7]}" : iso;
}

/// <summary>Chip de filtro (tipo ou selo) com contagem e estado marcado.</summary>
public sealed partial class SetupFiltro : ObservableObject
{
    public required string Chave { get; init; }
    public required string Rotulo { get; init; }
    public string Tooltip { get; init; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Texto))]
    private int _contagem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Borda), nameof(Frente))]
    private bool _marcado;

    public string Texto => $"{Rotulo} {Contagem}";

    // cor por estado sai do tema; resolvida aqui pra nao precisar de converter novo
    public Microsoft.UI.Xaml.Media.Brush Borda => Recurso(Marcado ? "NeonCyan" : "CyberPanelEdge");
    public Microsoft.UI.Xaml.Media.Brush Frente => Recurso(Marcado ? "NeonCyan" : "CyberText");

    private static Microsoft.UI.Xaml.Media.Brush Recurso(string key) =>
        (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
}

/// <summary>Uma mao do Odin (Assets\hands.json).</summary>
public sealed record MaoOdin(string Nome, string Tags);

/// <summary>Um script de tools\ com a 1a linha de comentario.</summary>
public sealed record ScriptFactory(string Nome, string Resumo, string Caminho);

/// <summary>Um lugar onde o Factory escreve (lista estatica, espelha o CLAUDE.md).</summary>
public sealed record EscritaFactory(string Onde, string Oque);
