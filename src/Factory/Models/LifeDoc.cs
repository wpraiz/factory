using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Factory.Models;

/// <summary>
/// Um documento da Wiki Vida (CV, portfolio, certificado...). Vem de <wiki>\vida.json,
/// so leitura: o app nunca escreve nele. Caminho relativo a pasta do JSON ou absoluto.
/// </summary>
public sealed partial class LifeDoc : ObservableObject
{
    [JsonPropertyName("titulo")] public string Title { get; set; } = "";
    [JsonPropertyName("categoria")] public string Category { get; set; } = "";
    /// <summary>pt-BR | en | vazio (sem idioma, ex.: contrato).</summary>
    [JsonPropertyName("idioma")] public string Lang { get; set; } = "";
    [JsonPropertyName("caminho")] public string Path { get; set; } = "";
    /// <summary>Documento vigente do tipo/idioma: selo ATUAL.</summary>
    [JsonPropertyName("atual")] public bool Current { get; set; }
    /// <summary>Versao antiga: fica recolhida em ARQUIVO.</summary>
    [JsonPropertyName("arquivo")] public bool Archived { get; set; }
    /// <summary>Pra que serve (legenda do tile).</summary>
    [JsonPropertyName("uso")] public string Use { get; set; } = "";

    // ---------- so em memoria (resolvido pelo LifeLibrary / ViewModel) ----------

    /// <summary>Caminho absoluto, ja resolvido.</summary>
    [JsonIgnore] public string FullPath { get; set; } = "";
    [JsonIgnore] public bool Exists { get; set; }
    [JsonIgnore] public bool Missing => !Exists;

    /// <summary>cyan | green | magenta | amber | violet — herdado da categoria.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string _accent = "cyan";

    /// <summary>Mouse em cima: mostra a fileira de mini-botoes (copiar arquivo / caminho / pasta).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _hovered;

    /// <summary>Confirmacao local no tile ("✓ ARQUIVO COPIADO") por ~1,5 s; vazio = mostra os mini-botoes.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(Flashing), nameof(NotFlashing))]
    private string _flash = "";

    [JsonIgnore] public bool Flashing => Flash.Length > 0;
    [JsonIgnore] public bool NotFlashing => Flash.Length == 0;

    /// <summary>Selo ATUAL so quando o arquivo existe: se o caminho quebrou, o magenta do aviso e o unico acento.</summary>
    [JsonIgnore] public bool ShowCurrent => Current && Exists;

    /// <summary>Rotulo de tipo so quando NAO e PDF (quase tudo e PDF; o canto fica pro idioma e pro ATUAL).</summary>
    [JsonIgnore] public bool ShowKind => !KindLabel.Equals("PDF", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore] public string FileName => System.IO.Path.GetFileName(FullPath.Length > 0 ? FullPath : Path);
    [JsonIgnore] public string Folder => System.IO.Path.GetDirectoryName(FullPath) ?? "";

    [JsonIgnore] public bool HasLang => Lang.Length > 0;

    /// <summary>Bandeira do idioma. No Windows a bandeira pode cair em "BR"/"US"; por isso o codigo vai ao lado.</summary>
    [JsonIgnore]
    public string Flag => Lang.ToLowerInvariant() switch
    {
        "pt-br" or "pt" => "\U0001F1E7\U0001F1F7", // 🇧🇷
        "en" or "en-us" => "\U0001F1FA\U0001F1F8", // 🇺🇸
        _ => "",
    };

    [JsonIgnore]
    public string LangLabel => Lang.ToLowerInvariant() switch
    {
        "pt-br" or "pt" => "PT-BR",
        "en" or "en-us" => "EN",
        _ => Lang.ToUpperInvariant(),
    };

    /// <summary>Tipo do arquivo em 3-4 letras (PDF, DOCX, HTML...).</summary>
    [JsonIgnore]
    public string KindLabel
    {
        get
        {
            var ext = System.IO.Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
            return ext.Length == 0 ? "ARQ" : ext;
        }
    }

    /// <summary>Segoe Fluent Icons por tipo: E8A5 documento (PDF e afins), E8B7 pasta, E774 web.</summary>
    [JsonIgnore]
    public string Glyph => System.IO.Path.GetExtension(FileName).ToLowerInvariant() switch
    {
        ".html" or ".htm" or ".url" => "\uE774",
        "" => "\uE8B7",
        _ => "\uE8A5",
    };

    /// <summary>Legenda: o uso; se o arquivo sumiu, diz isso no lugar.</summary>
    [JsonIgnore] public string Subtitle => Exists ? Use : $"nao achei: {FileName} — confira em vida.json";

    [JsonIgnore] public string Tooltip => Exists ? $"{FullPath}\nclique abre · arraste pra anexar · Ctrl+C copia o arquivo" : $"nao existe: {FullPath}";
}
